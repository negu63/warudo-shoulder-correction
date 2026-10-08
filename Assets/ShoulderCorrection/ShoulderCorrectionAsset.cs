using UnityEngine;
using System.Collections.Generic;
using RootMotion.FinalIK;
using Warudo.Core.Attributes;
using Warudo.Core.Scenes;
using Warudo.Plugins.Core.Assets.Character;

[AssetType(Id="surfacewetness.shoulder-correction.asset", Title="자동 어깨 보정")]
public class ShoulderCorrectionAsset : Asset
{
    [DataInput, Label("대상 캐릭터")] public CharacterAsset Character;
    [DataInput, Label("보정 사용")] public bool CorrectionEnabled=true;
    [DataInput, Label("보정 강도"), FloatSlider(0,1)] public float Strength=1;
    [DataInput, Label("보정 시작 각도"), FloatSlider(-80,60)] public float StartElevation=-30;
    [DataInput, Label("최대 어깨 들림"), FloatSlider(0,45)] public float MaxLift=25;
    [DataInput, Label("앞으로 뻗기 보정"), FloatSlider(0,30)] public float MaxForward=15;
    [DataInput, Label("움직임 부드러움 (초)"), FloatSlider(0,.5f)] public float SmoothTime=.08f;
    [DataInput, Label("팔 올리기 테스트 (모캡 없이)")] public bool TestMotion;
    [DataInput, Label("테스트 재생")] public bool PlayTest=true;
    [DataInput, Label("테스트 고정 자세"), FloatSlider(0,1)] public float TestPose;
    [DataInput, Label("테스트 주기 (초)"), FloatSlider(1,10)] public float TestPeriod=4;
    [DataInput, Label("상태"), Disabled] public string Status="캐릭터를 선택하세요";
    private static readonly Dictionary<Animator,ShoulderCorrectionAsset> owners=new Dictionary<Animator,ShoulderCorrectionAsset>();
    private readonly ShoulderCorrectionRig rig=new ShoulderCorrectionRig();
    private Animator animator;
    private FullBodyBipedIK ik;
    private ShoulderRotator builtin;
    private bool builtinEnabled;
    private int appliedFrame=-1;
    private float elapsed;
    private float nextStatusTime;

    protected override void OnCreate() { base.OnCreate(); SetActive(true); }

    public override void OnPreUpdate()
    {
        base.OnPreUpdate();
        rig.Restore();
        EnsureBinding();
    }

    public override void OnUpdate() { base.OnUpdate(); EnsureBinding(); }

    private void EnsureBinding()
    {
        var next=Character!=null ? Character.Animator : null;
        var nextIK=Character!=null ? Character.FullBodyBipedIK : null;
        if(next!=animator || nextIK!=ik)
        {
            Unbind();
            ShoulderCorrectionAsset owner;
            if(next!=null && owners.TryGetValue(next,out owner) && owner!=this)
            { SetStatus("이 캐릭터에는 다른 어깨 보정 에셋이 연결돼 있습니다"); return; }
            animator=next;
            if(!animator) { SetStatus("캐릭터를 선택하세요"); return; }
            if(!rig.Bind(animator)) { SetStatus("쇄골과 양팔이 매핑된 Humanoid 캐릭터가 필요합니다"); return; }
            owners[animator]=this;
            ik=nextIK;
            if(ik!=null)ik.solver.OnPreRead+=BeforeIK;
            builtin=Character.ShoulderRotator;
            if(builtin!=null)builtinEnabled=builtin.enabled;
            Debug.Log("[ShoulderCorrection] Bound "+Character.Name+"; IK pre-read="+(ik!=null));
        }
        if(builtin!=null)builtin.enabled=CorrectionEnabled ? false : builtinEnabled;
    }

    private void BeforeIK() { Apply(true); }

    public override void OnPostLateUpdate()
    {
        base.OnPostLateUpdate();
        // Bypassed/disabled IK and animation-only characters still get assistance.
        if(appliedFrame!=Time.frameCount)Apply(false);
    }

    private void Apply(bool beforeIK)
    {
        if(!rig.IsBound || appliedFrame==Time.frameCount)return;
        appliedFrame=Time.frameCount;
        if(Character!=null && Character.RagdollEnabled) { SetStatus("래그돌 중에는 보정을 쉽니다"); return; }
        if(TestMotion)
        {
            if(PlayTest)elapsed+=Time.deltaTime;
            rig.ApplyTestPose(PlayTest ? .5f-.5f*Mathf.Cos(elapsed*2*Mathf.PI/Mathf.Max(1,TestPeriod)) : TestPose);
        }
        if(!CorrectionEnabled) { SetStatus("보정 꺼짐"); return; }
        rig.Strength=Strength;rig.StartElevation=StartElevation;rig.MaxLift=MaxLift;
        rig.MaxForward=MaxForward;rig.SmoothTime=SmoothTime;
        Vector3? left=null,right=null;
        if(beforeIK && ik!=null && !TestMotion)
        {
            var solver=ik.solver;
            float w=solver.IKPositionWeight;
            var le=solver.leftHandEffector;var re=solver.rightHandEffector;
            if(le.positionWeight*w>0)left=Vector3.Lerp(animator.GetBoneTransform(HumanBodyBones.LeftHand).position,
                le.target!=null ? le.target.position : le.position,Mathf.Clamp01(le.positionWeight*w));
            if(re.positionWeight*w>0)right=Vector3.Lerp(animator.GetBoneTransform(HumanBodyBones.RightHand).position,
                re.target!=null ? re.target.position : re.position,Mathf.Clamp01(re.positionWeight*w));
        }
        rig.Apply(Time.deltaTime,left,right);
        if(Time.unscaledTime>=nextStatusTime)
        {
            nextStatusTime=Time.unscaledTime+.5f;
            SetStatus((beforeIK ? "손 IK 연동" : "애니메이션 보정")+" · 왼쪽 "+rig.LeftLift.ToString("F1")+
                "° / 오른쪽 "+rig.RightLift.ToString("F1")+"°"+(rig.ReachLimited ? " · 팔 길이에 맞춰 보정 제한" : ""));
        }
    }

    private void SetStatus(string value)
    {
        if(Status==value)return;
        Status=value;
        BroadcastDataInput(nameof(Status));
    }

    private void Unbind()
    {
        rig.Restore();
        if(ik!=null)ik.solver.OnPreRead-=BeforeIK;
        if(builtin!=null)builtin.enabled=builtinEnabled;
        if(animator!=null && owners.ContainsKey(animator) && owners[animator]==this)owners.Remove(animator);
        animator=null;ik=null;builtin=null;
        rig.Bind(null);
        appliedFrame=-1;
    }

    protected override void OnDestroy() { Unbind(); base.OnDestroy(); }
}
