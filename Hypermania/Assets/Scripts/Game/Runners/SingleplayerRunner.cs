using System;
using System.Collections.Generic;
using Design;
using Game.Sim;
using Netcode.P2P;
using Netcode.Rollback;
using Netcode.Rollback.Sessions;
using Steamworks;

namespace Game.Runners
{
    public class SingleplayerRunner : GameRunner
    {
        protected SyncTestSession<GameState, GameInput, SteamNetworkingIdentity> _session;

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
                .WithFps(64);
            foreach ((PlayerHandle playerHandle, PlayerKind playerKind, SteamNetworkingIdentity address) in players)
            {
                if (playerKind != PlayerKind.Local)
                {
                    throw new InvalidOperationException("Cannot have remote/spectators in a local session");
                }
                builder.AddPlayer(
                    new PlayerType<SteamNetworkingIdentity> { Kind = playerKind, Address = address },
                    playerHandle
                );
            }
            _session = builder.StartSynctestSession<GameState>();
        }

        public override void DeInit()
        {
            _session = null;
            base.DeInit();
        }

        public override void Poll(float deltaTime)
        {
            if (!_initialized)
            {
                return;
            }

            _inputBuffer.Saturate();

            float fpsDelta = 1.0f / GameManager.TPS;
            _time += deltaTime;

            while (_time > fpsDelta)
            {
                _time -= fpsDelta;
                GameLoop();
            }
        }

        protected void GameLoop()
        {
            if (_session == null)
            {
                return;
            }

            _session.AddLocalInput(new PlayerHandle(0), _inputBuffer.Consume());
            _session.AddLocalInput(new PlayerHandle(1), new GameInput(InputFlags.None));
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

            if (_curState.FightersDead())
            {
                DeInit();
                return;
            }

            _view.Render(_curState, _config);
        }
    }
}
