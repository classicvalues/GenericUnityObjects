﻿namespace GenericUnityObjects.Util
{
#if UNITY_EDITOR
#endif
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Database;
    using TypeReferences;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Assertions;
    using Debug = UnityEngine.Debug;

    /// <summary>
    /// A database of all the type parameters of generic scriptable objects and their matching concrete implementations.
    /// When a new GenericScriptableObject asset is created through Unity context menu, a concrete implementation is
    /// created and added to this dictionary.
    /// <example>
    /// CustomGeneric&lt;T>
    ///     bool --- CustomGeneric_Boolean
    ///     int  --- CustomGeneric_Int32
    /// CustomGeneric&lt;T1,T2>
    ///     bool, int --- CustomGeneric_Boolean_Int32
    ///     bool, float --- CustomGeneric_Boolean_Single
    /// </example>
    /// </summary>
    internal class GenericObjectDatabase :
        SingletonScriptableObject<GenericObjectDatabase>,
        ISerializationCallbackReceiver
    {
        private readonly Dictionary<TypeReference, TypeDictionary> _dict =
            new Dictionary<TypeReference, TypeDictionary>();

        [HideInInspector]
        [SerializeField] private TypeReference[] _keys;

        [HideInInspector]
        [SerializeField] private TypeDictionary[] _values;

        /// <summary>
        /// <see cref="EditorUtility.SetDirty"/> cannot be called in OnAfterDeserialize, so the need to call the
        /// method must be saved and be called later (in <see cref="OnEnable"/>).
        /// </summary>
        private bool _shouldSetDirty;

        public static void Add(Type genericType, Type value)
        {
            var genericArgs = genericType.GetGenericArguments();
            Assert.IsFalse(genericArgs.Length == 0);
            Type genericTypeWithoutArgs = genericType.GetGenericTypeDefinition();
            Add(genericTypeWithoutArgs, genericArgs, value);
        }

        public static void Add(Type genericTypeWithoutArgs, Type[] key, Type value)
        {
            Assert.IsTrue(genericTypeWithoutArgs.IsGenericTypeDefinition);
            TypeDictionary assetDict = GetAssetDict(genericTypeWithoutArgs);
            assetDict.Add(key, value);
            SetInstanceDirty();
        }

        public static bool ContainsKey(Type genericType, Type[] key)
        {
            Assert.IsTrue(genericType.IsGenericTypeDefinition);
            TypeDictionary assetDict = GetAssetDict(genericType);
            return assetDict.ContainsKey(key);
        }

        public static bool TryGetValue(Type genericType, out Type value)
        {
            var genericArgs = genericType.GetGenericArguments();
            Assert.IsFalse(genericArgs.Length == 0);
            Type genericTypeWithoutArgs = genericType.GetGenericTypeDefinition();
            return TryGetValue(genericTypeWithoutArgs, genericArgs, out value);
        }

        public static bool TryGetValue(Type genericTypeWithoutArgs, Type[] genericArgs, out Type value)
        {
            Assert.IsTrue(genericTypeWithoutArgs.IsGenericTypeDefinition);
            TypeDictionary assetDict = GetAssetDict(genericTypeWithoutArgs);
            bool success = assetDict.TryGetValue(genericArgs, out value);

            if ( ! success)
            {
                success = TryFindExistingType(genericTypeWithoutArgs, genericArgs, out value);
            }

            return success;
        }

        public void OnAfterDeserialize()
        {
            int keysLength = _keys.Length;
            int valuesLength = _values.Length;

            if (keysLength != valuesLength)
            {
                Debug.LogError($"Something wrong happened in the database. Keys count ({keysLength}) does " +
                               $"not equal to values count ({valuesLength}). The database will be cleaned up.");
                _shouldSetDirty = true;
                return;
            }

            Assert.IsTrue(_dict.Count == 0);

            for (int i = 0; i < keysLength; ++i)
            {
                TypeReference typeRef = _keys[i];

                if (typeRef.TypeIsMissing())
                    continue;

                _dict[typeRef] = _values[i];
            }

            if (_dict.Count != keysLength)
                _shouldSetDirty = true;

            TypeReference.TypeRestoredFromGUID += ReAddKey;
        }

        private void ReAddKey(TypeReference typeRef)
        {
            var previousTypeRef = new TypeReference( (Type)null, typeRef.GUID);

            if ( ! _dict.TryGetValue(previousTypeRef, out TypeDictionary typeDict))
                return;

            _dict.Remove(previousTypeRef);
            SetDirty();

            if (_dict.ContainsKey(typeRef))
                return;

            _dict[typeRef] = typeDict;
        }

        public void OnBeforeSerialize()
        {
            int dictLength = _dict.Count;

            _keys = new TypeReference[dictLength];
            _values = new TypeDictionary[dictLength];

            int keysIndex = 0;
            foreach (var pair in _dict)
            {
                _keys[keysIndex] = pair.Key;
                _values[keysIndex] = pair.Value;
                ++keysIndex;
            }
        }

        private static bool TryFindExistingType(Type genericTypeWithoutArgs, Type[] genericArgs, out Type existingType)
        {
            var genericType = genericTypeWithoutArgs.MakeGenericType(genericArgs);
            existingType = TypeHelper.GetEmptyTypeDerivedFrom(genericType);

            if (existingType == null)
                return false;

            Add(genericTypeWithoutArgs, genericArgs, existingType);
            return true;
        }

        private static TypeDictionary GetAssetDict(Type genericType)
        {
            if (Instance._dict.TryGetValue(genericType, out TypeDictionary assetDict))
                return assetDict;

            assetDict = new TypeDictionary();
            Instance._dict.Add(new TypeReference(genericType, suppressLogs: true), assetDict);
            SetInstanceDirty();
            return assetDict;
        }

        private void OnEnable()
        {
            if ( ! _shouldSetDirty)
                return;

            _shouldSetDirty = false;
            SetDirty();
        }

        private static void SetInstanceDirty()
        {
#if UNITY_EDITOR
            EditorUtility.SetDirty(Instance);
#endif
        }
    }
}