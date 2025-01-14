﻿namespace GenericUnityObjects.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using GeneratedTypesDatabase;
    using GenericUnityObjects.Util;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Assertions;
    using Util;
    using Object = UnityEngine.Object;

    /// <summary>
    /// A class that gathers data from <see cref="GenerationDatabase{TUnityObject}"/> and
    /// fills <see cref="GenericTypesDatabase{TObject}"/> with items to be used at runtime.
    /// </summary>
    /// <typeparam name="TObject"> A type derived from <see cref="UnityEngine.Object"/>. </typeparam>
    internal static class DictInitializer<TObject>
        where TObject : Object
    {
        public static void Initialize()
        {
            var genericTypes = GenerationDatabase<TObject>.GenericTypes;
            var dict = new Dictionary<Type, Dictionary<Type[], Type>>(genericTypes.Length);

            foreach (GenericTypeInfo genericTypeInfo in genericTypes)
            {
                CheckSelectorAssembly(genericTypeInfo);

                Type genericType = genericTypeInfo.RetrieveType<TObject>(false);
                var concreteClassesDict = CreateConcreteClassesDict(genericTypeInfo);
                dict.Add(genericType, concreteClassesDict);
            }

            GenericTypesDatabase<TObject>.Initialize(dict);
        }

        private static void CheckSelectorAssembly(GenericTypeInfo genericTypeInfo)
        {
            if (string.IsNullOrEmpty(genericTypeInfo.AssemblyGUID))
                return;

            string assemblyPath = AssetDatabase.GUIDToAssetPath(genericTypeInfo.AssemblyGUID);

            if (!File.Exists(assemblyPath))
                return;

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assemblyPath);

            if (script == null)
                FailedAssembliesChecker.FailedAssemblyPaths.Add(assemblyPath);
        }

        private static Dictionary<Type[], Type> CreateConcreteClassesDict(GenericTypeInfo genericTypeInfo)
        {
            var concreteClasses = GenerationDatabase<TObject>.GetConcreteClasses(genericTypeInfo);
            var concreteClassesDict = new Dictionary<Type[], Type>(concreteClasses.Length, default(TypeArrayComparer));

            foreach (ConcreteClass concreteClass in concreteClasses)
            {
                var key = GetConcreteClassArguments(concreteClass);

                if (TryGetConcreteClassType(genericTypeInfo, concreteClass, out Type value))
                    concreteClassesDict.Add(key, value);
            }

            return concreteClassesDict;
        }

        private static Type[] GetConcreteClassArguments(ConcreteClass concreteClass)
        {
            int argsLength = concreteClass.Arguments.Length;

            Type[] arguments = new Type[argsLength];

            for (int i = 0; i < argsLength; i++)
            {
                var type = concreteClass.Arguments[i].RetrieveType<TObject>();
                Assert.IsNotNull(type);
                arguments[i] = type;
            }

            return arguments;
        }

        private static bool TryGetConcreteClassType(GenericTypeInfo genericTypeInfo, ConcreteClass concreteClass, out Type type)
        {
            type = null;
            string assemblyPath = AssetDatabase.GUIDToAssetPath(concreteClass.AssemblyGUID);

            // This means the assembly was physically removed, so it shouldn't be in the database anymore.
            if ( ! File.Exists(assemblyPath))
            {
                GenerationDatabase<TObject>.RemoveConcreteClass(genericTypeInfo, concreteClass);
                return false;
            }

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assemblyPath);

            // There was once NullReferenceReference here because Unity lost a MonoScript asset connected to
            // the concrete class assembly. Would be great to find a consistent reproduction of the issue.
            if (script == null)
            {
                FailedAssembliesChecker.FailedAssemblyPaths.Add(assemblyPath);
                return false;
            }

            type = script.GetClass();
            return true;
        }
    }
}