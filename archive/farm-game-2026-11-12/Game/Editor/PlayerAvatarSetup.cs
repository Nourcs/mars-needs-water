using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MarsNeedsWater.EditorTools
{
    /// <summary>
    /// Builds the player's look from Kevin Iglesias' "Human Basic Motions FREE" pack (if it is in the project):
    /// an Animator Controller (IK pass on, a speed blend of idle, walk, run and sprint, plus a falling state) and a prefab of
    /// the male model using it, both in Assets/Game/Art/Resources/Player, where the game loads them at runtime.
    /// Runs once by itself when they are missing; "Mars Needs Water/Rebuild Player Avatar" rebuilds them.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayerAvatarSetup
    {
        const string Folder = "Assets/Game/Art/Resources/Player";
        const string ControllerPath = Folder + "/PlayerAvatar.controller";
        const string PrefabPath = Folder + "/PlayerAvatar.prefab";
        const string Pack = "Assets/Kevin Iglesias/Human Animations";

        static PlayerAvatarSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (!AssetDatabase.IsValidFolder(Pack)) return;
                if (!File.Exists(PrefabPath) || !File.Exists(ControllerPath)) { Build(); return; }
                // Older builds had no IK pass, which the first-person hands need.
                var old = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                if (old == null || old.layers.Length == 0 || !old.layers[0].iKPass) Build();
            };
        }

        [MenuItem("Mars Needs Water/Rebuild Player Avatar")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Pack)) { Debug.LogWarning("Human Basic Motions pack not found under " + Pack); return; }
            var idle = Clip("HumanM@Idle01");
            var walk = Clip("HumanM@Walk01_Forward");
            var run = Clip("HumanM@Run01_Forward");
            var sprint = Clip("HumanM@Sprint01_Forward");
            var fall = Clip("HumanM@Fall01");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "/Models/HumanM_Model.fbx");
            if (idle == null || walk == null || run == null || model == null)
            {
                Debug.LogWarning("Player avatar: some Human Basic Motions files are missing; the game stays first-person.");
                return;
            }

            Directory.CreateDirectory(Folder);
            AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            var sm = ctrl.layers[0].stateMachine;

            var move = ctrl.CreateBlendTreeInController("Move", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 1.6f);
            tree.AddChild(run, 4.2f);
            if (sprint != null) tree.AddChild(sprint, 7f);
            sm.defaultState = move;

            if (fall != null)
            {
                var air = sm.AddState("Air");
                air.motion = fall;
                var toAir = move.AddTransition(air);
                toAir.hasExitTime = false; toAir.duration = 0.2f;
                toAir.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
                var toMove = air.AddTransition(move);
                toMove.hasExitTime = false; toMove.duration = 0.15f;
                toMove.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            }
            var layers = ctrl.layers;
            layers[0].iKPass = true;           // first-person hands (HandsIK) are placed with Animator IK
            ctrl.layers = layers;
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();

            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "PlayerAvatar";
            var anim = go.GetComponent<Animator>();
            if (anim == null) anim = go.AddComponent<Animator>();
            if (anim.avatar == null) anim.avatar = AssetDatabase.LoadAllAssetsAtPath(Pack + "/Models/HumanM_Model.fbx").OfType<Avatar>().FirstOrDefault();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Player avatar built: " + PrefabPath);
        }

        /// <summary>The animation clip in the pack's FBX with exactly this file name (not the root-motion [RM] one).</summary>
        static AnimationClip Clip(string fileName)
        {
            foreach (var guid in AssetDatabase.FindAssets(fileName, new[] { Pack }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != fileName) continue;
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip != null) return clip;
            }
            Debug.LogWarning("Player avatar: clip not found: " + fileName);
            return null;
        }
    }
}
