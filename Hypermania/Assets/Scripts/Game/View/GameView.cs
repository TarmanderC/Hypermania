using System;
using System.Collections.Generic;
using Design;
using Game.Sim;
using Game.View.Fighters;
using Game.View.Mania;
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
        private FighterIndicatorManager _fighterIndicatorManager;

        [SerializeField]
        private HealthBarView[] _healthbars;

        [SerializeField]
        private ManiaView[] _manias;

        [SerializeField]
        private float _zoom = 1.6f;

        [SerializeField]
        private CameraControl _cameraControl;

        [SerializeField]
        private ComboCountView[] _comboViews;

        [SerializeField]
        private InfoOverlayView _overlayView;

        [SerializeField]
        private RoundTimerView _roundTimerView;

        public void OnValidate()
        {
            if (_healthbars == null)
            {
                throw new InvalidOperationException("Healthbars should exist");
            }
            if (_healthbars.Length != 2)
            {
                throw new InvalidOperationException("Healthbar length should be 2");
            }
            if (_cameraControl == null)
            {
                throw new InvalidOperationException("Camera control must be assigned to the game view!");
            }
            for (int i = 0; i < 2; i++)
            {
                if (_healthbars[i] == null)
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

                _manias[i].Init();
                _healthbars[i].SetMaxHealth((float)characters[i].Health);
            }
            _conductor.Init();
        }

        public void Render(in GameState state, GlobalConfig config, InfoOverlayDetails overlayDetails)
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                _fighters[i].Render(state.Frame, state.Fighters[i]);
                _manias[i].Render(state.Frame, state.Manias[i]);
            }
            _conductor.RequestSlice(state.Frame);

            List<Vector2> interestPoints = new List<Vector2>();
            for (int i = 0; i < _characters.Length; i++)
            {
                interestPoints.Add((Vector2)state.Fighters[i].Position);
                // ensure that fighter heads are included
                interestPoints.Add(
                    (Vector2)state.Fighters[i].Position + new Vector2(0, (float)_characters[i].CharacterHeight)
                );
            }

            for (int i = 0; i < _characters.Length; i++)
            {
                _healthbars[i].SetHealth((int)state.Fighters[i].Health);
            }

            _cameraControl.UpdateCamera(interestPoints, _zoom);
            _fighterIndicatorManager.Track(state.Fighters);

            for (int i = 0; i < _characters.Length; i++)
            {
                int combo = state.Fighters[i ^ 1].ComboedCount;
                _comboViews[i].SetComboCount(combo);
            }
            _overlayView.Render(overlayDetails);
            _roundTimerView.DisplayRoundTimer(state.Frame, state.RoundEnd);
        }

        public void DeInit()
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                _fighters[i].DeInit();
                Destroy(_fighters[i].gameObject);
                _manias[i].DeInit();
            }
            _fighters = null;
            _characters = null;
        }
    }
}
