// SoldierRigBuilder.cs -- the rigged soldier's prefabs and Animator, from SF_TROOPER_RIG.fbx
// (Tools/blender/build_soldier_rig.py). Run after the model is re-exported, and after Build
// step 1 (it takes the palette's materials). The prefabs sit in Resources until a unit uses them.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace StarForge.EditorTools
{
    public static class SoldierRigBuilder
    {
        const string Model = "TROOPER_RIG";
        const string Dir = "Assets/StarForge/Resources";
        const string ControllerPath = Dir + "/SoldierRig.controller";

        [MenuItem("StarForge/Build/2b Soldier Rig", priority = 3)]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var clips = new List<AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath($"{SFAssetPostprocessor.ModelDir}SF_{Model}.fbx"))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) clips.Add(c);
            if (clips.Count == 0) return $"SF_{Model}.fbx has no clips: has it been imported?";

            if (File.Exists(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var c in clips)
            {
                var st = sm.AddState(c.name);
                st.motion = c;
                if (c.name == "Idle") sm.defaultState = st;
            }

            string info = "";
            for (int team = 0; team < 2; team++)
            {
                var go = ModelFactory.Create(Model, team);
                // The writer's axes with no space baking bring the model in facing -Z (as
                // Tools/blender/export_fbx.py found of its own first write): turned here.
                var root = new GameObject("SoldierRig");
                go.transform.SetParent(root.transform, false);
                go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var anim = go.GetComponentInChildren<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullCompletely;
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                smr.quality = SkinQuality.Bone1;
                smr.skinnedMotionVectors = false;
                smr.updateWhenOffscreen = false;
                // The clips reach further than the T the mesh was bound in (a crawl lies forward),
                // and the import leaves the mesh's own axes Z up: a box that holds him either way.
                smr.localBounds = new Bounds(Vector3.zero, Vector3.one * 5f);
                PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/SoldierRig_T{team}.prefab");
                info = $"{smr.sharedMesh.vertexCount} vertices, {smr.sharedMesh.subMeshCount} submeshes, {smr.bones.Length} bones exposed, avatar {(anim.avatar != null && anim.avatar.isValid)}, mesh bounds {smr.sharedMesh.bounds.size}";
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            return $"SoldierRig: {clips.Count} clips ({string.Join(", ", clips.ConvertAll(c => $"{c.name} {c.length:0.00}s{(c.isLooping ? " loop" : "")}"))}); {info}";
        }
    }
}
