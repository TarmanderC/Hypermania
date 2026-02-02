using System;
using System.Collections.Generic;
using Design;
using Game.Sim;
using Game.View;
using Netcode.P2P;
using Netcode.Rollback;
using Netcode.Rollback.Sessions;
using Steamworks;
using UnityEngine;

namespace Game.Runners
{
    public class MultiplayerRunner : GameRunner
    {
        private P2PSession<GameState, GameInput, SteamNetworkingIdentity> _session;

        private uint _waitRemaining;
        private PlayerHandle _myHandle;
        private PlayerHandle _remoteHandle;

        public override void Init(
            List<(PlayerHandle playerHandle, PlayerKind playerKind, SteamNetworkingIdentity address)> players,
            P2PClient client
        )
        {
            base.Init(players, client);

            SessionBuilder<GameInput, SteamNetworkingIdentity> builder = new SessionBuilder<
                GameInput,
                SteamNetworkingIdentity
            >()
                .WithNumPlayers(players.Count)
                .WithFps(GameManager.TPS);
            foreach ((PlayerHandle playerHandle, PlayerKind playerKind, SteamNetworkingIdentity address) in players)
            {
                if (playerKind == PlayerKind.Local)
                {
                    _myHandle = playerHandle;
                }
                else if (playerKind == PlayerKind.Remote)
                {
                    // assume only two people for now
                    _remoteHandle = playerHandle;
                }
                builder.AddPlayer(
                    new PlayerType<SteamNetworkingIdentity> { Kind = playerKind, Address = address },
                    playerHandle
                );
            }
            _session = builder.StartP2PSession<GameState>(client);
            _waitRemaining = 0;

            if (_myHandle.Id == -1 || _remoteHandle.Id == -1)
            {
                throw new InvalidOperationException("Players not found in multiplayer runner");
            }
        }

        public override void DeInit()
        {
            _waitRemaining = 0;
            _session = null;
            _myHandle = new PlayerHandle(-1);
            base.DeInit();
        }

        public override void Poll(float deltaTime)
        {
            if (!_initialized)
            {
                return;
            }

            _inputBuffer.Clear();
            _inputBuffer.Saturate();

            _session.PollRemoteClients();

            foreach (RollbackEvent<GameInput, SteamNetworkingIdentity> ev in _session.DrainEvents())
            {
                Debug.Log($"[Game] Received {ev.Kind} event");
                switch (ev.Kind)
                {
                    case RollbackEventKind.WaitRecommendation:
                        var waitRec = ev.GetWaitRecommendation();
                        _waitRemaining = waitRec.SkipFrames;
                        break;
                }
            }

            // accumulate time and update frame
            float fpsDelta = 1.0f / GameManager.TPS;
            if (_session.FramesAhead > 0)
            {
                fpsDelta *= 1.1f;
            }

            _time += deltaTime;
            while (_time > fpsDelta)
            {
                _time -= fpsDelta;
                GameLoop();
            }
        }

        void GameLoop()
        {
            if (_session.CurrentState != SessionState.Running)
            {
                return;
            }

            if (_waitRemaining > 0)
            {
                Debug.Log("[Game] Skipping frame due to wait recommendation");
                _waitRemaining--;
                return;
            }

            _session.AddLocalInput(_myHandle, _inputBuffer.Poll());

            List<RollbackRequest<GameState, GameInput>> requests = _session.AdvanceFrame();
            foreach (RollbackRequest<GameState, GameInput> request in requests)
            {
                switch (request.Kind)
                {
                    case RollbackRequestKind.SaveGameStateReq:
                        var saveReq = request.GetSaveGameStateReq();
                        saveReq.Cell.Save(saveReq.Frame, _curState, _curState.Checksum());
                        break;
                    case RollbackRequestKind.LoadGameStateReq:
                        var loadReq = request.GetLoadGameStateReq();
                        loadReq.Cell.Load(out _curState);
                        break;
                    case RollbackRequestKind.AdvanceFrameReq:
                        _curState.Advance(request.GetAdvanceFrameRequest().Inputs, _characters, _config);
                        break;
                }
            }

            if (_session.ConfirmedState().FightersDead())
            {
                DeInit();
                return;
            }
            InfoOverlayDetails details = new InfoOverlayDetails
            {
                HasPing = true,
                Ping = _session.NetworkStats(_remoteHandle).Ping,
            };
            _view.Render(_curState, _config, details);
        }
    }
}
