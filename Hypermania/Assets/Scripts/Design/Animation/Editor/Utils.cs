using UnityEditor;
using UnityEngine;

namespace Design.Animation.Editors
{
    public static class Utils
    {
        public static Rect LocalBoxToGuiRect(Rect rect, Camera cam, Transform root, BoxData box)
        {
            float hw = (float)box.SizeLocal.x * 0.5f;
            float hh = (float)box.SizeLocal.y * 0.5f;

            Vector3 w0 = root.TransformPoint(
                new Vector3((float)box.CenterLocal.x - hw, (float)box.CenterLocal.y - hh, 0)
            );
            Vector3 w1 = root.TransformPoint(
                new Vector3((float)box.CenterLocal.x + hw, (float)box.CenterLocal.y + hh, 0)
            );

            Vector2 g0 = WorldToGui(rect, cam, w0);
            Vector2 g1 = WorldToGui(rect, cam, w1);

            return Rect.MinMaxRect(
                Mathf.Min(g0.x, g1.x),
                Mathf.Min(g0.y, g1.y),
                Mathf.Max(g0.x, g1.x),
                Mathf.Max(g0.y, g1.y)
            );
        }

        public static Vector2 WorldToGui(Rect rect, Camera cam, Vector3 world)
        {
            // PreviewRenderUtility renders into a texture sized: rect * pixelsPerPoint.
            // WorldToScreenPoint returns pixel coordinates in that render target.
            Vector3 sp = cam.WorldToScreenPoint(world);

            float ppp = EditorGUIUtility.pixelsPerPoint;
            float texW = Mathf.Max(1f, rect.width * ppp);
            float texH = Mathf.Max(1f, rect.height * ppp);

            float x = rect.xMin + (sp.x / texW) * rect.width;
            float y = rect.yMin + (1f - (sp.y / texH)) * rect.height;

            return new Vector2(x, y);
        }

        public static void DrawRectOutline(Rect r, float t, Color color)
        {
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, r.width, t), color);
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMax - t, r.width, t), color);
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, t, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.yMin, t, r.height), color);
        }

        public static void DrawCentered(Rect r, string text)
        {
            var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            GUI.Label(r, text, style);
        }
    }
}
