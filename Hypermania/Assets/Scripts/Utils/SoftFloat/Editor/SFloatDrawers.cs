using UnityEditor;
using UnityEngine;

namespace Utils.SoftFloat
{
    [CustomPropertyDrawer(typeof(sfloat))]
    public sealed class SFloatDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var value = SFloatGUI.ReadSfloat(property);
            var next = SFloatGUI.Field(position, label, value);
            if (!Equals(next, value))
                SFloatGUI.WriteSfloat(property, next);
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight;
    }

    [CustomPropertyDrawer(typeof(SVector2))]
    public sealed class SVector2Drawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var value = SFloatGUI.ReadSVector2(property);
            var next = SFloatGUI.Field(position, label, value);
            if (!Equals(next, value))
                SFloatGUI.WriteSVector2(property, next);
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight;
    }

    public sealed class SVector3Drawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var value = SFloatGUI.ReadSVector3(property);
            var next = SFloatGUI.Field(position, label, value);
            if (!Equals(next, value))
                SFloatGUI.WriteSVector3(property, next);
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight;
    }
}
