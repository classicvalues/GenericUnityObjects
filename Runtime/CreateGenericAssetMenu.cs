﻿namespace GenericScriptableObjects
{
    using System;

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class CreateGenericAssetMenuAttribute : Attribute
    {
        public const string DefaultNamespaceName = "GenericSOTypes";
        public const string DefaultScriptsPath = "Scripts/GenericSOTypes";

        /// <summary>The default file name used by newly created instances of this type.</summary>
        public string FileName { get; }

        /// <summary>The display name for this type shown in the Assets/Create menu.</summary>
        public string MenuName { get; }

        /// <summary>The position of the menu item within the Assets/Create menu.</summary>
        public int Order { get; } = 0;

        /// <summary>
        /// Custom namespace name to set for auto-generated non-generic types.
        /// Default is "GenericScriptableObjectsTypes".
        /// </summary>
        public string NamespaceName { get; } = DefaultNamespaceName;

        /// <summary>
        /// Custom path to a folder where auto-generated non-generic types must be kept.
        /// Default is "Scripts/GenericScriptableObjectTypes".
        /// </summary>
        public string ScriptsPath { get; } = DefaultScriptsPath;
    }
}