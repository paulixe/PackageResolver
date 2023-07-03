using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditorInternal;
using UnityEngine;

namespace Historisation
{

    public static class PackageResolver
    {
        private static TextAsset packageFile;
        public static TextAsset PackageFile
        {
            get
            {
                if (packageFile == null)
                {
                    ReloadPackageFile();
                }
                return packageFile;
            }
            set { packageFile = value; }
        }

        [InitializeOnLoadMethod]
        static void AddCallback()
        {
            EditorApplication.delayCall += OnLoad;
            UnityEditor.PackageManager.Events.registeringPackages += OnRegistering;
        }
        [MenuItem("Tools/Beep")]
        public static void Beep()
        {
            EditorApplication.Beep();
        }
        [MenuItem("Tools/reload")]
        public static void ReloadPackageFile()
        {
            string folderPath = GetFolderPath();
            string packagePath = Path.Combine(folderPath, "package.json");
            PackageFile = AssetDatabase.LoadAssetAtPath<TextAsset>(packagePath);
        }

        static void OnLoad()
        {
            AddConstraints();
            AddDependencies();
            DefineConstraint();
        }
        /// <summary>
        /// <see href="https://discussions.unity.com/t/unity-2021-2-get-current-namedbuildtarget/250332/2"/>
        /// </summary>
        static void DefineConstraint()
        {
            BuildTarget buildTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
            var namedBuildTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(targetGroup);
            string scriptingDefines = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);

            if (!scriptingDefines.Contains(PackageResolverSettings.DEPENDENCIES_LOADED_CONSTRAINT_NAME))
                scriptingDefines = scriptingDefines + ";" + PackageResolverSettings.DEPENDENCIES_LOADED_CONSTRAINT_NAME;
            PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, scriptingDefines);


        }
        static void AddConstraints()
        {
            string folderPath = GetFolderPath();
            var assembliesGUID = AssetDatabase.FindAssets($"t:{nameof(AssemblyDefinitionAsset)}", new string[] { folderPath });
            var assembliesPath = assembliesGUID.Select((g) => AssetDatabase.GUIDToAssetPath(g));


            foreach (var assemblyAsset in assembliesPath)
            {
                AddConstraint(assemblyAsset);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        static void AddConstraint(string assemblyPath)
        {
            AssemblyDefinitionAsset assemblyAsset = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(assemblyPath);
            if (assemblyAsset.name == PackageResolverSettings.PACKAGE_RESOLVER_ASSEMBLY_NAME)
            {
                return;
            }

            JObject assemblyJson = JObject.Parse(assemblyAsset.text);
            if (!assemblyJson.ContainsKey(PackageResolverSettings.DEFINE_CONSTRAINTS_PROPERTY_NAME))
                assemblyJson.Add(new JProperty(PackageResolverSettings.DEFINE_CONSTRAINTS_PROPERTY_NAME, new JArray()));

            JArray defineConstraints = assemblyJson[PackageResolverSettings.DEFINE_CONSTRAINTS_PROPERTY_NAME] as JArray;
            if (!defineConstraints.Any((t) => (t as JValue).ToString() == PackageResolverSettings.DEPENDENCIES_LOADED_CONSTRAINT_NAME))
            {
                Debug.Log($"Package resolver : add constraint to assembly : {assemblyAsset.name}");
                defineConstraints.Add(PackageResolverSettings.DEPENDENCIES_LOADED_CONSTRAINT_NAME);
            }


            File.WriteAllText(assemblyPath, assemblyJson.ToString());
        }
        static void OnRegistering(PackageRegistrationEventArgs args)
        {
            foreach (var packRemoved in args.removed)
            {
                string targetedPackageName = GetPackagesName();
                string removedPackageName = packRemoved.name;
                if (targetedPackageName.Equals(removedPackageName))
                    RemoveDependencies();
            }
        }
        static string GetFolderPath()
        {
            var paths = AssetDatabase.FindAssets($"{PackageResolverSettings.PACKAGE_RESOLVER_ASSEMBLY_NAME} t:{nameof(AssemblyDefinitionAsset)}");
            if (paths.Length != 1)
                Debug.LogError($"There should be only one assembly definition named {PackageResolverSettings.PACKAGE_RESOLVER_ASSEMBLY_NAME}");

            string assemblyPath = AssetDatabase.GUIDToAssetPath(paths[0]);
            return Path.GetDirectoryName(Path.GetDirectoryName(assemblyPath));
        }
        static string GetPackagesName()
        {
            JObject packageJson = JObject.Parse(PackageFile.text);
            return packageJson["name"].ToString();
        }
        [MenuItem("Tools/Remove Dependencies")]
        public static void RemoveDependencies()
        {
            string[] gitUrl = GetGitsName();
            Client.AddAndRemove(packagesToRemove: gitUrl);
        }
        [MenuItem("Tools/Add Dependencies")]
        public static void AddDependencies()
        {
            string[] gitUrl = GetGitsUrl();
            Client.AddAndRemove(packagesToAdd: gitUrl);
        }
        private static string[] GetGitsName()
        {
            var gitDependenciesProperties = GetGitDependencies();
            return gitDependenciesProperties.Select(prop => prop.Name.ToString()).ToArray();
        }

        private static string[] GetGitsUrl()
        {
            var gitDependenciesProperties = GetGitDependencies();
            return gitDependenciesProperties.Select(prop => prop.Value.ToString()).ToArray();
        }
        private static IEnumerable<JProperty> GetGitDependencies()
        {
            JObject packageJson = JObject.Parse(PackageFile.text);
            return
                (packageJson["gitDependencies"] as JObject).Properties();
        }

    }
}