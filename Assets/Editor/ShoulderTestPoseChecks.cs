using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;

public static class ShoulderTestPoseChecks
{
    public static void Run()
    {
        var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mesh/wet_base_model_rigged.fbx"));
        var rig=new ShoulderCorrectionRig();
        float maxAngleError=0,minForward=1,maxSideError=0;
        int cases=0;
        try
        {
            var animator=root.GetComponentInChildren<Animator>();
            foreach(float yaw in new[]{0f,73f})
            {
                rig.Restore();root.transform.rotation=Quaternion.Euler(0,yaw,0);
                if(!rig.Bind(animator))throw new Exception("Humanoid binding failed");
                var chest=animator.GetBoneTransform(HumanBodyBones.UpperChest);
                if(!chest)chest=animator.GetBoneTransform(HumanBodyBones.Chest);
                foreach(float pose in new[]{0f,.25f,.5f,.75f,1f})
                {
                    rig.Restore();rig.ApplyTestPose(pose);
                    foreach(bool left in new[]{true,false})
                    {
                        var upper=animator.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
                        var lower=animator.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
                        var hand=animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
                        var direction=(lower.position-upper.position).normalized;
                        var forearm=(hand.position-lower.position).normalized;
                        var forward=Vector3.ProjectOnPlane(animator.transform.forward,direction).normalized;
                        var bend=Vector3.ProjectOnPlane(forearm,direction).normalized;
                        float angleError=Mathf.Abs(Vector3.Angle(direction,forearm)-20);
                        float forwardDot=Vector3.Dot(bend,forward);
                        float sideError=1-forwardDot;
                        maxAngleError=Mathf.Max(maxAngleError,angleError);minForward=Mathf.Min(minForward,forwardDot);maxSideError=Mathf.Max(maxSideError,sideError);cases++;
                        if(angleError>.1f || forwardDot<.999f)
                            throw new Exception("Test elbow direction: side="+(left?"left":"right")+" pose="+pose+" angleError="+angleError+" forwardDot="+forwardDot);
                    }
                }
            }
            Directory.CreateDirectory("Artifacts");
            File.WriteAllText("Artifacts/test-pose-validation.json",JsonConvert.SerializeObject(new{passed=true,cases,maxAngleError,minForward,maxSideError},Formatting.Indented));
            Debug.Log("[Portfolio] TEST ELBOW DIRECTION PASSED");
        }
        catch(Exception e){Debug.LogException(e);if(Application.isBatchMode)EditorApplication.Exit(1);throw;}
        finally{rig.Restore();UnityEngine.Object.DestroyImmediate(root);}
    }
}
