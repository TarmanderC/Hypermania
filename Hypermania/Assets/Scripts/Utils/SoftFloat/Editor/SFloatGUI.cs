using UnityEditor;
using UnityEngine;

namespace Utils.SoftFloat
{
    public static class SFloatGUI
    {
        public static sfloat Field(Rect position, GUIContent label, sfloat value)
        {
            float f = (float)value;
            float next = EditorGUI.FloatField(position, label, f);
            return (sfloat)next;
        }

        public static sfloat Field(GUIContent label, sfloat value) =>
            (sfloat)EditorGUILayout.FloatField(label, (float)value);

        public static sfloat Field(string label, sfloat value) =>
            (sfloat)EditorGUILayout.FloatField(label, (float)value);

        public static SVector2 Field(Rect position, GUIContent label, SVector2 value)
        {
            var v = new Vector2((float)value.x, (float)value.y);
            var next = EditorGUI.Vector2Field(position, label, v);
            return new SVector2((sfloat)next.x, (sfloat)next.y);
        }

        public static SVector2 Field(string label, SVector2 value)
        {
            var v = new Vector2((float)value.x, (float)value.y);
            var next = EditorGUILayout.Vector2Field(label, v);
            return new SVector2((sfloat)next.x, (sfloat)next.y);
        }

        public static SVector3 Field(Rect position, GUIContent label, SVector3 value)
        {
            var v = new Vector3((float)value.x, (float)value.y, (float)value.z);
            var next = EditorGUI.Vector3Field(position, label, v);
            return new SVector3((sfloat)next.x, (sfloat)next.y, (sfloat)next.z);
        }

        public static SVector3 Field(string label, SVector3 value)
        {
            var v = new Vector3((float)value.x, (float)value.y, (float)value.z);
            var next = EditorGUILayout.Vector3Field(label, v);
            return new SVector3((sfloat)next.x, (sfloat)next.y, (sfloat)next.z);
        }

        public static sfloat ReadSfloat(SerializedProperty sfloatProp)
        {
            var raw = sfloatProp.FindPropertyRelative("rawValue");
            uint r = unchecked((uint)raw.intValue);
            return sfloat.FromRaw(r);
        }

        public static void WriteSfloat(SerializedProperty sfloatProp, sfloat value)
        {
            var raw = sfloatProp.FindPropertyRelative("rawValue");
            raw.intValue = unchecked((int)value.RawValue);
        }

        public static SVector2 ReadSVector2(SerializedProperty sv2Prop)
        {
            var x = ReadSfloat(sv2Prop.FindPropertyRelative("x"));
            var y = ReadSfloat(sv2Prop.FindPropertyRelative("y"));
            return new SVector2(x, y);
        }

        public static void WriteSVector2(SerializedProperty sv2Prop, SVector2 value)
        {
            WriteSfloat(sv2Prop.FindPropertyRelative("x"), value.x);
            WriteSfloat(sv2Prop.FindPropertyRelative("y"), value.y);
        }

        public static SVector3 ReadSVector3(SerializedProperty sv3Prop)
        {
            var x = ReadSfloat(sv3Prop.FindPropertyRelative("x"));
            var y = ReadSfloat(sv3Prop.FindPropertyRelative("y"));
            var z = ReadSfloat(sv3Prop.FindPropertyRelative("z"));
            return new SVector3(x, y, z);
        }

        public static void WriteSVector3(SerializedProperty sv3Prop, SVector3 value)
        {
            WriteSfloat(sv3Prop.FindPropertyRelative("x"), value.x);
            WriteSfloat(sv3Prop.FindPropertyRelative("y"), value.y);
            WriteSfloat(sv3Prop.FindPropertyRelative("z"), value.z);
        }
    }
}
