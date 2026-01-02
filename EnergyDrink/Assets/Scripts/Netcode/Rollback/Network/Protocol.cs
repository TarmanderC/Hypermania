using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using Utils;

namespace Netcode.Rollback.Network
{
    public struct InputBytes
    {
        public Frame Frame;
        public byte[] Bytes;

        public static InputBytes Zeroed<TInput>(int numPlayers) where TInput : IInput<TInput>
        {
            int size = Serializer<TInput>.DefaultSize() * numPlayers;
            return new InputBytes
            {
                Frame = Frame.NullFrame,
                Bytes = new byte[size],
            };
        }

        public static InputBytes FromInputs<TInput>(
            Dictionary<PlayerHandle, PlayerInput<TInput>> inputs)
            where TInput : IInput<TInput>
        {
            Frame frame = Frame.NullFrame;
            int size = Serializer<TInput>.DefaultSize() * inputs.Count;
            byte[] buf = new byte[size];

            int ptr = 0;
            foreach ((PlayerHandle handle, PlayerInput<TInput> input) in inputs)
            {
                if (inputs.TryGetValue(handle, out PlayerInput<TInput> pi))
                {
                    Assert.IsTrue(frame == Frame.NullFrame || pi.Frame == Frame.NullFrame || frame == pi.Frame);
                    if (pi.Frame != Frame.NullFrame) frame = pi.Frame;
                    pi.Input.Serialize(buf.AsSpan().Slice(ptr, Serializer<TInput>.DefaultSize()));
                    ptr += Serializer<TInput>.DefaultSize();
                }
            }

            return new InputBytes { Frame = frame, Bytes = buf };
        }


        public PlayerInput<TInput>[] ToInputs<TInput>(int numPlayers)
            where TInput : IInput<TInput>
        {
            Assert.IsTrue(numPlayers != 0);
            Assert.IsTrue(Bytes.Length % numPlayers == 0);

            int size = Bytes.Length / numPlayers;
            PlayerInput<TInput>[] res = new PlayerInput<TInput>[numPlayers];

            for (int p = 0; p < numPlayers; p++)
            {
                int start = p * size;
                ReadOnlySpan<byte> slice = Bytes.AsSpan().Slice(start, size);
                TInput input = default;
                input.Deserialize(slice);

                res[p] = new PlayerInput<TInput> { Frame = Frame, Input = input };
            }
            return res;
        }
    }

    public enum ProtocolState
    {
        Initializing,
        Synchronizing,
        Running,
        Disconnected,
        Shutdown,
    }

    public static class ProtocolConstants
    {
        public const int MAX_CHECKSUM_HISTORY_SIZE = 32;
    }

    public class UdpProtocol<TInput, TAddress> where TInput : IInput<TInput>
    {
        const uint NUM_SYNC_PACKETS = 5;
        const ulong UDP_SHUTDOWN_TIMER = 5000;
        const int PENDING_OUTPUT_SIZE = 128;
        const ulong SYNC_RETRY_INTERVAL = 200;
        const ulong RUNNING_RETRY_INTERVAL = 200;
        const ulong KEEP_ALIVE_INTERVAL = 200;
        const ulong QUALITY_REPORT_INTERVAL = 200;

        // configuration / collections
        private int _numPlayers;
        private int _localPlayers;
        private PlayerHandle[] _handles;
        private Deque<Message> _sendQueue;
        private Deque<Event<TInput>> _eventQueue;

        // state
        private ProtocolState _state;
        private uint _syncRemainingRoundtrips;
        private HashSet<uint> _syncRandomRequests;
        private Instant _runningLastQualityReport;
        private Instant _runningLastInputRecv;
        private bool _disconnectNotifySent;
        private bool _disconnectEventSent;

        // constants
        private TimeSpan _disconnectTimeout;
        private TimeSpan _disconnectNotifyStart;
        private Instant _shutdownTimeout;
        private uint _fps;
        private ushort _magic;

        // peer
        private TAddress _peerAddr;
        private ushort _remoteMagic;
        private ConnectionStatus[] _peerConnectStatus;

        // input compression
        private Deque<InputBytes> _pendingOutput;
        private InputBytes _lastAckedInput;
        private uint _maxPrediction;
        private Dictionary<Frame, InputBytes> _recvInputs;
        private Frame _lastRecvFrame;

        // time sync
        private TimeSync _timeSyncLayer;
        private int _localFrameAdvantage;
        private int _remoteFrameAdvantage;

        // network
        private ulong _statsStartTime;
        private uint _packetsSent;
        private ulong _roundTripTime;
        private Instant _lastSendTime;
        private Instant _lastRecvTime;

        // desync detection
        private Dictionary<Frame, ulong> _pendingChecksums;
        private DesyncDetection _desyncDetection;
        public Dictionary<Frame, ulong> PendingChecksums => _pendingChecksums;

        // cached 
        private System.Random _random;
        private Compression _compression;


        public UdpProtocol(
            ReadOnlySpan<PlayerHandle> handles,
            TAddress peerAddr,
            int numPlayers,
            int localPlayers,
            uint maxPrediction,
            TimeSpan disconnectTimeout,
            TimeSpan disconnectNotifyStart,
            uint fps, DesyncDetection desyncDetection)
        {
            _random = new System.Random();
            ushort magic = 0;
            while (magic == 0)
                magic = (ushort)_random.Next(ushort.MaxValue + 1);

            _handles = new PlayerHandle[handles.Length];
            handles.CopyTo(_handles);
            int recvPlayerNum = handles.Length;

            ConnectionStatus[] peerConnectStatus = new ConnectionStatus[numPlayers];
            for (int i = 0; i < numPlayers; i++)
                peerConnectStatus[i] = ConnectionStatus.Default;

            Dictionary<Frame, InputBytes> recvInputs = new Dictionary<Frame, InputBytes>
            {
                { Frame.NullFrame, InputBytes.Zeroed<TInput>(recvPlayerNum) }
            };

            _numPlayers = numPlayers;
            _localPlayers = localPlayers;
            Array.Sort(_handles);
            _sendQueue = new Deque<Message>();
            _eventQueue = new Deque<Event<TInput>>();

            _state = ProtocolState.Initializing;
            _syncRemainingRoundtrips = NUM_SYNC_PACKETS;
            _syncRandomRequests = new HashSet<uint>();
            _runningLastQualityReport = Instant.Now();
            _runningLastInputRecv = Instant.Now();
            _disconnectNotifySent = false;
            _disconnectEventSent = false;

            _disconnectTimeout = disconnectTimeout;
            _disconnectNotifyStart = disconnectNotifyStart;
            _shutdownTimeout = Instant.Now();
            _fps = fps;
            _magic = magic;

            _peerAddr = peerAddr;
            _remoteMagic = 0;
            _peerConnectStatus = peerConnectStatus;

            _pendingOutput = new Deque<InputBytes>(PENDING_OUTPUT_SIZE);
            _lastAckedInput = InputBytes.Zeroed<TInput>(localPlayers);
            _maxPrediction = maxPrediction;
            _recvInputs = recvInputs;
            _lastRecvFrame = Frame.NullFrame;

            _timeSyncLayer = default;
            _localFrameAdvantage = 0;
            _remoteFrameAdvantage = 0;

            _statsStartTime = 0;
            _packetsSent = 0;
            _roundTripTime = 0;
            _lastSendTime = Instant.Now();
            _lastRecvTime = Instant.Now();

            _pendingChecksums = new Dictionary<Frame, ulong>();
            _desyncDetection = desyncDetection;
        }

        public void UpdateLocalFrameAdvantage(Frame localFrame)
        {
            if (localFrame == Frame.NullFrame || _lastRecvFrame == Frame.NullFrame) { return; }
            int ping = (int)(_roundTripTime / 2);
            int remoteFrame = _lastRecvFrame.No + ping * (int)_fps / 1000;
            _localFrameAdvantage = remoteFrame - localFrame.No;
        }

        public NetworkStats NetworkStats()
        {
            if (_state != ProtocolState.Synchronizing && _state != ProtocolState.Running) { throw new InvalidOperationException("not synchronized"); }
            ulong now = Helpers.MillisSinceEpoch();
            ulong secs = (now - _statsStartTime) / 1000;
            if (secs == 0) { throw new InvalidOperationException("not synchronized"); }

            return new NetworkStats
            {
                Ping = _roundTripTime,
                SendQueueLen = _pendingOutput.Count,
                LocalFramesBehind = _localFrameAdvantage,
                RemoteFramesBehind = _remoteFrameAdvantage,
            };
        }

        public PlayerHandle[] Handles => _handles;
        public TAddress PeerAddr => _peerAddr;
        public bool IsSynchronized =>
            _state == ProtocolState.Running || _state == ProtocolState.Disconnected || _state == ProtocolState.Shutdown;
        public bool IsRunning => _state == ProtocolState.Running;

        public bool IsHandlingMessage(TAddress addr) => _peerAddr.Equals(addr);

        public ConnectionStatus PeerConnectStatus(PlayerHandle handle) => _peerConnectStatus[handle.Id];

        public void Disconnect()
        {
            if (_state == ProtocolState.Shutdown) { return; }
            _state = ProtocolState.Disconnected;
            _shutdownTimeout = Instant.Now() + TimeSpan.FromMilliseconds(UDP_SHUTDOWN_TIMER);
        }

        public void Synchronize()
        {
            Assert.AreEqual(_state, ProtocolState.Initializing);
            _state = ProtocolState.Synchronizing;
            _syncRemainingRoundtrips = NUM_SYNC_PACKETS;
            _statsStartTime = Helpers.MillisSinceEpoch();
            SendSyncRequest();
        }

        public int AverageFrameAdvantage() => _timeSyncLayer.AverageFrameAdvantage();

        public IEnumerable<Event<TInput>> Poll(ConnectionStatus[] connectStatus)
        {
            Instant now = Instant.Now();
            switch (_state)
            {
                case ProtocolState.Synchronizing:
                    if (_lastSendTime + TimeSpan.FromMilliseconds(SYNC_RETRY_INTERVAL) < now)
                    {
                        SendSyncRequest();
                    }
                    break;
                case ProtocolState.Running:
                    if (_runningLastInputRecv + TimeSpan.FromMilliseconds(RUNNING_RETRY_INTERVAL) < now)
                    {
                        SendPendingOutput(connectStatus);
                        _runningLastInputRecv = Instant.Now();
                    }
                    if (_runningLastQualityReport + TimeSpan.FromMilliseconds(QUALITY_REPORT_INTERVAL) < now)
                    {
                        SendQualityReport();
                    }
                    if (_lastSendTime + TimeSpan.FromMilliseconds(KEEP_ALIVE_INTERVAL) < now)
                    {
                        SendKeepAlive();
                    }

                    if (!_disconnectNotifySent && _lastRecvTime + _disconnectNotifyStart < now)
                    {
                        TimeSpan duration = _disconnectTimeout - _disconnectNotifyStart;
                        _eventQueue.PushBack(Event<TInput>.From(new Event<TInput>.NetworkInterrupted { DisconnectTimeout = (ulong)duration.TotalMilliseconds }));
                        _disconnectNotifySent = true;
                    }

                    if (!_disconnectEventSent && _lastRecvTime + _disconnectTimeout < now)
                    {
                        _eventQueue.PushBack(Event<TInput>.From(new Event<TInput>.Disconnected { }));
                        _disconnectEventSent = true;
                    }
                    break;
                case ProtocolState.Disconnected:
                    if (_shutdownTimeout < Instant.Now()) { _state = ProtocolState.Shutdown; }
                    break;
            }
            while (_eventQueue.Count > 0) { yield return _eventQueue.PopFront(); }
        }

        public void PopPendingOutput(Frame ackFrame)
        {
            while (_pendingOutput.Count > 0)
            {
                InputBytes input = _pendingOutput.Front();
                if (input.Frame <= ackFrame) { _lastAckedInput = _pendingOutput.PopFront(); }
                else { break; }
            }
        }

        public void SendAllMessages(in INonBlockingSocket<TAddress> socket)
        {
            if (_state == ProtocolState.Shutdown)
            {
                Debug.Log($"[Rollback] Protocol is shutting down, dropping {_sendQueue.Count} messages");
                _sendQueue.Clear();
                return;
            }

            if (_sendQueue.Count == 0) { return; }

            while (_sendQueue.Count > 0) { socket.SendTo(_sendQueue.PopFront(), _peerAddr); }
        }

        public void SendInput(Dictionary<PlayerHandle, PlayerInput<TInput>> inputs, ConnectionStatus[] connectStatus)
        {
            if (!IsRunning) { return; }
            Assert.IsTrue(inputs.Count == _localPlayers);
            InputBytes endpointData = InputBytes.FromInputs(inputs);
            _timeSyncLayer.AdvanceFrame(endpointData.Frame, _localFrameAdvantage, _remoteFrameAdvantage);
            _pendingOutput.PushBack(endpointData);

            if (_pendingOutput.Count > PENDING_OUTPUT_SIZE)
            {
                _eventQueue.PushBack(Event<TInput>.From(new Event<TInput>.Disconnected { }));
            }

            SendPendingOutput(connectStatus);
        }

        private void SendPendingOutput(ConnectionStatus[] connectStatus)
        {
            MessageBody.Input body = MessageBody.Input.Default;
            if (_pendingOutput.Count == 0) { return; }

            InputBytes input = _pendingOutput.Front();
            Assert.IsTrue(_lastAckedInput.Frame == Frame.NullFrame || _lastAckedInput.Frame + 1 == input.Frame);

            body.StartFrame = input.Frame;
            body.Bytes = _compression.Encode(_lastAckedInput, _pendingOutput.Iter());

            int totalBytes = 0;
            foreach (InputBytes bytes in _pendingOutput.Iter()) { totalBytes += bytes.Bytes.Length; }
            // Debug.Log($"[Rollback] Encoded {totalBytes} bytes from {_pendingOutput.Count} pending outputs(s) into {body.Bytes.Length} bytes");

            body.AckFrame = _lastRecvFrame;
            body.DisconnectRequested = _state == ProtocolState.Disconnected;
            ConnectionStatus[] buf = new ConnectionStatus[connectStatus.Length];
            connectStatus.AsSpan().CopyTo(buf);
            body.PeerConnectStatus = buf;

            QueueMessage(MessageBody.From(body));
        }

        private void SendInputAck()
        {
            MessageBody.InputAck body = new MessageBody.InputAck { AckFrame = _lastRecvFrame, };
            QueueMessage(MessageBody.From(body));
        }

        private void SendKeepAlive()
        {
            MessageBody.KeepAlive body = new MessageBody.KeepAlive { };
            QueueMessage(MessageBody.From(body));
        }

        private void SendSyncRequest()
        {
            Span<byte> nmbBytes = stackalloc byte[4];
            _random.NextBytes(nmbBytes);
            uint randomNumber = BinaryPrimitives.ReadUInt32BigEndian(nmbBytes);
            _syncRandomRequests.Add(randomNumber);
            MessageBody.SyncRequest body = new MessageBody.SyncRequest { RandomRequest = randomNumber };
            QueueMessage(MessageBody.From(body));
        }

        private void SendQualityReport()
        {
            _runningLastQualityReport = Instant.Now();
            MessageBody.QualityReport body = new MessageBody.QualityReport
            {
                FrameAdvantage = (short)Math.Clamp(_localFrameAdvantage, short.MinValue, short.MaxValue),
                Ping = Helpers.MillisSinceEpoch(),
            };
            QueueMessage(MessageBody.From(body));
        }

        public void SendChecksumReport(Frame frameToSend, ulong checksum)
        {
            MessageBody.ChecksumReport body = new MessageBody.ChecksumReport { Frame = frameToSend, Checksum = checksum };
            QueueMessage(MessageBody.From(body));
        }

        private void QueueMessage(in MessageBody body)
        {
            MessageHeader header = new MessageHeader { Magic = _magic };
            Message message = new Message { Header = header, Body = body };

            _packetsSent += 1;
            _lastSendTime = Instant.Now();

            _sendQueue.PushBack(message);
        }

        public void HandleMessage(in Message msg)
        {
            if (_state == ProtocolState.Shutdown) { return; }
            if (_remoteMagic != 0 && msg.Header.Magic != _remoteMagic) { return; }

            _lastRecvTime = Instant.Now();

            if (_disconnectNotifySent && _state == ProtocolState.Running)
            {
                _disconnectNotifySent = false;
                _eventQueue.PushBack(Event<TInput>.From(new Event<TInput>.NetworkResumed { }));
            }

            // Debug.Log($"[Rollback] handling message of kind {msg.Body.Kind}");
            switch (msg.Body.Kind)
            {
                case MessageKind.SyncRequest:
                    OnSyncRequest(msg.Body.GetSyncRequest());
                    break;
                case MessageKind.SyncReply:
                    OnSyncReply(msg.Header, msg.Body.GetSyncReply());
                    break;
                case MessageKind.Input:
                    OnInput(msg.Body.GetInput());
                    break;
                case MessageKind.InputAck:
                    OnInputAck(msg.Body.GetInputAck());
                    break;
                case MessageKind.QualityReport:
                    OnQualityReport(msg.Body.GetQualityReport());
                    break;
                case MessageKind.QualityReply:
                    OnQualityReply(msg.Body.GetQualityReply());
                    break;
                case MessageKind.ChecksumReport:
                    OnChecksumReport(msg.Body.GetChecksumReport());
                    break;
            }
        }

        private void OnSyncRequest(in MessageBody.SyncRequest body)
        {
            MessageBody.SyncReply reply = new MessageBody.SyncReply { RandomReply = body.RandomRequest };
            QueueMessage(MessageBody.From(reply));
        }

        private void OnSyncReply(in MessageHeader header, in MessageBody.SyncReply body)
        {
            if (_state != ProtocolState.Synchronizing) { return; }
            if (!_syncRandomRequests.Remove(body.RandomReply)) { return; }

            _syncRemainingRoundtrips -= 1;
            if (_syncRemainingRoundtrips > 0)
            {
                Event<TInput>.Synchronizing evt = new Event<TInput>.Synchronizing
                {
                    Total = NUM_SYNC_PACKETS,
                    Count = NUM_SYNC_PACKETS - _syncRemainingRoundtrips,
                };
                _eventQueue.PushBack(Event<TInput>.From(evt));
                SendSyncRequest();
            }
            else
            {
                _state = ProtocolState.Running;
                _eventQueue.PushBack(Event<TInput>.From(new Event<TInput>.Synchronized { }));
                _remoteMagic = header.Magic;
            }
        }

        private void OnInput(in MessageBody.Input body)
        {
            PopPendingOutput(body.AckFrame);

            if (body.DisconnectRequested)
            {
                if (_state != ProtocolState.Disconnected && !_disconnectEventSent)
                {
                    _eventQueue.PushBack(Event<TInput>.From(new Event<TInput>.Disconnected { }));
                    _disconnectEventSent = true;
                }
            }
            else
            {
                Assert.IsTrue(_numPlayers == _peerConnectStatus.Length);
                for (int i = 0; i < _numPlayers; i++)
                {
                    _peerConnectStatus[i].Disconnected = body.PeerConnectStatus[i].Disconnected || _peerConnectStatus[i].Disconnected;
                    _peerConnectStatus[i].LastFrame = Frame.Max(_peerConnectStatus[i].LastFrame, body.PeerConnectStatus[i].LastFrame);
                }
            }

            Assert.IsTrue(_lastRecvFrame == Frame.NullFrame || _lastRecvFrame + 1 >= body.StartFrame);

            Frame decodeFrame = _lastRecvFrame == Frame.NullFrame ? Frame.NullFrame : body.StartFrame - 1;
            if (_recvInputs.TryGetValue(decodeFrame, out InputBytes decodeInp))
            {
                _runningLastInputRecv = Instant.Now();
                byte[][] recvInputs = _compression.Decode(decodeInp, body.Bytes);

                for (int i = 0; i < recvInputs.GetLength(0); i++)
                {
                    Frame inpFrame = body.StartFrame + i;
                    if (inpFrame <= _lastRecvFrame) { continue; }
                    InputBytes inpData = new InputBytes { Frame = inpFrame, Bytes = recvInputs[i] };
                    _recvInputs[inpData.Frame] = inpData;
                    _lastRecvFrame = Frame.Max(_lastRecvFrame, inpData.Frame);

                    PlayerInput<TInput>[] inputs = inpData.ToInputs<TInput>(_handles.Length);
                    for (int j = 0; j < inputs.Length; j++)
                    {
                        _eventQueue.PushBack(Event<TInput>.From(new Event<TInput>.Input
                        {
                            Data = inputs[j],
                            Player = _handles[j],
                        }));
                    }
                }

                SendInputAck();

                List<Frame> inputsToRemove = new List<Frame>();
                foreach (Frame frame in _recvInputs.Keys)
                {
                    if (frame < _lastRecvFrame - 2 * (int)_maxPrediction) { inputsToRemove.Add(frame); }
                }
                foreach (Frame frame in inputsToRemove) { _recvInputs.Remove(frame); }
                _lastRecvFrame = Frame.NullFrame;
                foreach (Frame frame in _recvInputs.Keys) { _lastRecvFrame = Frame.Max(_lastRecvFrame, frame); }
            }
        }

        private void OnInputAck(in MessageBody.InputAck body)
        {
            PopPendingOutput(body.AckFrame);
        }

        private void OnQualityReport(in MessageBody.QualityReport body)
        {
            _remoteFrameAdvantage = body.FrameAdvantage;
            MessageBody.QualityReply reply = new MessageBody.QualityReply { Pong = body.Ping };
            QueueMessage(MessageBody.From(reply));
        }

        private void OnQualityReply(in MessageBody.QualityReply body)
        {
            ulong millis = Helpers.MillisSinceEpoch();
            Assert.IsTrue(millis >= body.Pong);
            _roundTripTime = millis - body.Pong;
        }

        private void OnChecksumReport(in MessageBody.ChecksumReport body)
        {
            uint interval = 1;
            if (!_desyncDetection.On)
            {
                Debug.Log("[Rollback] Received checksum report but desync detection is off, check for consistent configuration");
            }
            else { interval = _desyncDetection.Interval; }

            if (_pendingChecksums.Count >= ProtocolConstants.MAX_CHECKSUM_HISTORY_SIZE)
            {
                Frame oldestFrameToKeep = body.Frame - (ProtocolConstants.MAX_CHECKSUM_HISTORY_SIZE - 1) * (int)interval;
                List<Frame> toRemove = new List<Frame>();
                foreach (Frame frame in _pendingChecksums.Keys)
                {
                    if (frame < oldestFrameToKeep) { toRemove.Add(frame); }
                }
                foreach (Frame frame in toRemove) { _pendingChecksums.Remove(frame); }
            }
            _pendingChecksums.Add(body.Frame, body.Checksum);
        }
    }
}
