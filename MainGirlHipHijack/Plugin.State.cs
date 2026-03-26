using System.Reflection;
using MainGameTransformGizmo;
using RootMotion.FinalIK;
using UnityEngine;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        private sealed class BIKEffectorState
        {
            public bool Running;
            public bool IsBendGoal;
            public FullBodyBipedChain BendChain;
            public bool HasOriginalEffectorTarget;
            public Transform OriginalEffectorTarget;
            public bool HasOriginalBendGoal;
            public Transform OriginalBendGoal;
            public GameObject FallbackEffectorTargetGo;
            public GameObject FallbackBendGoalGo;
            public GameObject ProxyGo;
            public Transform Proxy;
            public TransformGizmo Gizmo;
            public System.Action<bool> GizmoDragHandler;
            public System.Action<GizmoMode> GizmoModeHandler;
            public bool GizmoDragging;
            public float BendGoalRadius;
            public Transform FollowBone;
            public Vector3 FollowBonePositionOffset;
            public Quaternion FollowBoneRotationOffset;
            public Transform CandidateBone;
            public GameObject BoneMarkerGo;
            public LineRenderer FollowLine;
            public Quaternion GrabStartCtrlRot;
            public Quaternion GrabStartProxyRot;
            public GameObject VRMarkerGo;
            public Renderer   VRMarkerRend;
            public bool HasPostDragHold;
            public int PostDragHoldFrames;
            public Vector3 PostDragHoldPos;
            public Quaternion PostDragHoldRot;
            // LateUpdate(IK適用後)の骨位置キャッシュ — コルーチン(Update相)から参照用
            public bool HasLateUpdateBoneCache;
            public Vector3 LateUpdateBonePos;
            public Quaternion LateUpdateBoneRot;
            // コルーチンからLateUpdateへ遅延する追従リバインド
            public bool PendingFollowRebind;
            public Transform PendingFollowBone;
            public bool PendingFollowHasPresetOffset;
            public Vector3 PendingFollowPosOffset;
            public Quaternion PendingFollowRotOffset;
            // 距離閾値ブレンドウェイト（1=完全追従, 0=切断）
            public float FollowDistanceWeight = 1f;
        }

        private sealed class RuntimeState
        {
            public HSceneProc HSceneProc;
            // 速さハイジャック追跡
            public Vector3 SpeedLastPos;
            public bool    SpeedHasLastPos;
            public float   SpeedLastMoveTime;
            public bool    SpeedIsMoving;
            public bool    SpeedBootstrapSent;
            public bool    InsertBootstrapSent;
            // 腰リンク追従
            public Vector3    BodyCtrlBaseProxyPos;
            public Quaternion BodyCtrlBaseProxyRot;
            public Vector3    BodyCtrlBaseCtrlPos;
            public Quaternion BodyCtrlBaseCtrlRot;
            public Vector3    BodyCtrlSmoothVelocity;
            public ChaControl TargetFemaleCha;
            public Animator AnimBodyCached;
            public bool HasAnimatorStateHashCached;
            public int AnimatorStateHashCached;
            public bool HasMotionStrengthCached;
            public string MotionStrengthTagCached;
            public FullBodyBipedIK Fbbik;
            public bool AutoEnabledInCurrentH;
            public bool HasNowAnimationInfoCached;
            public int NowAnimationInfoIdCached;
            public int NowAnimationInfoModeCached;
            public string NowAnimationInfoNameCached;
            public bool HasSelectAnimationInfoCached;
            public int SelectAnimationInfoIdCached;
            public int SelectAnimationInfoModeCached;
            public string SelectAnimationInfoNameCached;
            public Transform[] BoneCache;
            public GameObject FollowHmdTargetGo;
            public Transform FollowHmdTarget;
            // 男キャラ HMD 追従
            public ChaControl TargetMaleCha;
            public Transform[] MaleBoneCache;
            public Transform MaleHeadBone;
            public string MaleHeadBoneName;
            public Transform MaleNeckBone;
            public Transform MaleWaistBone;
            public Transform MaleSpine1Bone;
            public Transform MaleSpine2Bone;
            public Transform MaleSpineBone;
            public Transform MaleLeftHandBone;
            public Transform MaleRightHandBone;
            public Transform MaleLeftFootBone;
            public Transform MaleRightFootBone;
            public Transform MaleLeftShoulderBone;
            public Transform MaleRightShoulderBone;
            public Transform MaleLeftUpperArmBone;
            public Transform MaleRightUpperArmBone;
            public Transform MaleLeftUpperLegBone;
            public Transform MaleRightUpperLegBone;
            public Transform MaleLeftLowerArmBone;
            public Transform MaleRightLowerArmBone;
            public Transform MaleLeftLowerLegBone;
            public Transform MaleRightLowerLegBone;
            public Transform MaleHipsBone;
            public FullBodyBipedIK MaleFbbik;
            public bool MaleFbbikInjectedByPlugin;
            public GameObject MaleSyntheticIkTargetRootGo;
            public Transform[] MaleSyntheticIkTargets = new Transform[BIK_TOTAL];
            public Transform MaleIkHipsTarget;
            public Transform MalePvHandL;
            public Transform MalePvHandR;
            public Transform MalePvElboL;
            public Transform MalePvElboR;
            public Transform MalePvLegL;
            public Transform MalePvLegR;
            public Transform MalePvKneeL;
            public Transform MalePvKneeR;
            public Transform MalePvShoulderL;
            public Transform MalePvShoulderR;
            public Transform MalePvWaistL;
            public Transform MalePvWaistR;
            public Transform MaleLeftHandFollowBone;
            public Transform MaleRightHandFollowBone;
            public Vector3 MaleLeftHandFollowPosOffset;
            public Vector3 MaleRightHandFollowPosOffset;
            public Quaternion MaleLeftHandFollowRotOffset;
            public Quaternion MaleRightHandFollowRotOffset;
            public bool HasMaleHmdBaseline;
            public Vector3 MaleHmdBaseHMDPos;
            public Vector3 MaleHmdBaseHeadPosOffset;
            public Quaternion MaleHmdBaseHeadRotOffset;
            public Vector3 MaleHmdBaseSpinePos;
            public Vector3 MaleHmdBaseBodyPos;
            public Vector3 MaleHmdBaseBodyPosLocal;
            public bool HasMaleNeckShoulderBaseline;
            public Vector3 MaleLeftShoulderFromNeckPosLocal;
            public Vector3 MaleRightShoulderFromNeckPosLocal;
            public Quaternion MaleLeftShoulderFromNeckRotLocal;
            public Quaternion MaleRightShoulderFromNeckRotLocal;
            public bool HasMaleNeckShoulderPrevPose;
            public Vector3 MaleNeckShoulderPrevPos;
            public Quaternion MaleNeckShoulderPrevRot;
            public bool HasMaleNeckFromHeadBaseline;
            public Vector3 MaleNeckFromHeadPosLocal;
            public Quaternion MaleNeckFromHeadRotLocal;
            public bool HasMaleHmdLocalDelta;
            public Vector3 MaleHmdLocalDeltaSmoothed;
            public int MaleHeadBoneSelectionCached;
            public GameObject MaleHeadTargetGo;
            public Transform MaleHeadTarget;
            public TransformGizmo MaleHeadTargetGizmo;
            public System.Action<bool> MaleHeadTargetGizmoDragHandler;
            public System.Action<GizmoMode> MaleHeadTargetGizmoModeHandler;
            public bool MaleHeadTargetGizmoDragging;
        }

        private static readonly FieldInfo FiHActionFemale  = HarmonyLib.AccessTools.Field(typeof(HActionBase), "female");
        private static readonly FieldInfo FiHActionFemale1 = HarmonyLib.AccessTools.Field(typeof(HActionBase), "female1");
        private static readonly FieldInfo FiHActionMale    = HarmonyLib.AccessTools.Field(typeof(HActionBase), "male");
        private static readonly FieldInfo FiHActionMale1   = HarmonyLib.AccessTools.Field(typeof(HActionBase), "male1");
        private static readonly FieldInfo FiHActionItem    = HarmonyLib.AccessTools.Field(typeof(HActionBase), "item");

        private static readonly FieldInfo FiHSceneLstFemale =
            HarmonyLib.AccessTools.Field(typeof(HSceneProc), "lstFemale");
        private static readonly FieldInfo FiHSceneLstMale =
            HarmonyLib.AccessTools.Field(typeof(HSceneProc), "lstMale");
        private static readonly FieldInfo FiHSceneFlags =
            HarmonyLib.AccessTools.Field(typeof(HSceneProc), "flags");
        private static readonly PropertyInfo PiHSceneFlags =
            HarmonyLib.AccessTools.Property(typeof(HSceneProc), "flags");
        private static readonly FieldInfo FiHFlagNowAnimationInfo =
            HarmonyLib.AccessTools.Field(typeof(HFlag), "nowAnimationInfo");
        private static readonly PropertyInfo PiHFlagNowAnimationInfo =
            HarmonyLib.AccessTools.Property(typeof(HFlag), "nowAnimationInfo");
        private static readonly FieldInfo FiHFlagSelectAnimationListInfo =
            HarmonyLib.AccessTools.Field(typeof(HFlag), "selectAnimationListInfo");
        private static readonly PropertyInfo PiHFlagSelectAnimationListInfo =
            HarmonyLib.AccessTools.Property(typeof(HFlag), "selectAnimationListInfo");
    }
}
