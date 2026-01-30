using Game;
using UnityEditor;
using UnityEngine;
using Utils.SoftFloat;

namespace Design.Animation.Editors
{
    public sealed class MoveBuilderControlsView
    {
        public void DrawLeft(MoveBuilderModel m, int tps)
        {
            m.CharacterPrefab = (GameObject)
                EditorGUILayout.ObjectField("Character Prefab", m.CharacterPrefab, typeof(GameObject), false);

            m.Clip = (AnimationClip)EditorGUILayout.ObjectField("Animation Clip", m.Clip, typeof(AnimationClip), false);

            m.Data = (HitboxData)EditorGUILayout.ObjectField("Move Data (Asset)", m.Data, typeof(HitboxData), false);

            EditorGUILayout.Space(8);

            if (!m.HasAllInputs)
            {
                EditorGUILayout.HelpBox("Assign Prefab, Clip, and Move Data asset.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("Initialize Data"))
                m.BindDataToClipLength(m, tps);
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);
            DrawControls(m);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Box List", EditorStyles.boldLabel);
            DrawBoxList(m);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Selected Box", EditorStyles.boldLabel);
            DrawSelectedBoxInspector(m);

            EditorGUILayout.Space(8);
            if (m.HasUnsavedChanges)
            {
                EditorGUILayout.HelpBox("You have unsaved changes.", MessageType.Warning);
            }
        }

        public void DrawBottomTimelineLayout(MoveBuilderModel m, int tps)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!m.HasAllInputs)
                {
                    EditorGUILayout.LabelField("Timeline: assign Prefab, Clip, and Move Data.", EditorStyles.miniLabel);
                    return;
                }

                int total = Mathf.Max(1, m.TotalTicks);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("<<", GUILayout.Width(36)))
                        m.SetTick(0);
                    if (GUILayout.Button("<", GUILayout.Width(36)))
                        m.SetTick(m.CurrentTick - 1);

                    int newTick = EditorGUILayout.IntSlider(m.CurrentTick, 0, total - 1);
                    if (newTick != m.CurrentTick)
                        m.SetTick(newTick);

                    if (GUILayout.Button(">", GUILayout.Width(36)))
                        m.SetTick(m.CurrentTick + 1);
                    if (GUILayout.Button(">>", GUILayout.Width(36)))
                        m.SetTick(total - 1);

                    GUILayout.Space(8);

                    float time = m.CurrentTimeSeconds(tps);
                    GUILayout.Label(
                        $"Tick {m.CurrentTick}/{total - 1}  ({time:0.000}s @ {tps}Hz)",
                        EditorStyles.miniLabel,
                        GUILayout.Width(220)
                    );
                }
            }
        }

        private void DrawControls(MoveBuilderModel m)
        {
            var frame = m.GetCurrentFrame();
            if (frame == null)
                return;

            if (GUILayout.Button("Add Hurtbox (Shift A)"))
                m.AddBox(HitboxKind.Hurtbox);
            if (GUILayout.Button("Add Hitbox (A)"))
                m.AddBox(HitboxKind.Hitbox);
            using (new EditorGUI.DisabledScope(m.SelectedBoxIndex < 0 || m.SelectedBoxIndex >= frame.Boxes.Count))
            {
                if (GUILayout.Button("Duplicate Selected (Ctrl D)"))
                    m.DuplicateSelected();
                if (GUILayout.Button("Delete Selected (Backspace/Delete)"))
                    m.DeleteSelected();
            }
            using (new EditorGUI.DisabledScope(m.CurrentTick <= 0))
            {
                if (GUILayout.Button("Set Hitboxes from Previous Frame (Ctrl F)"))
                    m.SetBoxesFromPreviousFrame();
            }
        }

        private void DrawBoxList(MoveBuilderModel m)
        {
            var frame = m.GetCurrentFrame();
            if (frame == null)
                return;

            for (int i = 0; i < frame.Boxes.Count; i++)
            {
                var b = frame.Boxes[i];
                bool sel = i == m.SelectedBoxIndex;
                string label = $"{i}: {b.Props.Kind} - {b.Name}";

                if (GUILayout.Toggle(sel, label, "Button") && !sel)
                {
                    m.SelectBox(i);
                }
            }
        }

        private void DrawSelectedBoxInspector(MoveBuilderModel m)
        {
            var frame = m.GetCurrentFrame();
            if (frame == null)
                return;

            if (m.SelectedBoxIndex < 0 || m.SelectedBoxIndex >= frame.Boxes.Count)
            {
                EditorGUILayout.HelpBox("Select a box to edit. Drag in preview to move/resize.", MessageType.None);
                return;
            }

            var box = frame.Boxes[m.SelectedBoxIndex];

            box.Name = EditorGUILayout.TextField("Name", box.Name);
            box.CenterLocal = SFloatGUI.Field("Center (Local)", box.CenterLocal);

            box.SizeLocal = SFloatGUI.Field("Size (Local)", box.SizeLocal);
            box.SizeLocal.x = Mathsf.Max((sfloat)0.001f, box.SizeLocal.x);
            box.SizeLocal.y = Mathsf.Max((sfloat)0.001f, box.SizeLocal.y);

            var p = box.Props;
            p.Kind = (HitboxKind)EditorGUILayout.EnumPopup("Kind", p.Kind);

            using (new EditorGUI.DisabledScope(p.Kind != HitboxKind.Hitbox))
            {
                p.Damage = EditorGUILayout.IntField("Damage", p.Damage);
                p.HitstunTicks = EditorGUILayout.IntField("Hitstun (ticks)", p.HitstunTicks);
                p.BlockstunTicks = EditorGUILayout.IntField("Blockstun (ticks)", p.BlockstunTicks);
                p.Knockback = SFloatGUI.Field("Knockback", p.Knockback);
                p.StartsRhythmCombo = EditorGUILayout.Toggle("Starts rhythm combo", p.StartsRhythmCombo);
            }
            box.Props = p;

            m.SetBox(m.SelectedBoxIndex, box);
        }

        public void DrawToolbar(MoveBuilderModel m)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Move Builder", EditorStyles.toolbarButton);

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(m == null || !m.HasUnsavedChanges))
                {
                    if (
                        GUILayout.Button(
                            m.HasUnsavedChanges ? "Apply*" : "Apply",
                            EditorStyles.toolbarButton,
                            GUILayout.Width(60)
                        )
                    )
                    {
                        m.SaveAsset();
                        GUI.FocusControl(null);
                    }
                }
            }
        }
    }
}
