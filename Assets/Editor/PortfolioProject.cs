using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;

public static class PortfolioProject
{
    [MenuItem("Portfolio/Configure Mod Exports")]
    public static void ConfigureExports()
    {
        PortfolioExportSetup.ConfigurePaths();
    }

    [MenuItem("Portfolio/Run Validation")]
    public static void Validate()
    {
        ShoulderTestPoseChecks.Run();
        GameObject root = null;
        var rig = new ShoulderCorrectionRig();
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mesh/wet_base_model_rigged.fbx");
            if (!prefab) throw new InvalidOperationException("Test model missing");
            root = UnityEngine.Object.Instantiate(prefab);
            var animator = root.GetComponentInChildren<Animator>();
            if (!rig.Bind(animator)) throw new InvalidOperationException("Humanoid shoulder/arm mapping missing");
            var left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var shoulder = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            float maxError = 0, maxRestore = 0;
            for (int pose = 0; pose < 5; pose++)
            {
                rig.Restore(); rig.ApplyTestPose(pose / 4f);
                var lp = left.position; var rp = right.position;
                rig.Apply(1f / 60);
                maxError = Mathf.Max(maxError, Vector3.Distance(lp, left.position), Vector3.Distance(rp, right.position));
                if (maxError > .001f) throw new InvalidOperationException("Hand position changed: " + maxError);
            }
            rig.Restore();
            var baseline = shoulder.localRotation;
            for (int frame = 0; frame < 500; frame++)
            {
                rig.ApplyTestPose(.75f); rig.Apply(1f / 60); rig.Restore();
                maxRestore = Mathf.Max(maxRestore, Quaternion.Angle(baseline, shoulder.localRotation));
            }
            if (maxRestore > .01f) throw new InvalidOperationException("Accumulated shoulder rotation: " + maxRestore);
            rig.Strength = 0; rig.ApplyTestPose(.75f);
            var before = shoulder.localRotation; rig.Apply(1f / 60);
            float zeroStrengthAngle = Quaternion.Angle(before, shoulder.localRotation);
            if (zeroStrengthAngle > .01f) throw new InvalidOperationException("Strength zero changes shoulder");
            Directory.CreateDirectory("Artifacts");
            File.WriteAllText("Artifacts/validation.json", JsonConvert.SerializeObject(new { passed = true, poses = 5, restoreFrames = 500, maxHandError = maxError, maxRestoreAngle = maxRestore, zeroStrengthAngle }, Formatting.Indented));
            Debug.Log("[Portfolio] VALIDATION PASSED");
        }
        catch (Exception e)
        {
            Directory.CreateDirectory("Artifacts");
            File.WriteAllText("Artifacts/validation.json", JsonConvert.SerializeObject(new { failed = true, error = e.ToString() }, Formatting.Indented));
            Debug.LogException(e);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            throw;
        }
        finally
        {
            rig.Restore();
            if (root) UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [MenuItem("Portfolio/Build All Mods")]
    public static void BuildAll()
    {
        ConfigureExports();
        var initial = AssetDatabase.LoadAssetAtPath<UMod.ModTools.Export.ExportSettings>("Assets/ExportSettings.asset");
        var initialState = new SerializedObject(initial);
        int selected = initialState.FindProperty("activeProfile").intValue;
        int count = initialState.FindProperty("exportProfiles").arraySize;
        try
        {
            for (int i = 0; i < count; i++)
            {
                // The SDK releases temporary assemblies and settings after each build.
                var settings = AssetDatabase.LoadAssetAtPath<UMod.ModTools.Export.ExportSettings>("Assets/ExportSettings.asset");
                if (settings == null) throw new InvalidOperationException("Export settings unavailable after build");
                var so = new SerializedObject(settings);
                so.FindProperty("activeProfile").intValue = i;
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(settings);
                var result = UMod.BuildEngine.ModToolsUtil.StartBuild(settings);
                if (result == null || !result.Successful) throw new InvalidOperationException("Mod build failed for profile " + i);
                Debug.Log("[Portfolio] MOD BUILD PASSED " + i);
            }
        }
        finally
        {
            var settings = AssetDatabase.LoadAssetAtPath<UMod.ModTools.Export.ExportSettings>("Assets/ExportSettings.asset");
            if (settings != null)
            {
                var so = new SerializedObject(settings);
                so.FindProperty("activeProfile").intValue = selected;
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(settings);
            }
        }
    }
}
