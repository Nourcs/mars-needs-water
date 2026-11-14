using System.IO;
using MarsNeedsWater.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarsNeedsWater.EditorTools
{
    /// <summary>
    /// Creates Assets/Game/Scenes/Greybox.unity (an empty scene with a camera and light; GameRoot builds the
    /// level at runtime) the first time the project opens, adds it to the build list, and offers a menu item.
    /// </summary>
    [InitializeOnLoad]
    public static class GreyboxSceneSetup
    {
        public const string ScenePath = "Assets/Game/Scenes/Greybox.unity";

        static GreyboxSceneSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (!File.Exists(ScenePath)) CreateScene(openAfter: false);
            };
        }

        [MenuItem("Mars Needs Water/Open Greybox Scene")]
        public static void Open()
        {
            if (!File.Exists(ScenePath)) CreateScene(openAfter: true);
            else if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        static void CreateScene(bool openAfter)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == ScenePath))
            {
                list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = list.ToArray();
            }
            AssetDatabase.Refresh();
            Debug.Log($"Mars Needs Water: created {ScenePath}. Open it and press Play ({GameBootstrap.SceneName}).");
            if (openAfter) EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
