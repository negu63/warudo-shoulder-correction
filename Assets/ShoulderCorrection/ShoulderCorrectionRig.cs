using UnityEngine;

// Bone-axis independent: calibration uses the avatar's chest frame and clavicle direction.
// This class does not run its own update loop; the Warudo asset supplies the input pose.
public sealed class ShoulderCorrectionRig
{
    public float Strength = 1, StartElevation = -30, MaxLift = 25, MaxForward = 15, SmoothTime = .08f;
    public float LeftLift, RightLift, MaxHandError;
    public bool ReachLimited;
    public bool IsBound { get { return chest != null && left != null && right != null; } }
    private Transform chest;
    private Vector3 localUp, localForward;
    private Arm left, right;

    private sealed class Arm
    {
        public Transform Shoulder, Upper, Lower, Hand;
        public Vector3 RestDirection;
        public Quaternion TestUpper, TestLower;
        public float Lift, Forward;
        public Transform[] Nodes;
        public Quaternion[] Before = new Quaternion[4], After = new Quaternion[4];
        public bool Applied;
        public bool TestApplied;
        public Quaternion TestBeforeUpper, TestBeforeLower, TestAfterUpper, TestAfterLower;
    }

    public bool Bind(Animator animator)
    {
        Restore();
        left = right = null;
        chest = null;
        if (animator == null || !animator.isHuman) return false;
        chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (!chest) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        if (!chest) chest = animator.GetBoneTransform(HumanBodyBones.Spine);
        if (!chest) return false;
        localUp = chest.InverseTransformDirection(animator.transform.up);
        localForward = chest.InverseTransformDirection(animator.transform.forward);
        left = MakeArm(animator, true);
        right = MakeArm(animator, false);
        return IsBound;
    }

    private Arm MakeArm(Animator a, bool isLeft)
    {
        var s = a.GetBoneTransform(isLeft ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder);
        var u = a.GetBoneTransform(isLeft ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var l = a.GetBoneTransform(isLeft ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var h = a.GetBoneTransform(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        if (!s || !u || !l || !h || !u.IsChildOf(s) || (u.position-s.position).sqrMagnitude < 1e-8f) return null;
        return new Arm { Shoulder=s, Upper=u, Lower=l, Hand=h,
            RestDirection=chest.InverseTransformDirection((u.position-s.position).normalized),
            TestUpper=u.localRotation, TestLower=l.localRotation, Nodes=new[] { s,u,l,h } };
    }

    // Remove our previous result only if no other pose writer has replaced that bone.
    public void Restore()
    {
        RestoreArm(left);
        RestoreArm(right);
    }

    private static void RestoreArm(Arm a)
    {
        if (a == null) return;
        if(a.Applied) for (int i=0;i<4;i++)
            if (a.Nodes[i] && Quaternion.Angle(a.Nodes[i].localRotation,a.After[i]) < .001f)
                a.Nodes[i].localRotation = a.Before[i];
        a.Applied = false;
        if(a.TestApplied)
        {
            if(a.Upper && Quaternion.Angle(a.Upper.localRotation,a.TestAfterUpper)<.001f)a.Upper.localRotation=a.TestBeforeUpper;
            if(a.Lower && Quaternion.Angle(a.Lower.localRotation,a.TestAfterLower)<.001f)a.Lower.localRotation=a.TestBeforeLower;
            a.TestApplied=false;
        }
    }

    public void Apply(float dt, Vector3? leftTarget = null, Vector3? rightTarget = null)
    {
        if (!IsBound) return;
        MaxHandError=0;
        ReachLimited=false;
        LeftLift=ApplyArm(left,dt,leftTarget);
        RightLift=ApplyArm(right,dt,rightTarget);
    }

    private float ApplyArm(Arm a, float dt, Vector3? target)
    {
        var up=chest.TransformDirection(localUp).normalized;
        var forward=Vector3.ProjectOnPlane(chest.TransformDirection(localForward),up).normalized;
        var rest=chest.TransformDirection(a.RestDirection).normalized;
        var outward=Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(rest,up),forward).normalized;
        var armDirection=((target.HasValue ? target.Value : a.Lower.position)-a.Upper.position).normalized;
        float elevation=Mathf.Asin(Mathf.Clamp(Vector3.Dot(armDirection,up),-1,1))*Mathf.Rad2Deg;
        float amount=Mathf.Clamp01((elevation-Mathf.Clamp(StartElevation,-80,60))/(90-Mathf.Clamp(StartElevation,-80,60)));
        amount=amount*amount*(3-2*amount);
        float lift=amount*Mathf.Clamp(MaxLift,0,45)*Mathf.Clamp01(Strength);
        float reach=Mathf.Max(0,Vector3.Dot(armDirection,forward))*Mathf.Clamp(MaxForward,0,30)*Mathf.Clamp01(Strength);
        float alpha=SmoothTime<=0 ? 1 : 1-Mathf.Exp(-Mathf.Max(0,dt)/SmoothTime);
        a.Lift=Mathf.Lerp(a.Lift,lift,alpha);
        a.Forward=Mathf.Lerp(a.Forward,reach,alpha);
        if (Strength<=0) { a.Lift=a.Forward=0; return 0; }
        var current=(a.Upper.position-a.Shoulder.position).normalized;
        float restLift=Mathf.Asin(Mathf.Clamp(Vector3.Dot(rest,up),-1,1))*Mathf.Rad2Deg;
        float currentLift=Mathf.Asin(Mathf.Clamp(Vector3.Dot(current,up),-1,1))*Mathf.Rad2Deg;
        float restForward=Mathf.Atan2(Vector3.Dot(rest,forward),Vector3.Dot(rest,outward))*Mathf.Rad2Deg;
        float currentForward=Mathf.Atan2(Vector3.Dot(current,forward),Vector3.Dot(current,outward))*Mathf.Rad2Deg;
        // Existing tracked/animated shoulder motion counts toward the requested assistance.
        float desiredLift=a.Lift>.0001f ? Mathf.Max(currentLift,restLift+a.Lift) : currentLift;
        float desiredForward=currentForward+(a.Forward>.0001f ? Mathf.Max(0,Mathf.DeltaAngle(currentForward,restForward+a.Forward)) : 0);
        float yaw=desiredForward*Mathf.Deg2Rad, pitch=desiredLift*Mathf.Deg2Rad;
        var desired=(outward*Mathf.Cos(yaw)+forward*Mathf.Sin(yaw))*Mathf.Cos(pitch)+up*Mathf.Sin(pitch);
        if (Vector3.Angle(current,desired)<.001f) return 0;
        for (int i=0;i<4;i++) a.Before[i]=a.Nodes[i].localRotation;
        var wrist=a.Hand.position;
        var wristRotation=a.Hand.rotation;
        var elbow=a.Lower.position;
        float upperLength=Vector3.Distance(a.Upper.position,elbow);
        float lowerLength=Vector3.Distance(elbow,wrist);
        var swing=Quaternion.FromToRotation(current,desired);
        var pivot=a.Shoulder.position;
        var offset=a.Upper.position-pivot;
        float minimum=Mathf.Abs(upperLength-lowerLength), maximum=upperLength+lowerLength;
        float movedDistance=Vector3.Distance(wrist,pivot+swing*offset);
        if(movedDistance>maximum || movedDistance<minimum)
        {
            // Do not stretch the arm or displace the tracked hand to force shoulder assistance.
            float lo=0,hi=1;
            for(int i=0;i<16;i++)
            {
                float mid=(lo+hi)*.5f;
                float d=Vector3.Distance(wrist,pivot+Quaternion.Slerp(Quaternion.identity,swing,mid)*offset);
                if(d<=maximum && d>=minimum)lo=mid;else hi=mid;
            }
            swing=Quaternion.Slerp(Quaternion.identity,swing,lo);
            ReachLimited=true;
        }
        a.Shoulder.rotation=swing*a.Shoulder.rotation;
        PreserveHand(a,wrist,wristRotation,elbow,upperLength,lowerLength,up);
        MaxHandError=Mathf.Max(MaxHandError,Vector3.Distance(a.Hand.position,wrist));
        for(int i=0;i<4;i++) a.After[i]=a.Nodes[i].localRotation;
        a.Applied=true;
        return Quaternion.Angle(Quaternion.identity,swing);
    }

    // Analytic two-bone solve keeps FK hands in place; the host IK then solves its own targets.
    private static void PreserveHand(Arm a, Vector3 goal, Quaternion handRotation, Vector3 oldElbow,
        float upperLength, float lowerLength, Vector3 up)
    {
        var start=a.Upper.position;
        var delta=goal-start;
        float distance=delta.magnitude;
        if (upperLength<1e-5f || lowerLength<1e-5f) return;
        var axis=distance>1e-6f ? delta/distance : (a.Hand.position-start).normalized;
        if(axis.sqrMagnitude<.5f)axis=up;
        var pole=Vector3.ProjectOnPlane(oldElbow-start,axis);
        if(pole.sqrMagnitude<1e-10f)pole=Vector3.ProjectOnPlane(a.Lower.position-start,axis);
        if(pole.sqrMagnitude<1e-10f)pole=Vector3.Cross(axis,up);
        if(pole.sqrMagnitude<1e-10f)pole=Vector3.Cross(axis,Vector3.right);
        pole.Normalize();
        float d=Mathf.Clamp(distance,Mathf.Abs(upperLength-lowerLength)+1e-6f,upperLength+lowerLength-1e-6f);
        float along=(upperLength*upperLength-lowerLength*lowerLength+d*d)/(2*d);
        float height=Mathf.Sqrt(Mathf.Max(0,upperLength*upperLength-along*along));
        var desiredElbow=start+axis*along+pole*height;
        a.Upper.rotation=Quaternion.FromToRotation(a.Lower.position-start,desiredElbow-start)*a.Upper.rotation;
        var reachableGoal=start+axis*d;
        a.Lower.rotation=Quaternion.FromToRotation(a.Hand.position-a.Lower.position,reachableGoal-a.Lower.position)*a.Lower.rotation;
        a.Hand.rotation=handRotation;
    }

    // Optional input generator, deliberately separate from correction and disabled by default.
    public void ApplyTestPose(float phase)
    {
        if(!IsBound)return;
        TestArm(left,phase);
        TestArm(right,phase);
    }

    private void TestArm(Arm a,float phase)
    {
        a.TestBeforeUpper=a.Upper.localRotation;
        a.TestBeforeLower=a.Lower.localRotation;
        a.Upper.localRotation=a.TestUpper;
        a.Lower.localRotation=a.TestLower;
        var up=chest.TransformDirection(localUp).normalized;
        var outward=Vector3.ProjectOnPlane(chest.TransformDirection(a.RestDirection),up).normalized;
        float angle=Mathf.Lerp(-75,80,Mathf.Clamp01(phase))*Mathf.Deg2Rad;
        var direction=outward*Mathf.Cos(angle)+up*Mathf.Sin(angle);
        a.Upper.rotation=Quaternion.FromToRotation(a.Lower.position-a.Upper.position,direction)*a.Upper.rotation;
        // Bend toward the chest's forward direction, independent of each bone's local axes.
        var forward=Vector3.ProjectOnPlane(chest.TransformDirection(localForward),direction).normalized;
        float bend=20*Mathf.Deg2Rad;
        var forearmDirection=direction*Mathf.Cos(bend)+forward*Mathf.Sin(bend);
        a.Lower.rotation=Quaternion.FromToRotation(a.Hand.position-a.Lower.position,forearmDirection)*a.Lower.rotation;
        a.TestAfterUpper=a.Upper.localRotation;
        a.TestAfterLower=a.Lower.localRotation;
        a.TestApplied=true;
    }
}
