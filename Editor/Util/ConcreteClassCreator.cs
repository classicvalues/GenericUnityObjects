﻿namespace GenericUnityObjects.Editor.Util
{
    using System;
    using System.IO;
    using System.Linq;
    using GeneratedTypesDatabase;
    using GenericUnityObjects.Util;
    using SolidUtilities.Extensions;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// A class responsible for generation on concrete class assemblies. It also adds created concrete classes to database.
    /// </summary>
    /// <typeparam name="TObject"> A type derived from <see cref="UnityEngine.Object"/>. </typeparam>
    internal static class ConcreteClassCreator<TObject>
        where TObject : Object
    {
        public static event Action<Type, Type[]> ConcreteClassAdded;

        /// <summary>
        /// Creates a new concrete class assembly, adds it to the database, and adds a task to update behaviour icon
        /// if the generic type is MonoBehaviour.
        /// </summary>
        /// <param name="genericTypeWithoutArgs">Generic type definition of a type that has to be created.</param>
        /// <param name="argumentTypes">Generic arguments that a concrete class has to use.</param>
        public static void CreateConcreteClass(Type genericTypeWithoutArgs, Type[] argumentTypes)
        {
            string assemblyGUID = CreateConcreteClassAssembly(genericTypeWithoutArgs, argumentTypes);
            var genericTypeInfo = GenericTypeInfo.Instantiate<TObject>(genericTypeWithoutArgs);

            IconSetter.AddAssemblyForIconChange(genericTypeInfo.GUID, assemblyGUID, typeof(TObject) == typeof(GenericScriptableObject));

            AddToDatabase(genericTypeInfo, argumentTypes, assemblyGUID);

            ConcreteClassAdded?.Invoke(genericTypeWithoutArgs, argumentTypes);
        }

        /// <summary>
        /// Updates a concrete class assembly in a way that already created assets don't lose a reference to it.
        /// </summary>
        /// <param name="genericType">Generic type definition of a type that has to be updated.</param>
        /// <param name="argumentTypes">Generic arguments that a concrete class has to use.</param>
        /// <param name="concreteClass">A reference to the concrete class that has to be updated.</param>
        public static void UpdateConcreteClassAssembly(Type genericType, Type[] argumentTypes, ConcreteClass concreteClass, string genericTypeGUID)
        {
            string newAssemblyName;

            try
            {
                newAssemblyName = GetConcreteClassAssemblyName(genericType, argumentTypes);
            }
            catch (TypeLoadException)
            {
                return;
            }

            using (AssemblyAssetOperations.AssemblyReplacer.UsingGUID(concreteClass.AssemblyGUID, newAssemblyName))
            {
                CreateConcreteClassAssembly(genericType, argumentTypes, newAssemblyName, concreteClass.AssemblyGUID);
            }

            IconSetter.AddAssemblyForIconChange(genericTypeGUID, concreteClass.AssemblyGUID, typeof(TObject) == typeof(GenericScriptableObject));
        }



        private static void CreateConcreteClassAssembly(Type genericTypeWithoutArgs, Type[] argumentTypes,
            string newAssemblyName, string assemblyGUID)
        {
            AssemblyCreator.CreateConcreteClass<TObject>(newAssemblyName, genericTypeWithoutArgs.MakeGenericType(argumentTypes), assemblyGUID);
        }

        private static void AddToDatabase(GenericTypeInfo genericTypeInfo, Type[] argumentTypes, string assemblyGUID)
        {
            GenerationDatabase<TObject>.AddConcreteClass(genericTypeInfo, argumentTypes, assemblyGUID);
        }

        private static string CreateConcreteClassAssembly(Type genericTypeWithoutArgs, Type[] argumentTypes)
        {
            string assemblyName = GetConcreteClassAssemblyName(genericTypeWithoutArgs, argumentTypes);
            string assemblyPath = $"{Config.AssembliesDirPath}/{assemblyName}.dll";

            string assemblyGUID;

            using (new DisabledAssetDatabase(true))
            {
                assemblyGUID = AssemblyGeneration.GetUniqueGUID();
                CreateConcreteClassAssembly(genericTypeWithoutArgs, argumentTypes, assemblyName, assemblyGUID);
                AssemblyGeneration.ImportAssemblyAsset(assemblyPath, assemblyGUID);
            }

            return assemblyGUID;
        }

        private static string GetShortNameForNaming(string fullName)
        {
            int lastDotIndex = fullName.LastIndexOf('.');

            if (lastDotIndex != -1)
            {
                fullName = fullName.Substring(lastDotIndex + 1, fullName.Length - lastDotIndex - 1);
            }

            int backtickIndex = fullName.IndexOf('`');

            if (backtickIndex != -1)
            {
                fullName = fullName.Substring(0, backtickIndex);
            }

            return fullName;
        }

        private static string GetConcreteClassAssemblyName(Type genericTypeWithoutArgs, Type[] genericArgs)
        {
            var argumentsNames = genericArgs.Select(argument => GetShortNameForNaming(argument.FullName));
            string newAssemblyName = $"{GetShortNameForNaming(genericTypeWithoutArgs.FullName)}_{string.Join("_", argumentsNames)}";
            var dirInfo = new DirectoryInfo(Config.AssembliesDirPath);
            int identicalFilesCount = dirInfo.GetFiles($"{newAssemblyName}*.dll").Length;

            if (identicalFilesCount == 0)
                return newAssemblyName;

            Type genericTypeWithArgs = genericTypeWithoutArgs.MakeGenericType(genericArgs);
            bool typeExists = TypeCache.GetTypesDerivedFrom(genericTypeWithArgs).Any(type => type.IsEmpty());

            if (typeExists)
                throw new TypeLoadException($"The type {genericTypeWithArgs} already exists. No need to create one more.");

            newAssemblyName += $"_{identicalFilesCount}";
            return newAssemblyName;
        }
    }
}