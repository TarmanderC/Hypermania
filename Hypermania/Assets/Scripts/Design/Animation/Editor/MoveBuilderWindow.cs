using Game;
using UnityEditor;
using UnityEngine;

namespace Design.Animation.Editors
{
    public sealed class MoveBuilderWindow : EditorWindow
    {
        [MenuItem("Tools/Hypermania/Move Builder")]
        public static void Open() => GetWindow<MoveBuilderWindow>("Move Builder");

        private MoveBuilderModel _model;
        private MoveBuilderControlsView _controls;
        private MoveBuilderPreviewView _preview;

        private void OnEnable()
        {
            _model = new MoveBuilderModel();
            _controls = new MoveBuilderControlsView();
            _preview = new MoveBuilderPreviewView();

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            _preview?.Dispose();
            _preview = null;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Preview scene objects must not survive mode transitions.
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
                _preview?.ResetPreviewObjects();
        }

        private void HandleGlobalKeyShortcuts(MoveBuilderModel m)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown)
                return;

            if (EditorGUIUtility.editingTextField)
                return;

            switch (e.keyCode)
            {
                case KeyCode.LeftArrow:
                    m.SetTick(m.CurrentTick - 1);
                    e.Use();
                    break;
                case KeyCode.RightArrow:
                    m.SetTick(m.CurrentTick + 1);
                    e.Use();
                    break;
                case KeyCode.Comma:
                    m.SetTick(0);
                    e.Use();
                    break;
                case KeyCode.Period:
                    m.SetTick(m.TotalTicks - 1);
                    e.Use();
                    break;
                case KeyCode.D:
                    if (e.control || e.command)
                    {
                        m.DuplicateSelected();
                        e.Use();
                    }
                    break;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    m.DeleteSelected();
                    e.Use();
                    break;
                case KeyCode.A:
                    if (e.shift)
                    {
                        m.AddBox(HitboxKind.Hurtbox);
                    }
                    else
                    {
                        m.AddBox(HitboxKind.Hitbox);
                    }
                    e.Use();
                    break;
                case KeyCode.F:
                    if (e.control || e.command)
                    {
                        m.SetBoxesFromPreviousFrame();
                        e.Use();
                    }
                    break;
            }
        }

        private void OnGUI()
        {
            if (_model == null)
                return;

            // ensure things are valid
            _model.SetTick(_model.CurrentTick);

            _controls.DrawToolbar(_model);

            HandleGlobalKeyShortcuts(_model);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (
                    new EditorGUILayout.VerticalScope(
                        new GUIStyle { padding = new RectOffset(8, 8, 8, 8) },
                        GUILayout.Width(320)
                    )
                )
                {
                    _controls.DrawLeft(_model, GameManager.TPS);
                }

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                {
                    Rect previewRect = GUILayoutUtility.GetRect(
                        10,
                        10,
                        GUILayout.ExpandWidth(true),
                        GUILayout.ExpandHeight(true)
                    );

                    GUI.BeginGroup(previewRect);
                    {
                        Rect localRect = new Rect(0, 0, previewRect.width, previewRect.height);
                        _preview.Draw(localRect, _model, GameManager.TPS);
                    }
                    GUI.EndGroup();

                    _controls.DrawBottomTimelineLayout(_model, GameManager.TPS);
                }
            }
        }
    }
}
