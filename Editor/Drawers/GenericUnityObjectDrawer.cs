﻿namespace GenericUnityObjects.Editor
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// The default object field drawn for types derived from <see cref="GenericScriptableObject"/> does not list
    /// available assets in the object picker window. This custom property drawer looks the same but lists the
    /// available assets.
    /// </summary>
    [CustomPropertyDrawer(typeof(GenericScriptableObject), true)]
    internal class GenericSODrawer : GenericUnityObjectDrawer { }

    [CustomPropertyDrawer(typeof(MonoBehaviour), true)]
    internal class GenericBehaviourDrawer : GenericUnityObjectDrawer { }

    internal class GenericUnityObjectDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            GenericObjectDrawer.ObjectField(position, property);
        }
    }
}