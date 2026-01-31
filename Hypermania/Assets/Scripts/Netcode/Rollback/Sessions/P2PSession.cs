using System;
using System.Collections.Generic;
using System.Linq;
using Netcode.Rollback.Network;
using UnityEngine;
using UnityEngine.Assertions;
using Utils;

namespace Netcode.Rollback.Sessions
{
    public class PlayerRegisty<TInput, TAddress>
        where TInput : IInput<TInput>
    {
        public Dictionary<PlayerHandle, PlayerType<TAddress>> Handles;
        public Dictionary<TAddress, UdpProtocol<TInput, TAddress>> Remotes;
        public Dictionary<TAddress, UdpProtocol<TInput, TAddress>> Spectators;

        public PlayerRegisty()
        {
            Handles = new Dictionary<PlayerHandle, PlayerType<TAddress>>();
            Remotes = new Dictionary<TAddress, UdpProtocol<TInput, TAddress>>();
            Spectators = new Dictionary<TAddress, UdpProtocol<TInput, TAddress>>();
        }

        public bool IsLocal(PlayerHandle handle) =>
            Handles.TryGetValue(handle, out PlayerType<TAddress> type) && type.Kind == PlayerKind.Local;

        public bool IsRemote(PlayerHandle handle) =>
            Handles.TryGetValue(handle, out PlayerType<TAddress> type) && type.Kind == PlayerKind.Remote;

        public bool IsSpectator(PlayerHandle handle) =>
            Handles.TryGetValue(handle, out PlayerType<TAddress> type) && type.Kind == PlayerKind.Spectator;

        public IEnumerable<PlayerHandle> LocalPlayerHandles()
        {
            foreach ((PlayerHandle handle, PlayerType<TAddress> type) in Handles)
            {
                if (type.Kind == PlayerKind.Local)
                    yield return handle;
            }
        }

        public IEnumerable<PlayerHandle> RemotePlayerHandles()
        {
            foreach ((PlayerHandle handle, PlayerType<TAddress> type) in Handles)
            {
                if (type.Kind == PlayerKind.Remote)
                    yield return handle;
            }
        }

        public IEnumerable<PlayerHandle> SpectatorHandles()
        {
            foreach ((PlayerHandle handle, PlayerType<TAddress> type) in Handles)
            {
                if (type.Kind == PlayerKind.Spectator)
                    yield return handle;
            }
        }

        public int NumPlayers()
        {
            int cnt = 0;
            foreach (PlayerType<TAddress> type in Handles.Values)
            {
                if (type.Kind == PlayerKind.Local || type.Kind == PlayerKind.Remote)
                {
                    cnt++;
                }
            }
            return cnt;
        }

        public int NumSpectators()
        {
            int cnt = 0;
            foreach (PlayerType<TAddress> type in Handles.Values)
            {
                if (type.Kind == PlayerKind.Spectator)
                {
                    cnt++;
                }
            }
            return cnt;
        }
    }

    public class P2PSession<TState, TInput, TAddress>
        where TState : IState<TState>
        where TInput : struct, IInput<TInput>
    {
        const uint MIN_RECOMMENDATION = 3;
        const int RECOMMENDATION_INTERVAL = 60;

        private int _numPlayers;
        private uint _maxPrediction;
        private SyncLayer<TState, TInput> _syncLayer;
        private bool _sparseSaving;
        private Frame _disconnectFrame;
        private SessionState _state;
        private INonBlockingSocket<TAddress> _socket;
        private PlayerRegisty<TInput, TAddress> _playerRegistry;
        private ConnectionStatus[] _localConnectStatus;
        private Frame _nextSpectatorFrame;
        private Frame _nextRecommendedSleep;
        private int _framesAhead;
        private Deque<RollbackEvent<TInput, TAddress>> _eventQueue;
        private Dictionary<PlayerHandle, PlayerInput<TInput>> _localInputs;
        private DesyncDetection _desyncDetection;
        private Dictionary<Frame, ulong> _localChecksumHistory;
        private Frame _lastSentChecksumFrame;

        public P2PSession(
            int numPlayers,
            uint maxPrediction,
            INonBlockingSocket<TAddress> socket,
            PlayerRegisty<TInput, TAddress> players,
            bool sparseSaving,
            in DesyncDetection desyncDetection,
            uint inputDelay
        )
        {
            _localConnectStatus = new ConnectionStatus[numPlayers];
            for (int i = 0; i < numPlayers; i++)
            {
                _localConnectStatus[i] = ConnectionStatus.Default;
            }
            _syncLayer = new SyncLayer<TState, TInput>(numPlayers, maxPrediction);
            foreach ((PlayerHandle handle, PlayerType<TAddress> type) in players.Handles)
            {
                if (type.Kind == PlayerKind.Local)
                {
                    _syncLayer.SetFrameDelay(handle, inputDelay);
                }
            }
            _state =
                players.Remotes.Count + players.Spectators.Count == 0
                    ? SessionState.Running
                    : SessionState.Synchronizing;
            _sparseSaving = (maxPrediction == 0 && sparseSaving) ? false : sparseSaving;

            _numPlayers = numPlayers;
            _maxPrediction = maxPrediction;
            _socket = socket;
            _nextRecommendedSleep = Frame.FirstFrame;
            _nextSpectatorFrame = Frame.FirstFrame;
            _framesAhead = 0;
            _disconnectFrame = Frame.NullFrame;
            _playerRegistry = players;
            _eventQueue = new Deque<RollbackEvent<TInput, TAddress>>();
            _localInputs = new Dictionary<PlayerHandle, PlayerInput<TInput>>();
            _desyncDetection = desyncDetection;
            _localChecksumHistory = new Dictionary<Frame, ulong>();
            _lastSentChecksumFrame = Frame.NullFrame;
        }

        public void AddLocalInput(PlayerHandle playerHandle, TInput input)
        {
            if (!_playerRegistry.IsLocal(playerHandle))
            {
                throw new InvalidOperationException("player handle is not a local handle");
            }
            PlayerInput<TInput> playerInput = new PlayerInput<TInput>
            {
                Frame = _syncLayer.CurrentFrame,
                Input = input,
            };
            _localInputs[playerHandle] = playerInput;
        }

        public List<RollbackRequest<TState, TInput>> AdvanceFrame()
        {
            PollRemoteClients();

            if (_state != SessionState.Running)
            {
                Debug.Log("[Rollback] session not synchronized, returning error");
                throw new InvalidOperationException("session not synchronized");
            }
            foreach (PlayerHandle handle in _playerRegistry.LocalPlayerHandles())
            {
                if (!_localInputs.ContainsKey(handle))
                {
                    throw new InvalidOperationException(
                        $"missing local inputs for handle {handle}, cannot AdvanceFrame()"
                    );
                }
            }

            if (_desyncDetection.On)
            {
                CheckChecksumSendInterval();
                CompareLocalChecksumsAgainstPeers();
            }

            List<RollbackRequest<TState, TInput>> requests = new List<RollbackRequest<TState, TInput>>();
            bool lockstep = InLockstepMode;

            if (_syncLayer.CurrentFrame == Frame.FirstFrame && !lockstep)
            {
                Debug.Log("[Rollback] saving state of first frame");
                requests.Add(_syncLayer.SaveCurrentState());
            }

            UpdatePlayerDisconnects();

            Frame confirmedFrame = ConfirmedFrame();
            if (!lockstep)
            {
                Frame firstIncorrect = _syncLayer.CheckSimulationConsistency(_disconnectFrame);
                if (firstIncorrect != Frame.NullFrame)
                {
                    AdjustGameState(firstIncorrect, confirmedFrame, requests);
                    _disconnectFrame = Frame.NullFrame;
                }

                Frame lastSaved = _syncLayer.LastSavedFrame;
                if (_sparseSaving)
                {
                    CheckLastSavedState(lastSaved, confirmedFrame, requests);
                }
                else
                {
                    requests.Add(_syncLayer.SaveCurrentState());
                }
            }

            SendConfirmedInputsToSpectators(confirmedFrame);
            _syncLayer.SetLastConfirmedFrame(confirmedFrame, _sparseSaving);

            CheckWaitRecommmendation();

            foreach (PlayerHandle handle in _playerRegistry.LocalPlayerHandles())
            {
                PlayerInput<TInput> playerInput = _localInputs[handle];
                Frame actualFrame = _syncLayer.AddLocalInput(handle, playerInput);
                playerInput.Frame = actualFrame;
                _localInputs[handle] = playerInput;
                if (actualFrame != Frame.NullFrame)
                {
                    _localConnectStatus[handle.Id].LastFrame = actualFrame;
                }
            }

            if (!_localInputs.Values.Any(input => input.Frame == Frame.NullFrame))
            {
                foreach (UdpProtocol<TInput, TAddress> ep in _playerRegistry.Remotes.Values)
                {
                    ep.SendInput(_localInputs, _localConnectStatus);
                    ep.SendAllMessages(_socket);
                }
            }

            bool canAdvance = _syncLayer.LastConfirmedFrame == _syncLayer.CurrentFrame; // assuming lockstep
            if (!lockstep)
            {
                int framesAhead =
                    _syncLayer.LastConfirmedFrame == Frame.NullFrame
                        ? _syncLayer.CurrentFrame.No
                        : _syncLayer.CurrentFrame - _syncLayer.LastConfirmedFrame;
                canAdvance = framesAhead < _maxPrediction;
            }

            if (canAdvance)
            {
                (TInput input, InputStatus status)[] inputs = _syncLayer.SynchronizedInputs(_localConnectStatus);
                _syncLayer.AdvanceFrame();
                _localInputs.Clear();
                requests.Add(
                    RollbackRequest<TState, TInput>.From(
                        new RollbackRequest<TState, TInput>.AdvanceFrame { Inputs = inputs }
                    )
                );
            }
            else
            {
                Debug.Log($"[Rollback] prediction threshold reached, skipping on frame {_syncLayer.CurrentFrame}");
            }

            return requests;
        }

        public void PollRemoteClients()
        {
            foreach ((TAddress addr, Message msg) in _socket.ReceiveAllMessages())
            {
                if (_playerRegistry.Remotes.TryGetValue(addr, out UdpProtocol<TInput, TAddress> ep1))
                {
                    ep1.HandleMessage(msg);
                }
                if (_playerRegistry.Spectators.TryGetValue(addr, out var ep2))
                {
                    ep2.HandleMessage(msg);
                }
            }

            foreach (UdpProtocol<TInput, TAddress> remoteEp in _playerRegistry.Remotes.Values)
            {
                if (remoteEp.IsRunning)
                {
                    remoteEp.UpdateLocalFrameAdvantage(_syncLayer.CurrentFrame);
                }
            }

            Deque<(Event<TInput> ev, PlayerHandle[] handles, TAddress addr)> events =
                new Deque<(Event<TInput> ev, PlayerHandle[] handles, TAddress addr)>();

            foreach (UdpProtocol<TInput, TAddress> ep in _playerRegistry.Remotes.Values)
            {
                PlayerHandle[] handles = (PlayerHandle[])ep.Handles.Clone();
                TAddress addr = ep.PeerAddr;
                foreach (Event<TInput> ev in ep.Poll(_localConnectStatus))
                {
                    events.PushBack((ev, handles, addr));
                }
            }
            foreach (UdpProtocol<TInput, TAddress> ep in _playerRegistry.Spectators.Values)
            {
                PlayerHandle[] handles = (PlayerHandle[])ep.Handles.Clone();
                TAddress addr = ep.PeerAddr;
                foreach (Event<TInput> ev in ep.Poll(_localConnectStatus))
                {
                    events.PushBack((ev, handles, addr));
                }
            }

            while (events.Count > 0)
            {
                (Event<TInput> ev, PlayerHandle[] handles, TAddress addr) = events.PopFront();
                HandleEvent(ev, handles, addr);
            }

            foreach (UdpProtocol<TInput, TAddress> ep in _playerRegistry.Remotes.Values)
            {
                ep.SendAllMessages(_socket);
            }
            foreach (UdpProtocol<TInput, TAddress> ep in _playerRegistry.Spectators.Values)
            {
                ep.SendAllMessages(_socket);
            }
        }

        public void DisconnectPlayer(PlayerHandle playerHandle)
        {
            if (_playerRegistry.Handles.TryGetValue(playerHandle, out PlayerType<TAddress> kind))
            {
                switch (kind.Kind)
                {
                    case PlayerKind.Local:
                        throw new InvalidOperationException("cannot disconnect local player");
                    case PlayerKind.Remote:
                        if (!_localConnectStatus[playerHandle.Id].Disconnected)
                        {
                            Frame lastFrame = _localConnectStatus[playerHandle.Id].LastFrame;
                            DisconnectPlayerAtFrame(playerHandle, lastFrame);
                            return;
                        }
                        throw new InvalidOperationException("player already disconnected");
                    case PlayerKind.Spectator:
                        DisconnectPlayerAtFrame(playerHandle, Frame.NullFrame);
                        break;
                }
            }
            throw new InvalidOperationException("invalid player handle");
        }

        public NetworkStats NetworkStats(PlayerHandle playerHandle)
        {
            if (_playerRegistry.Handles.TryGetValue(playerHandle, out PlayerType<TAddress> type))
            {
                switch (type.Kind)
                {
                    case PlayerKind.Local:
                        throw new InvalidOperationException("no net stats for local player");
                    case PlayerKind.Remote:
                        return _playerRegistry.Remotes[type.Address].NetworkStats();
                    case PlayerKind.Spectator:
                        return _playerRegistry.Spectators[type.Address].NetworkStats();
                }
            }
            throw new InvalidOperationException("invalid player handle");
        }

        public Frame ConfirmedFrame()
        {
            Frame confirmedFrame = new Frame { No = int.MaxValue };
            for (int i = 0; i < _localConnectStatus.Length; i++)
            {
                if (!_localConnectStatus[i].Disconnected)
                {
                    confirmedFrame = Frame.Min(confirmedFrame, _localConnectStatus[i].LastFrame);
                }
            }

            Assert.IsTrue(confirmedFrame.No < int.MaxValue);
            return confirmedFrame;
        }

        public TState ConfirmedState()
        {
            _syncLayer.SavedStateByFrame(ConfirmedFrame()).Load(out var data);
            return data;
        }

        public Frame CurrentFrame => _syncLayer.CurrentFrame;
        public uint MaxPrediction => _maxPrediction;
        public bool InLockstepMode => _maxPrediction == 0;
        public SessionState CurrentState => _state;
        public int FramesAhead => _framesAhead;
        public DesyncDetection DesyncDetection => _desyncDetection;

        public IEnumerable<RollbackEvent<TInput, TAddress>> DrainEvents()
        {
            while (_eventQueue.Count > 0)
            {
                yield return _eventQueue.PopFront();
            }
        }

        public int NumPlayers() => _playerRegistry.NumPlayers();

        public int NumSpectators() => _playerRegistry.NumSpectators();

        public IEnumerable<PlayerHandle> LocalPlayerHandles() => _playerRegistry.LocalPlayerHandles();

        public IEnumerable<PlayerHandle> RemotePlayerHandles() => _playerRegistry.RemotePlayerHandles();

        public IEnumerable<PlayerHandle> SpectatorHandles() => _playerRegistry.SpectatorHandles();

        private void DisconnectPlayerAtFrame(PlayerHandle playerHandle, Frame lastFrame)
        {
            if (_playerRegistry.Handles.TryGetValue(playerHandle, out PlayerType<TAddress> type))
            {
                switch (type.Kind)
                {
                    case PlayerKind.Remote:
                        UdpProtocol<TInput, TAddress> ep1 = _playerRegistry.Remotes[type.Address];
                        foreach (PlayerHandle handle in ep1.Handles)
                        {
                            _localConnectStatus[handle.Id].Disconnected = true;
                        }
                        ep1.Disconnect();
                        if (_syncLayer.CurrentFrame > lastFrame)
                        {
                            _disconnectFrame = lastFrame + 1;
                        }
                        break;
                    case PlayerKind.Spectator:
                        UdpProtocol<TInput, TAddress> ep2 = _playerRegistry.Spectators[type.Address];
                        ep2.Disconnect();
                        break;
                }
            }
            CheckInitialSync();
        }

        private void CheckInitialSync()
        {
            if (_state != SessionState.Synchronizing)
            {
                return;
            }
            foreach (UdpProtocol<TInput, TAddress> ep in _playerRegistry.Remotes.Values)
            {
                if (!ep.IsSynchronized)
                    return;
            }
            foreach (UdpProtocol<TInput, TAddress> ep in _playerRegistry.Spectators.Values)
            {
                if (!ep.IsSynchronized)
                    return;
            }
            _state = SessionState.Running;
        }

        private void AdjustGameState(
            Frame firstIncorrect,
            Frame minConfirmed,
            List<RollbackRequest<TState, TInput>> requests
        )
        {
            Frame currentFrame = _syncLayer.CurrentFrame;
            Frame frameToLoad = _sparseSaving ? _syncLayer.LastSavedFrame : firstIncorrect;

            Assert.IsTrue(frameToLoad <= firstIncorrect);
            int count = currentFrame - frameToLoad;

            requests.Add(_syncLayer.LoadFrame(frameToLoad));

            Assert.IsTrue(_syncLayer.CurrentFrame == frameToLoad);
            _syncLayer.ResetPrediction();

            for (int i = 0; i < count; i++)
            {
                (TInput input, InputStatus status)[] inputs = _syncLayer.SynchronizedInputs(_localConnectStatus);
                if (_sparseSaving)
                {
                    if (_syncLayer.CurrentFrame == minConfirmed)
                    {
                        requests.Add(_syncLayer.SaveCurrentState());
                    }
                }
                else
                {
                    if (i > 0)
                    {
                        requests.Add(_syncLayer.SaveCurrentState());
                    }
                }

                _syncLayer.AdvanceFrame();
                requests.Add(
                    RollbackRequest<TState, TInput>.From(
                        new RollbackRequest<TState, TInput>.AdvanceFrame { Inputs = inputs }
                    )
                );
            }
            Assert.IsTrue(_syncLayer.CurrentFrame == currentFrame);
        }

        private void SendConfirmedInputsToSpectators(Frame confirmedFrame)
        {
            if (NumSpectators() == 0)
            {
                return;
            }
            while (_nextSpectatorFrame <= confirmedFrame)
            {
                PlayerInput<TInput>[] inputs = _syncLayer.ConfirmedInputs(_nextSpectatorFrame, _localConnectStatus);
                Assert.IsTrue(inputs.Length == _numPlayers);

                Dictionary<PlayerHandle, PlayerInput<TInput>> inputMap =
                    new Dictionary<PlayerHandle, PlayerInput<TInput>>();
                for (int i = 0; i < inputs.Length; i++)
                {
                    Assert.IsTrue(inputs[i].Frame == Frame.NullFrame || inputs[i].Frame == _nextSpectatorFrame);
                    inputMap.Add(new PlayerHandle(i), inputs[i]);
                }

                foreach (UdpProtocol<TInput, TAddress> ep in _playerRegistry.Spectators.Values)
                {
                    if (ep.IsRunning)
                    {
                        ep.SendInput(inputMap, _localConnectStatus);
                    }
                }
                _nextSpectatorFrame += 1;
            }
        }

        private void UpdatePlayerDisconnects()
        {
            for (int i = 0; i < _numPlayers; i++)
            {
                PlayerHandle handle = new PlayerHandle(i);
                bool queueConnected = true;
                Frame queueMinConfirmed = new Frame { No = int.MaxValue };

                foreach (UdpProtocol<TInput, TAddress> ep in _playerRegistry.Remotes.Values)
                {
                    if (!ep.IsRunning)
                    {
                        continue;
                    }
                    ConnectionStatus conStatus = ep.PeerConnectStatus(handle);
                    bool connected = !conStatus.Disconnected;
                    Frame minConfirmed = conStatus.LastFrame;

                    queueConnected = queueConnected && connected;
                    queueMinConfirmed = Frame.Min(queueMinConfirmed, minConfirmed);
                }

                bool localConnected = !_localConnectStatus[handle.Id].Disconnected;
                Frame localMinConfirmed = _localConnectStatus[handle.Id].LastFrame;

                if (localConnected)
                {
                    queueMinConfirmed = Frame.Min(queueMinConfirmed, localMinConfirmed);
                }

                if (!queueConnected)
                {
                    if (localConnected || localMinConfirmed > queueMinConfirmed)
                    {
                        DisconnectPlayerAtFrame(handle, queueMinConfirmed);
                    }
                }
            }
        }

        private int MaxFrameAdvantage()
        {
            int interval = int.MinValue;
            foreach (UdpProtocol<TInput, TAddress> ep in _playerRegistry.Remotes.Values)
            {
                for (int i = 0; i < ep.Handles.Length; i++)
                {
                    if (!_localConnectStatus[ep.Handles[i].Id].Disconnected)
                    {
                        interval = Math.Max(interval, ep.AverageFrameAdvantage());
                    }
                }
            }
            if (interval == int.MinValue)
            {
                return 0;
            }
            return interval;
        }

        private void CheckWaitRecommmendation()
        {
            _framesAhead = MaxFrameAdvantage();
            if (_syncLayer.CurrentFrame > _nextRecommendedSleep && _framesAhead >= MIN_RECOMMENDATION)
            {
                _nextRecommendedSleep = _syncLayer.CurrentFrame + RECOMMENDATION_INTERVAL;
                _eventQueue.PushBack(
                    RollbackEvent<TInput, TAddress>.From(
                        new RollbackEvent<TInput, TAddress>.WaitRecommendation { SkipFrames = (uint)_framesAhead }
                    )
                );
            }
        }

        private void CheckLastSavedState(
            Frame lastSaved,
            Frame confirmedFrame,
            List<RollbackRequest<TState, TInput>> requests
        )
        {
            if (_syncLayer.CurrentFrame - lastSaved >= _maxPrediction)
            {
                if (confirmedFrame >= _syncLayer.CurrentFrame)
                {
                    requests.Add(_syncLayer.SaveCurrentState());
                }
                else
                {
                    AdjustGameState(lastSaved, confirmedFrame, requests);
                }

                Assert.IsTrue(
                    confirmedFrame == Frame.NullFrame
                        || _syncLayer.LastSavedFrame == Frame.Min(confirmedFrame, _syncLayer.CurrentFrame)
                );
            }
        }

        private void HandleEvent(in Event<TInput> ev, PlayerHandle[] playerHandles, TAddress addr)
        {
            switch (ev.Kind)
            {
                case EventKind.Synchronizing:
                    Event<TInput>.Synchronizing synchronizing = ev.GetSynchronizing();
                    _eventQueue.PushBack(
                        RollbackEvent<TInput, TAddress>.From(
                            new RollbackEvent<TInput, TAddress>.Synchronizing
                            {
                                Addr = addr,
                                Total = synchronizing.Total,
                                Count = synchronizing.Count,
                            }
                        )
                    );
                    break;
                case EventKind.NetworkInterrupted:
                    Event<TInput>.NetworkInterrupted nwInterrupted = ev.GetNetworkInterrupted();
                    _eventQueue.PushBack(
                        RollbackEvent<TInput, TAddress>.From(
                            new RollbackEvent<TInput, TAddress>.NetworkInterrupted
                            {
                                Addr = addr,
                                DisconnectTimeout = nwInterrupted.DisconnectTimeout,
                            }
                        )
                    );
                    break;
                case EventKind.NetworkResumed:
                    _eventQueue.PushBack(
                        RollbackEvent<TInput, TAddress>.From(
                            new RollbackEvent<TInput, TAddress>.NetworkResumed { Addr = addr }
                        )
                    );
                    break;
                case EventKind.Synchronized:
                    CheckInitialSync();
                    _eventQueue.PushBack(
                        RollbackEvent<TInput, TAddress>.From(
                            new RollbackEvent<TInput, TAddress>.Synchronized { Addr = addr }
                        )
                    );
                    break;
                case EventKind.Disconnected:
                    foreach (PlayerHandle handle in playerHandles)
                    {
                        Frame lastFrame =
                            handle.Id < _numPlayers ? _localConnectStatus[handle.Id].LastFrame : Frame.NullFrame;
                        DisconnectPlayerAtFrame(handle, lastFrame);
                    }
                    _eventQueue.PushBack(
                        RollbackEvent<TInput, TAddress>.From(
                            new RollbackEvent<TInput, TAddress>.Disconnected { Addr = addr }
                        )
                    );
                    break;
                case EventKind.Input:
                    Event<TInput>.Input input = ev.GetInput();
                    Assert.IsTrue(input.Player.Id < _numPlayers);
                    if (!_localConnectStatus[input.Player.Id].Disconnected)
                    {
                        Frame currentRemoteFrame = _localConnectStatus[input.Player.Id].LastFrame;
                        Assert.IsTrue(
                            currentRemoteFrame == Frame.NullFrame || currentRemoteFrame + 1 == input.Data.Frame
                        );
                        _localConnectStatus[input.Player.Id].LastFrame = input.Data.Frame;
                        _syncLayer.AddRemoteInput(input.Player, input.Data);
                    }
                    break;
            }

            while (_eventQueue.Count > SessionConstants.MAX_EVENT_QUEUE_SIZE)
            {
                _eventQueue.PopFront();
            }
        }

        private void CompareLocalChecksumsAgainstPeers()
        {
            if (!_desyncDetection.On)
            {
                return;
            }
            foreach (UdpProtocol<TInput, TAddress> remote in _playerRegistry.Remotes.Values)
            {
                List<Frame> checkedFrames = new List<Frame>();
                foreach ((Frame remoteFrame, ulong remoteChecksum) in remote.PendingChecksums)
                {
                    if (remoteFrame >= _syncLayer.LastConfirmedFrame)
                    {
                        continue;
                    }
                    if (_localChecksumHistory.TryGetValue(remoteFrame, out ulong localChecksum))
                    {
                        if (localChecksum != remoteChecksum)
                        {
                            _eventQueue.PushBack(
                                RollbackEvent<TInput, TAddress>.From(
                                    new RollbackEvent<TInput, TAddress>.DesyncDetected
                                    {
                                        Frame = remoteFrame,
                                        LocalChecksum = localChecksum,
                                        RemoteChecksum = remoteChecksum,
                                        Addr = remote.PeerAddr,
                                    }
                                )
                            );
                        }
                        checkedFrames.Add(remoteFrame);
                    }
                }

                foreach (Frame frame in checkedFrames)
                {
                    remote.PendingChecksums.Remove(frame);
                }
            }
        }

        private void CheckChecksumSendInterval()
        {
            if (!_desyncDetection.On)
            {
                return;
            }
            Frame frameToSend =
                _lastSentChecksumFrame == Frame.NullFrame
                    ? new Frame { No = (int)_desyncDetection.Interval }
                    : _lastSentChecksumFrame + (int)_desyncDetection.Interval;

            if (frameToSend <= _syncLayer.LastConfirmedFrame && frameToSend <= _syncLayer.LastSavedFrame)
            {
                GameStateCell<TState> cell = _syncLayer.SavedStateByFrame(frameToSend);
                ulong? checkSum = cell.State.Checksum;
                if (checkSum != null)
                {
                    foreach (UdpProtocol<TInput, TAddress> remote in _playerRegistry.Remotes.Values)
                    {
                        remote.SendChecksumReport(frameToSend, (ulong)checkSum);
                    }
                    _lastSentChecksumFrame = frameToSend;
                    _localChecksumHistory.Add(frameToSend, (ulong)checkSum);
                }

                if (_localChecksumHistory.Count > ProtocolConstants.MAX_CHECKSUM_HISTORY_SIZE)
                {
                    Frame oldestFrameToKeep =
                        frameToSend
                        - (ProtocolConstants.MAX_CHECKSUM_HISTORY_SIZE - 1) * (int)_desyncDetection.Interval;
                    List<Frame> framesToRemove = new List<Frame>();
                    foreach (Frame frame in _localChecksumHistory.Keys)
                    {
                        if (frame < oldestFrameToKeep)
                            framesToRemove.Add(frame);
                    }
                    foreach (Frame frame in framesToRemove)
                        _localChecksumHistory.Remove(frame);
                }
            }
        }
    }
}
