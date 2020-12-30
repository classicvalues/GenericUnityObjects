﻿namespace GenericUnityObjects.Editor.MonoBehaviour
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using GenericUnityObjects.Util;
    using SolidUtilities.Editor.Helpers;
    using SolidUtilities.Extensions;
    using SolidUtilities.Helpers;
    using UnityEditor;
    using UnityEditor.Callbacks;
    using UnityEngine;
    using UnityEngine.Assertions;
    using Util;

    internal static class GenericBehavioursAnalyzer
    {
        private static bool _needsAssetDatabaseRefresh;

        [DidReloadScripts(Config.AssemblyGenerationOrder)]
        private static void OnScriptsReload()
        {
            _needsAssetDatabaseRefresh = false;

            CreatorUtil.WithDisabledAssetDatabase(() =>
            {
                Directory.CreateDirectory(Config.AssembliesDirPath);
                CheckArguments();
                CheckBehaviours();
            });

            InitializeBehavioursDatabase();

            if (_needsAssetDatabaseRefresh)
                AssetDatabase.Refresh();
        }

        private static void InitializeBehavioursDatabase()
        {
            var behaviours = BehavioursGenerationDatabase.Behaviours;
            var dict = new Dictionary<Type, Dictionary<Type[], Type>>(behaviours.Length);

            foreach (BehaviourInfo behaviourInfo in behaviours)
            {
                BehavioursGenerationDatabase.TryGetConcreteClasses(behaviourInfo, out var concreteClasses);

                Type behaviourType = behaviourInfo.RetrieveType();

                var concreteClassesDict = new Dictionary<Type[], Type>(concreteClasses.Length, default(TypeArrayComparer));

                foreach (ConcreteClass concreteClass in concreteClasses)
                {
                    int argsLength = concreteClass.Arguments.Length;

                    Type[] key = new Type[argsLength];

                    for (int i = 0; i < argsLength; i++)
                    {
                        var type = concreteClass.Arguments[i].RetrieveType();
                        Assert.IsNotNull(type);
                        key[i] = type;
                    }

                    string assemblyPath = AssetDatabase.GUIDToAssetPath(concreteClass.AssemblyGUID);
                    var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assemblyPath);

                    // There was once NullReferenceReference here because Unity lost a MonoScript asset connected to
                    // the concrete class assembly. Would be great to find a consistent reproduction of the issue.
                    Type value = script.GetClass();

                    concreteClassesDict.Add(key, value);
                }

                dict.Add(behaviourType, concreteClassesDict);
            }

            BehavioursDatabase.Initialize(dict);
        }

        private static void CheckArguments()
        {
            foreach (ArgumentInfo argument in BehavioursGenerationDatabase.Arguments)
            {
                if (argument.RetrieveType(out Type type, out bool retrievedFromGUID))
                {
                    if (retrievedFromGUID)
                    {
                        UpdateArgumentTypeName(argument, type);
                    }
                }
                else
                {
                    BehavioursGenerationDatabase.RemoveArgument(argument, RemoveAssembly);
                }
            }
        }

        private static void CheckBehaviours()
        {
            var oldBehaviours = BehavioursGenerationDatabase.Behaviours;
            var newBehaviours = TypeCache.GetTypesDerivedFrom<MonoBehaviour>()
                .Where(type => type.IsGenericType && ! type.IsAbstract)
                .Select(type => new BehaviourInfo(type))
                .ToArray();

            int oldBehavioursLength = oldBehaviours.Length;
            int newBehavioursLength = newBehaviours.Length;

            // Optimizations for common cases.
            if (oldBehavioursLength == 0 && newBehavioursLength == 0)
            {
                return;
            }

            if (oldBehavioursLength == 0)
            {
                newBehaviours.ForEach(AddNewBehaviour);
                return;
            }

            if (newBehavioursLength == 0)
            {
                oldBehaviours.ForEach(RemoveBehaviour);
                return;
            }

            var oldTypesSet = new HashSet<BehaviourInfo>(oldBehaviours);
            var newTypesSet = new HashSet<BehaviourInfo>(newBehaviours);

            var oldBehavioursOnly = oldTypesSet.ExceptWithAndCreateNew(newTypesSet).ToList();
            var newBehavioursOnly = newTypesSet.ExceptWithAndCreateNew(oldTypesSet).ToArray();

            foreach (BehaviourInfo newBehaviour in newBehavioursOnly)
            {
                bool foundMatching = false;

                for (int i = 0; i < oldBehavioursOnly.Count; i++)
                {
                    BehaviourInfo oldBehaviour = oldBehavioursOnly[i];

                    // Full names are equal but GUIDs are not
                    if (newBehaviour.TypeNameAndAssembly == oldBehaviour.TypeNameAndAssembly)
                    {
                        oldBehavioursOnly.Remove(oldBehaviour);

                        // new type GUID is empty -> leave the old GUID
                        if (!string.IsNullOrEmpty(newBehaviour.GUID))
                        {
                            // new type GUID is not empty -> update old GUID
                            UpdateBehaviourGUID(oldBehaviour, newBehaviour.GUID);
                        }

                        foundMatching = true;
                        break;
                    }

                    // GUIDS are equal but full names are not
                    if (newBehaviour.GUID == oldBehaviour.GUID)
                    {
                        oldBehavioursOnly.Remove(oldBehaviour); // Remove old type from collection to optimize the next searches.
                        UpdateBehaviourTypeName(oldBehaviour, newBehaviour.Type);

                        foundMatching = true;
                        break; // Go to new newTypeInfo
                    }
                }

                // There is no matching old type info, so a completely new assembly must be added for this type
                if ( ! foundMatching)
                {
                    AddNewBehaviour(newBehaviour);
                }
            }

            oldBehavioursOnly.ForEach(RemoveBehaviour);
        }

        private static void UpdateBehaviourGUID(BehaviourInfo behaviour, string newGUID)
        {
            DebugUtil.Log($"Behaviour GUID updated: {behaviour.GUID} => {newGUID}");
            BehavioursGenerationDatabase.UpdateBehaviourGUID(ref behaviour, newGUID);
        }

        private static void RemoveBehaviour(BehaviourInfo behaviour)
        {
            DebugUtil.Log($"Behaviour removed: {behaviour.TypeFullName}");
            BehavioursGenerationDatabase.RemoveGenericBehaviour(behaviour, RemoveAssembly);
        }

        private static void UpdateArgumentTypeName(ArgumentInfo argument, Type newType)
        {
            // Retrieve the array of generic arguments where the old argument was listed
            bool behavioursSuccess = BehavioursGenerationDatabase.TryGetReferencedBehaviours(argument, out BehaviourInfo[] referencedBehaviours);

            // update argument typename in database before updating assemblies and trying to find behaviour because behaviour might also need to be updated, and the argument should already be new
            BehavioursGenerationDatabase.UpdateArgumentNameAndAssembly(ref argument, newType);

            Assert.IsTrue(behavioursSuccess);

            foreach (BehaviourInfo behaviour in referencedBehaviours)
            {
                // Retrieve type of the behaviour
                // If type cannot be retrieved, try finding by GUID
                // If GUID didn't help, remove the whole collection of assemblies
                if (behaviour.RetrieveType(out Type behaviourType, out bool retrievedFromGUID))
                {
                    if (retrievedFromGUID)
                    {
                        UpdateBehaviourTypeName(behaviour, behaviourType);
                    }
                }
                else
                {
                    BehavioursGenerationDatabase.RemoveGenericBehaviour(behaviour, RemoveAssembly);
                    continue; // concrete classes are already removed, no need to iterate through them.
                }

                bool concreteClassesSuccess = BehavioursGenerationDatabase.TryGetConcreteClassesByArgument(behaviour, argument, out ConcreteClass[] concreteClasses);

                Assert.IsTrue(concreteClassesSuccess);

                foreach (ConcreteClass concreteClass in concreteClasses)
                {
                    UpdateConcreteClassAssembly(behaviourType, concreteClass);
                }
            }
        }

        private static void UpdateBehaviourTypeName(BehaviourInfo behaviour, Type newType)
        {
            DebugUtil.Log($"Behaviour typename updated: {behaviour.TypeFullName} => {newType.FullName}");

            bool success = BehavioursGenerationDatabase.TryGetConcreteClasses(behaviour, out ConcreteClass[] concreteClasses);

            Assert.IsTrue(success);

            // Update database before operating on assemblies
            BehavioursGenerationDatabase.UpdateBehaviourNameAndAssembly(ref behaviour, newType);

            // update selector assembly
            UpdateSelectorAssembly(behaviour.AssemblyGUID, newType);

            // update concrete classes assemblies
            foreach (ConcreteClass concreteClass in concreteClasses)
            {
                UpdateConcreteClassAssembly(newType, concreteClass);
            }
        }

        private static string GetSelectorAssemblyName(Type genericTypeWithoutArgs) => genericTypeWithoutArgs.FullName.MakeClassFriendly();

        private static void UpdateSelectorAssembly(string selectorAssemblyGUID, Type newType)
        {
            string newAssemblyName = GetSelectorAssemblyName(newType);

            ReplaceAssembly(selectorAssemblyGUID, newAssemblyName, () =>
            {
                CreateSelectorAssembly(newType, newAssemblyName);
            });
        }

        private static void CreateSelectorAssembly(Type genericTypeWithoutArgs, string assemblyName)
        {
            Type[] genericArgs = genericTypeWithoutArgs.GetGenericArguments();
            string componentName = "Scripts/" + CreatorUtil.GetShortNameWithBrackets(genericTypeWithoutArgs, genericArgs);
            AssemblyCreator.CreateSelectorAssembly(assemblyName, genericTypeWithoutArgs, componentName);
        }

        private static void AddNewBehaviour(BehaviourInfo behaviour)
        {
            DebugUtil.Log($"Behaviour added: {behaviour.TypeFullName}");

            Type behaviourType = behaviour.Type;
            Assert.IsNotNull(behaviourType);

            string assemblyName = GetSelectorAssemblyName(behaviourType);
            CreateSelectorAssembly(behaviourType, assemblyName);

            string assemblyPath = $"{Config.AssembliesDirPath}/{assemblyName}.dll";
            behaviour.AssemblyGUID = AssemblyGeneration.ImportAssemblyAsset(assemblyPath);
            _needsAssetDatabaseRefresh = true;

            BehavioursGenerationDatabase.AddGenericBehaviour(behaviour);
        }

        private static void RemoveAssembly(string assemblyGUID)
        {
            string assemblyPath = AssetDatabase.GUIDToAssetPath(assemblyGUID);
            string mdbPath = $"{assemblyPath}.mdb";
            AssetDatabase.DeleteAsset(assemblyPath);
            AssetDatabase.DeleteAsset(mdbPath);
            _needsAssetDatabaseRefresh = true;
        }

        private static void UpdateConcreteClassAssembly(Type behaviourType, ConcreteClass concreteClass)
        {
            if ( ! GetArgumentTypes(concreteClass, out Type[] argumentTypes))
                return;

            UpdateConcreteClassAssembly(behaviourType, argumentTypes, concreteClass);
            LogHelper.RemoveLogEntriesByMode(LogModes.EditorErrors);
            LogHelper.RemoveLogEntriesByMode(LogModes.UserAndEditorWarnings);
        }

        private static bool GetArgumentTypes(ConcreteClass concreteClass, out Type[] argumentTypes)
        {
            var arguments = concreteClass.Arguments;
            int argumentsLength = arguments.Length;

            argumentTypes = new Type[argumentsLength];

            for (int i = 0; i < argumentsLength; i++)
            {
                ArgumentInfo argument = arguments[i];

                if (argument.RetrieveType(out Type type, out bool retrievedFromGUID))
                {
                    if (retrievedFromGUID)
                    {
                        UpdateArgumentTypeName(argument, type);
                    }

                    argumentTypes[i] = type;
                }
                else
                {
                    BehavioursGenerationDatabase.RemoveArgument(argument, RemoveAssembly);

                    // Since one of the arguments was not found, the assembly associated with the concrete class
                    // already has been removed, and there is no need to try updating it.
                    return false;
                }
            }

            return true;
        }

        private static void UpdateConcreteClassAssembly(Type behaviourType, Type[] argumentTypes, ConcreteClass concreteClass)
        {
            string newAssemblyName;

            try
            {
                newAssemblyName = ConcreteClassCreator.GetConcreteClassAssemblyName(behaviourType, argumentTypes);
            }
            catch (TypeLoadException)
            {
                return;
            }

            ReplaceAssembly(concreteClass.AssemblyGUID, newAssemblyName, () =>
            {
                ConcreteClassCreator.CreateConcreteClassAssembly(behaviourType, argumentTypes, newAssemblyName);
            });
        }

        private static void ReplaceAssembly(string assemblyGUID, string newAssemblyName, Action createAssembly)
        {
            string oldAssemblyPath = AssetDatabase.GUIDToAssetPath(assemblyGUID);
            string oldAssemblyPathWithoutExtension = RemoveDLLExtension(oldAssemblyPath);
            File.Delete(oldAssemblyPath);
            File.Delete($"{oldAssemblyPathWithoutExtension}.dll.mdb");

            createAssembly();

            string newAssemblyPathWithoutExtension = $"{Config.AssembliesDirPath}/{newAssemblyName}";
            File.Move($"{oldAssemblyPathWithoutExtension}.dll.meta", $"{newAssemblyPathWithoutExtension}.dll.meta");
            File.Move($"{oldAssemblyPathWithoutExtension}.dll.mdb.meta", $"{newAssemblyPathWithoutExtension}.dll.mdb.meta");
            _needsAssetDatabaseRefresh = true;
        }

        private static string RemoveDLLExtension(string assemblyPath)
        {
            return assemblyPath.Substring(0, assemblyPath.Length - 4);
        }
    }
}