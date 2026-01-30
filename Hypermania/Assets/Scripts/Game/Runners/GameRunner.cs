using System.Collections.Generic;
using Design;
using Design.Animation;
using Game.Sim;
using Game.View;
using Netcode.P2P;
using Netcode.Rollback;
using Steamworks;
using UnityEngine;

namespace Game.Runners
{
    public abstract class GameRunner : MonoBehaviour
    {
        [SerializeField]
        protected GameView _view;

        [SerializeField]
        protected GlobalConfig _config;

        [SerializeField]
        protected bool _drawHitboxes;

        /// <summary>
        /// The current state of the runner. If you derive from this class, it must be initialized on Init();
        /// </summary>
        protected GameState _curState;

        /// <summary>
        /// The characters of each player. _characters[i] should represent the chararcter being played by handle i. If
        /// you derive from this class, it must be initialized on Init();
        /// </summary>
        protected CharacterConfig[] _characters;
        protected InputBuffer _inputBuffer;
        protected bool _initialized;
        protected float _time;

        public virtual void Init(
            List<(PlayerHandle playerHandle, PlayerKind playerKind, SteamNetworkingIdentity address)> players,
            P2PClient client
        )
        {
            // TODO: take in character selections from matchmaking/lobby
            CharacterConfig sampleConfig = _config.Get(Character.SampleFighter);
            _characters = new CharacterConfig[players.Count];
            for (int i = 0; i < players.Count; i++)
            {
                _characters[i] = sampleConfig;
            }
            _curState = GameState.Create(_characters);
            _view.Init(_characters);
            _inputBuffer = new InputBuffer();
            _time = 0;
            _initialized = true;
        }

        public abstract void Poll(float deltaTime);

        public virtual void DeInit()
        {
            _initialized = false;
            _time = 0;
            _inputBuffer = null;
            _view.DeInit();
            _curState = null;
            _characters = null;
        }

        public void OnDrawGizmos()
        {
            if (!_drawHitboxes)
                return;
            if (
                _curState == null
                || _characters == null
                || _view == null
                || _view.Fighters == null
                || _curState.Fighters == null
            )
                return;

            for (int i = 0; i < _curState.Fighters.Length; i++)
            {
                var fighterView = _view.Fighters[i];
                CharacterState anim = _curState.Fighters[i].State;
                int tick = _curState.Frame - _curState.Fighters[i].StateStart;
                FrameData frame = _characters[i].GetFrameData(anim, tick);

                Transform t = fighterView.transform;

                foreach (var box in frame.Boxes)
                {
                    var kind = box.Props.Kind;
                    if (kind == HitboxKind.Hurtbox)
                        Gizmos.color = Color.blue;
                    else if (kind == HitboxKind.Hitbox)
                        Gizmos.color = Color.red;
                    else
                        continue;

                    Vector2 centerLocal = (Vector2)box.CenterLocal;
                    if (_curState.Fighters[i].FacingDir == FighterFacing.Left)
                    {
                        centerLocal.x *= -1;
                    }
                    Vector2 sizeLocal = (Vector2)box.SizeLocal;

                    Vector3 centerWorld = t.TransformPoint(new Vector3(centerLocal.x, centerLocal.y, 0f));
                    Vector3 halfWorldX = t.TransformVector(new Vector3(sizeLocal.x * 0.5f, 0f, 0f));
                    Vector3 halfWorldY = t.TransformVector(new Vector3(0f, sizeLocal.y * 0.5f, 0f));

                    Vector3 p0 = centerWorld - halfWorldX - halfWorldY;
                    Vector3 p1 = centerWorld + halfWorldX - halfWorldY;
                    Vector3 p2 = centerWorld + halfWorldX + halfWorldY;
                    Vector3 p3 = centerWorld - halfWorldX + halfWorldY;

                    Gizmos.DrawLine(p0, p1);
                    Gizmos.DrawLine(p1, p2);
                    Gizmos.DrawLine(p2, p3);
                    Gizmos.DrawLine(p3, p0);
                }
            }
        }
    }
}
