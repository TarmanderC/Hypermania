using System;
using System.Collections.Generic;
using Design;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Conductor))]
    public class GameView : MonoBehaviour
    {
        private Conductor _conductor;
        public FighterView[] Fighters => _fighters;

        private FighterView[] _fighters;
        private CharacterConfig[] _characters;

        [SerializeField]
        private FighterIndicatorManager FighterIndicatorManager;
        public HealthBarView[] Healthbars;

        [SerializeField]
        public ManiaView[] Manias;

        private float Zoom = 5f;

        [SerializeField]
        private CameraControl CameraControl;

        [SerializeField]
        private ComboCountView[] ComboViews;

        public void OnValidate()
        {
            if (Healthbars == null)
            {
                throw new InvalidOperationException("Healthbars should exist");
            }
            if (Healthbars.Length != 2)
            {
                throw new InvalidOperationException("Healthbar length should be 2");
            }
            if (CameraControl == null)
            {
                throw new InvalidOperationException("Camera control must be assigned to the game view!");
            }
            for (int i = 0; i < 2; i++)
            {
                if (Healthbars[i] == null)
                {
                    throw new InvalidOperationException("Healthbars must be assigned to the game view!");
                }
            }
        }

        public void Init(CharacterConfig[] characters)
        {
            if (characters.Length != 2)
            {
                throw new InvalidOperationException("num characters in GameView must be 2");
            }
            _conductor = GetComponent<Conductor>();
            if (_conductor == null)
            {
                throw new InvalidOperationException(
                    "Conductor was null. Did you forget to assign a conductor component to the GameView?"
                );
            }
            _fighters = new FighterView[characters.Length];

            _characters = characters;
            for (int i = 0; i < characters.Length; i++)
            {
                _fighters[i] = Instantiate(_characters[i].Prefab);
                _fighters[i].name = "Fighter View";
                _fighters[i].transform.SetParent(transform, true);
                _fighters[i].Init(characters[i]);

                Manias[i].Init();
                Healthbars[i].SetMaxHealth((float)characters[i].Health);
            }
            _conductor.Init();
        }

        public void Render(in GameState state, GlobalConfig config)
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                _fighters[i].Render(state.Frame, state.Fighters[i]);
                Manias[i].Render(state.Frame, state.Manias[i]);
            }
            _conductor.RequestSlice(state.Frame);

            List<Vector2> interestPoints = new List<Vector2>();
            for (int i = 0; i < _characters.Length; i++)
            {
                interestPoints.Add((Vector2)state.Fighters[i].Position);
            }

            for (int i = 0; i < _characters.Length; i++)
            {
                Healthbars[i].SetHealth((int)state.Fighters[i].Health);
            }

            CameraControl.UpdateCamera(interestPoints, Zoom, Time.deltaTime);
            FighterIndicatorManager.Track(_fighters);

            for (int i = 0; i < _characters.Length; i++)
            {
                int combo = state.Fighters[i ^ 1].ComboedCount;
                ComboViews[i].SetComboCount(combo);
            }
        }

        public void DeInit()
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                _fighters[i].DeInit();
                Destroy(_fighters[i].gameObject);
                Manias[i].DeInit();
            }
            _fighters = null;
            _characters = null;
        }
    }
}
