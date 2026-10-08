using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DemoSceneBuilder
{
    public const string ScenePath = "Assets/Demo/ShoulderDemo.unity";
    [MenuItem("Portfolio/Create Interactive Demo Scene")]
    public static void Create()
    {
        Directory.CreateDirectory("Assets/Demo");
        Directory.CreateDirectory("Artifacts");
        if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        try {
            var cameraObject=new GameObject("Demo Camera");var camera=cameraObject.AddComponent<Camera>();cameraObject.tag="MainCamera";
            cameraObject.AddComponent<AudioListener>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.16f,.22f);camera.nearClipPlane=.05f;camera.farClipPlane=30;
            var sun=new GameObject("Key Light").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.25f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(35,-35,0);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.55f,.65f);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Demo Floor";floor.transform.localScale=Vector3.one*.6f;
            var floorMat=DemoMaterial("Assets/Demo/Floor.mat",new Color(.22f,.27f,.32f));floor.GetComponent<Renderer>().sharedMaterial=floorMat;
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mesh/wet_base_model_rigged.fbx");
            var before=(GameObject)PrefabUtility.InstantiatePrefab(source);before.name="Input Pose";before.transform.position=new Vector3(-.5f,0,0);
            var after=(GameObject)PrefabUtility.InstantiatePrefab(source);after.name="Corrected Pose";after.transform.position=new Vector3(1.2f,0,0);
            var modelMat=DemoMaterial("Assets/Demo/Character.mat",new Color(.5f,.67f,.78f));modelMat.SetFloat("_Glossiness",.35f);
            foreach(var root in new[]{before,after})foreach(var r in root.GetComponentsInChildren<Renderer>()){var mats=r.sharedMaterials;for(int i=0;i<mats.Length;i++)mats[i]=modelMat;r.sharedMaterials=mats;}
            var controller=new GameObject("Shoulder Demo").AddComponent<ShoulderDemo>();controller.Before=before.GetComponentInChildren<Animator>();controller.After=after.GetComponentInChildren<Animator>();
            var goalMat=AssetDatabase.LoadAssetAtPath<Material>("Assets/Demo/HandGoal.mat");if(!goalMat){goalMat=new Material(Shader.Find("Unlit/Color"));AssetDatabase.CreateAsset(goalMat,"Assets/Demo/HandGoal.mat");}goalMat.color=Color.yellow;controller.GoalMaterial=goalMat;
            camera.orthographic=true;camera.orthographicSize=1.55f;camera.transform.position=new Vector3(-.35f,1,-5);camera.transform.LookAt(new Vector3(-.35f,1,0));
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            PlayerSettings.defaultScreenWidth=1200;PlayerSettings.defaultScreenHeight=720;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            AssetDatabase.SaveAssets();
            File.WriteAllText("Artifacts/demo-created.txt", "PASSED");
        }
        finally { if(!Application.isBatchMode && scene.IsValid() && scene.isLoaded)SceneManager.SetActiveScene(scene); }
        AssetDatabase.Refresh();
    }
    private static Material DemoMaterial(string path, Color color)
    {
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(material,path);}
        material.color=color;EditorUtility.SetDirty(material);return material;
    }
    [MenuItem("Portfolio/Open Interactive Demo Scene")]
    public static void Open() { if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(ScenePath); }
    [MenuItem("Portfolio/Build Interactive Demo")]
    public static void Build()
    {
        if(!File.Exists(ScenePath))Create();
        Directory.CreateDirectory("Artifacts/InteractiveDemo");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{ScenePath},locationPathName=Path.GetFullPath("Artifacts/InteractiveDemo/ShoulderDemo.exe"),target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development });
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Demo build failed: "+report.summary.result);
        File.WriteAllText("Artifacts/demo-build.txt","PASSED");
    }
}
