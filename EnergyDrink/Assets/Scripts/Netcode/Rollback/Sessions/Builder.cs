using System;
using System.Collections.Generic;
using Netcode.Rollback.Network;

namespace Netcode.Rollback.Sessions
{
    public static class SessionConstants
    {
        public const int MAX_EVENT_QUEUE_SIZE = 100;
        public const int MAX_NUM_PLAYERS = 16;
        public const int MAX_INPUT_PAYLOAD = 400;
    }
    public class SessionBuilder<TInput, TAddress>
        where TInput : struct, IInput<TInput>
    {
        const int DEFAULT_PLAYERS = 2;
        const bool DEFAULT_SAVE_MODE = false;
        static readonly DesyncDetection DEFAULT_DESYNC_DETECTION = new DesyncDetection { On = false };
        const uint DEFAULT_INPUT_DELAY = 0;
        const ulong DEFAULT_DISCONNECT_TIMEOUT = 2000;
        const ulong DEFAULT_DISCONNECT_NOTIFY_START = 500;
        const uint DEFAULT_FPS = 60;
        const uint DEFAULT_MAX_PREDICTION_FRAMES = 8;
        const uint DEFAULT_CHECK_DISTANCE = 2;
        const uint DEFAULT_MAX_FRAMES_BEHIND = 10;
        const uint DEFAULT_CATCHUP_SPEED = 1;

        private int _numPlayers;
        private int _localPlayers;
        private uint _maxPrediction;
        private uint _fps;
        private bool _sparseSaving;
        private DesyncDetection _desyncDetection;
        private TimeSpan _disconnectTimeout;
        private TimeSpan _disconnectNotifyStart;
        private PlayerRegisty<TInput, TAddress> _playerRegistry;
        private uint _inputDelay;
        private uint _checkDist;
        private uint _maxFramesBehind;
        private uint _catchupSpeed;
        private bool _started;

        public SessionBuilder()
        {
            _playerRegistry = new PlayerRegisty<TInput, TAddress>();
            _localPlayers = 0;
            _numPlayers = DEFAULT_PLAYERS;
            _maxPrediction = DEFAULT_MAX_PREDICTION_FRAMES;
            _fps = DEFAULT_FPS;
            _sparseSaving = DEFAULT_SAVE_MODE;
            _desyncDetection = DEFAULT_DESYNC_DETECTION;
            _disconnectTimeout = TimeSpan.FromMilliseconds(DEFAULT_DISCONNECT_TIMEOUT);
            _disconnectNotifyStart = TimeSpan.FromMilliseconds(DEFAULT_DISCONNECT_NOTIFY_START);
            _inputDelay = DEFAULT_INPUT_DELAY;
            _checkDist = DEFAULT_CHECK_DISTANCE;
            _maxFramesBehind = DEFAULT_MAX_FRAMES_BEHIND;
            _catchupSpeed = DEFAULT_CATCHUP_SPEED;
            _started = false;
        }

        public SessionBuilder<TInput, TAddress> AddPlayer(PlayerType<TAddress> playerType, PlayerHandle playerHandle)
        {
            if (_playerRegistry.Handles.ContainsKey(playerHandle))
            {
                throw new InvalidOperationException("player handle already in use");
            }
            switch (playerType.Kind)
            {
                case PlayerKind.Local:
                    _localPlayers += 1;
                    if (playerHandle.Id >= _numPlayers)
                    {
                        throw new InvalidOperationException("local player handle is invalid: should be in [0, numPlayers)");
                    }
                    break;
                case PlayerKind.Remote:
                    if (playerHandle.Id >= _numPlayers)
                    {
                        throw new InvalidOperationException("remote player handle is invalid: should be in [0, numPlayers)");
                    }
                    break;
                case PlayerKind.Spectator:
                    if (playerHandle.Id < _numPlayers)
                    {
                        throw new InvalidOperationException("spectator handle is invalid: should be in [numPlayers, infinity)");
                    }
                    break;
            }
            _playerRegistry.Handles.Add(playerHandle, playerType);
            return this;
        }

        public SessionBuilder<TInput, TAddress> WithMaxPredictionWindow(uint window)
        {
            _maxPrediction = window;
            return this;
        }

        public SessionBuilder<TInput, TAddress> WithInputDelay(uint delay)
        {
            _inputDelay = delay;
            return this;
        }

        public SessionBuilder<TInput, TAddress> WithNumPlayers(int numPlayers)
        {
            if (numPlayers < 0 || numPlayers > SessionConstants.MAX_NUM_PLAYERS)
            {
                throw new InvalidOperationException($"num players {numPlayers} is not in [0, MAX_NUM_PLAYERS]");
            }
            _numPlayers = numPlayers;
            return this;
        }

        public SessionBuilder<TInput, TAddress> WithSparseSavingMode(bool sparseSaving)
        {
            _sparseSaving = sparseSaving;
            return this;
        }

        public SessionBuilder<TInput, TAddress> WithDesyncDetection(DesyncDetection desyncDetection)
        {
            _desyncDetection = desyncDetection;
            return this;
        }

        public SessionBuilder<TInput, TAddress> WithDisconnectTimeout(TimeSpan timeout)
        {
            _disconnectTimeout = timeout;
            return this;
        }

        public SessionBuilder<TInput, TAddress> WithDisconnectNotifyDelay(TimeSpan notifyDelay)
        {
            _disconnectNotifyStart = notifyDelay;
            return this;
        }

        public SessionBuilder<TInput, TAddress> WithFps(uint fps)
        {
            if (fps == 0) { throw new InvalidOperationException("fps should be higher than 0"); }
            _fps = fps;
            return this;
        }

        public SessionBuilder<TInput, TAddress> WithCheckDistance(uint checkDistance)
        {
            _checkDist = checkDistance;
            return this;
        }

        public SessionBuilder<TInput, TAddress> WithMaxFramesBehind(uint maxFramesBehind)
        {
            if (maxFramesBehind < 1) { throw new InvalidOperationException("max frames cannot be smaller than 1"); }
            if (maxFramesBehind >= SpectatorConstants.SPECTATOR_BUFFER_SIZE)
            {
                throw new InvalidOperationException("max frames cannot be larger than the spectator buffer size");
            }
            _maxFramesBehind = maxFramesBehind;
            return this;
        }

        public SessionBuilder<TInput, TAddress> WithCatchupSpeed(uint catchupSpeed)
        {
            if (catchupSpeed < 1) throw new InvalidOperationException("catchup speed cannot be smaller than 1");
            if (catchupSpeed >= _maxFramesBehind)
            {
                throw new InvalidOperationException("catchup speed cannot be >= allowed max frames behind host");
            }
            _catchupSpeed = catchupSpeed;
            return this;
        }

        public P2PSession<TState, TInput, TAddress> StartP2PSession<TState>(INonBlockingSocket<TAddress> socket)
            where TState : struct
        {
            for (int i = 0; i < _numPlayers; i++)
            {
                if (!_playerRegistry.Handles.ContainsKey(new PlayerHandle(i)))
                {
                    throw new InvalidOperationException("not enough players have been added");
                }
            }

            Dictionary<PlayerType<TAddress>, List<PlayerHandle>> addrCount = new Dictionary<PlayerType<TAddress>, List<PlayerHandle>>();
            foreach ((PlayerHandle handle, PlayerType<TAddress> playerType) in _playerRegistry.Handles)
            {
                if (playerType.Kind == PlayerKind.Remote || playerType.Kind == PlayerKind.Spectator)
                {
                    if (!addrCount.TryGetValue(playerType, out var players))
                    {
                        players = new List<PlayerHandle>();
                        addrCount[playerType] = players;
                    }
                    players.Add(handle);
                }
            }

            foreach ((PlayerType<TAddress> playerType, List<PlayerHandle> handles) in addrCount)
            {
                switch (playerType.Kind)
                {
                    case PlayerKind.Remote:
                        _playerRegistry.Remotes.Add(playerType.Address, CreateEndpoint(handles, playerType.Address, _localPlayers));
                        break;
                    case PlayerKind.Spectator:
                        _playerRegistry.Spectators.Add(playerType.Address, CreateEndpoint(handles, playerType.Address, _numPlayers));
                        break;
                }
            }

            MarkStarted();
            return new P2PSession<TState, TInput, TAddress>(_numPlayers, _maxPrediction, socket, _playerRegistry, _sparseSaving, _desyncDetection, _inputDelay);
        }

        public SpectatorSession<TState, TInput, TAddress> StartSpectatorSession<TState>(TAddress hostAddr, INonBlockingSocket<TAddress> socket)
            where TState : struct
        {
            PlayerHandle[] handles = new PlayerHandle[_numPlayers];
            for (int i = 0; i < _numPlayers; i++) { handles[i] = new PlayerHandle(i); }
            UdpProtocol<TInput, TAddress> host = new UdpProtocol<TInput, TAddress>(handles, hostAddr, _numPlayers, 1, _maxPrediction, _disconnectTimeout, _disconnectNotifyStart, _fps, new DesyncDetection { On = false });
            host.Synchronize();

            MarkStarted();
            return new SpectatorSession<TState, TInput, TAddress>(_numPlayers, socket, host, _maxFramesBehind, _catchupSpeed);
        }

        public SyncTestSession<TState, TInput, TAddress> StartSynctestSession<TState>()
            where TState : struct
        {
            if (_checkDist >= _maxPrediction) { throw new InvalidOperationException("check distance too big"); }

            MarkStarted();
            return new SyncTestSession<TState, TInput, TAddress>(_numPlayers, _maxPrediction, _checkDist, _inputDelay);
        }

        private UdpProtocol<TInput, TAddress> CreateEndpoint(List<PlayerHandle> handles, TAddress peerAddr, int localPlayers)
        {
            UdpProtocol<TInput, TAddress> endpoint = new UdpProtocol<TInput, TAddress>(
                handles.ToArray(),
                peerAddr,
                _numPlayers,
                localPlayers,
                _maxPrediction,
                _disconnectTimeout
                , _disconnectNotifyStart,
                _fps,
                _desyncDetection
            );
            endpoint.Synchronize();
            return endpoint;
        }

        private void MarkStarted()
        {
            if (_started) throw new InvalidOperationException("session already started");
            _started = true;
        }
    }
}