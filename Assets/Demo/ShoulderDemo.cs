using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

[DefaultExecutionOrder(500)]
public sealed class ShoulderDemo : MonoBehaviour
{
    public Animator Before, After;
    public Material GoalMaterial;
    public bool Playing = true, Correction = true;
    [Range(0,1)] public float Pose = .7f;
    public float Strength = 1, Period = 4;
    private readonly ShoulderCorrectionRig baseline = new ShoulderCorrectionRig(), corrected = new ShoulderCorrectionRig();
    private Font font;
    private GUISkin demoSkin;
    private float elapsed;
    private Transform leftGoal, rightGoal;
    private void Start()
    {
        Application.runInBackground=true;
        font=Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","Apple SD Gothic Neo","Noto Sans CJK KR","Arial"},16);
        Before.enabled=false;After.enabled=false;
        if(!baseline.Bind(Before) || !corrected.Bind(After))throw new InvalidOperationException("Humanoid mapping missing");
        baseline.Strength=0;corrected.SmoothTime=.08f;
        leftGoal=Marker("왼손 입력 목표",Color.yellow);rightGoal=Marker("오른손 입력 목표",Color.yellow);
        if(Environment.GetCommandLineArgs().Contains("-demoSmoke"))StartCoroutine(Smoke());
    }
    private Transform Marker(string name,Color color)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name=name;go.transform.localScale=Vector3.one*.035f;
        Destroy(go.GetComponent<Collider>());var m=new Material(GoalMaterial);m.color=color;go.GetComponent<Renderer>().material=m;return go.transform;
    }
    private void LateUpdate()
    {
        if(!baseline.IsBound)return;
        if(Playing){elapsed+=Time.deltaTime;Pose=.5f-.5f*Mathf.Cos(elapsed*Mathf.PI*2/Mathf.Max(.5f,Period));}
        ApplyPose(Pose);
    }
    public void ApplyPose(float pose)
    {
        baseline.Restore();corrected.Restore();
        baseline.ApplyTestPose(pose);corrected.ApplyTestPose(pose);
        if(leftGoal)leftGoal.position=After.GetBoneTransform(HumanBodyBones.LeftHand).position;
        if(rightGoal)rightGoal.position=After.GetBoneTransform(HumanBodyBones.RightHand).position;
        corrected.Strength=Correction ? Strength : 0; corrected.Apply(Application.isPlaying ? Time.deltaTime : 1f/60);
    }
    private void UseDemoSkin()
    {
        if(demoSkin)return;
        demoSkin=Instantiate(GUI.skin);demoSkin.font=font;
        demoSkin.label.font=font;demoSkin.label.fontSize=14;demoSkin.label.wordWrap=true;
    }
    private void OnGUI()
    {
        var oldSkin=GUI.skin;var old=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1200f,Screen.height/720f,1));UseDemoSkin();GUI.skin=demoSkin;
        GUI.color=new Color(.025f,.045f,.075f,.96f);GUI.DrawTexture(new Rect(16,16,320,690),Texture2D.whiteTexture);GUI.color=Color.white;
        GUILayout.BeginArea(new Rect(32,28,288,668));
        GUILayout.Label("어깨 보정 비교",new GUIStyle(GUI.skin.label){fontSize=23,fontStyle=FontStyle.Bold});
        GUILayout.Label("왼쪽: 입력 자세\n오른쪽: 동일 입력 + 보정");GUILayout.Space(16);
        GUILayout.Label("자동 재생: "+(Playing ? "ON" : "OFF")+"\n어깨 보정: "+(Correction ? "ON" : "OFF"));
        GUILayout.Label("입력 자세: "+Pose.ToString("F2")+"\n보정 강도: "+Strength.ToString("F2")+"\n재생 주기: "+Period.ToString("F1")+"초");
        GUILayout.Space(20);GUILayout.Label("왼쪽 보정량: "+corrected.LeftLift.ToString("F2")+"°\n오른쪽 보정량: "+corrected.RightLift.ToString("F2")+"°");
        GUILayout.Label("손 위치 오차: "+corrected.MaxHandError.ToString("F7"));
        GUILayout.Label(corrected.ReachLimited ? "손 목표 유지로 보정량 제한 중" : "손 목표 유지");
        GUILayout.Space(16);GUILayout.Label("노란 점: 보정 전 손 위치\n손이 점에서 벗어나는지 확인");
        GUILayout.EndArea();
        GUI.Label(new Rect(380,20,260,35),"입력 자세 · 보정 OFF",new GUIStyle(GUI.skin.label){fontSize=21});
        GUI.Label(new Rect(780,20,330,35),"같은 입력 · 보정 "+(Correction ? "ON" : "OFF"),new GUIStyle(GUI.skin.label){fontSize=21});GUI.matrix=old;GUI.skin=oldSkin;
    }
    private IEnumerator Smoke()
    {
        Directory.CreateDirectory("Artifacts");
        while(!UnityEngine.Rendering.SplashScreen.isFinished)yield return null;
        yield return new WaitForSeconds(.5f);yield return null;Playing=false;Pose=.75f;
        // Allow time-based shoulder smoothing to settle even when the Player runs at high FPS.
        yield return new WaitForSeconds(.5f);
        try {
            if(corrected.LeftLift<1 || corrected.MaxHandError>.001f)throw new Exception("Correction not visible or hand drifted: lift="+corrected.LeftLift+" handError="+corrected.MaxHandError+" reachLimited="+corrected.ReachLimited);
        }catch(Exception e){Fail(e);yield break;}
        yield return new WaitForEndOfFrame();var capture=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.GetFullPath("Artifacts/demo-preview.png"),capture.EncodeToPNG());Destroy(capture);yield return null;
        try {
            float lift=corrected.LeftLift,error=corrected.MaxHandError;
            Correction=false;ApplyPose(Pose);
            var off=After.GetBoneTransform(HumanBodyBones.LeftShoulder).localRotation;
            var reference=Before.GetBoneTransform(HumanBodyBones.LeftShoulder).localRotation;
            if(Quaternion.Angle(off,reference)>.01f)throw new Exception("OFF does not match uncorrected input");
            Strength=0;Correction=true;ApplyPose(Pose);
            if(corrected.LeftLift>.001f)throw new Exception("Strength zero not respected");
            File.WriteAllText("Artifacts/demo-smoke.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{passed=true,lift,error,offMatchesInput=true,strengthZero=true},Newtonsoft.Json.Formatting.Indented));
        }catch(Exception e){Fail(e);yield break;}
        yield return new WaitForSeconds(.5f);Application.Quit(0);
    }
    private static void Fail(Exception e){Debug.LogException(e);File.WriteAllText("Artifacts/demo-failure.txt",e.ToString());Application.Quit(1);}
    private void OnDestroy(){baseline.Restore();corrected.Restore();if(demoSkin)Destroy(demoSkin);if(font)Destroy(font);if(leftGoal){Destroy(leftGoal.GetComponent<Renderer>().sharedMaterial);Destroy(leftGoal.gameObject);}if(rightGoal){Destroy(rightGoal.GetComponent<Renderer>().sharedMaterial);Destroy(rightGoal.gameObject);}}
}
