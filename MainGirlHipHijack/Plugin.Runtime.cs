using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RootMotion.FinalIK;
using UnityEngine;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        private bool TryResolveRuntimeRefs()
        {
            if (_runtime.HSceneProc != null && !_runtime.HSceneProc)
            {
                OnHSceneEnded();
            }

            if (_runtime.HSceneProc == null)
            {
                _runtime.HSceneProc = FindObjectOfType<HSceneProc>();
                if (_runtime.HSceneProc == null)
                {
                    LogResolveMissing("HSceneProc");
                    return false;
                }
                LogInfo("HSceneProc ready");
                ResetHipQuickSetupOnHSceneEnter();
            }

            TrackPostureBySelectAnimationInfo();
            TrackPostureByNowAnimationInfo();

            if (_runtime.TargetFemaleCha == null)
            {
                _runtime.TargetFemaleCha = ResolveMainFemale(_runtime.HSceneProc);
                if (_runtime.TargetFemaleCha == null)
                {
                    LogResolveMissing("FemaleChaControl");
                    return false;
                }
                LogInfo("female ready: " + GetFemaleName(_runtime.TargetFemaleCha));
                _runtime.BoneCache = null;
            }

            if (_runtime.TargetFemaleCha.animBody != null && _runtime.TargetFemaleCha.animBody != _runtime.AnimBodyCached)
            {
                int prevId = _runtime.AnimBodyCached != null ? _runtime.AnimBodyCached.GetInstanceID() : 0;
                int nextId = _runtime.TargetFemaleCha.animBody.GetInstanceID();
                LogInfo("animBody changed: " + prevId + " -> " + nextId);
                ResetCachedBodyIkBindings();
                _runtime.AnimBodyCached = _runtime.TargetFemaleCha.animBody;
                _runtime.HasAnimatorStateHashCached = false;
                _runtime.AnimatorStateHashCached = 0;
                _runtime.HasMotionStrengthCached = false;
                _runtime.MotionStrengthTagCached = PoseMotionStrengthUnknown;
                RequestAbandonAllBodyIKByPostureChange("animBody changed");
                _runtime.Fbbik = null;
                _runtime.BoneCache = null;
            }

            TrackMotionByAnimatorState();

            if (_runtime.Fbbik == null)
            {
                _runtime.Fbbik = ResolveFbbik(_runtime.TargetFemaleCha);
                if (_runtime.Fbbik == null)
                {
                    LogResolveMissing("FullBodyBipedIK");
                    return false;
                }
                LogInfo("FullBodyBipedIK ready");
            }

            if (_runtime.BoneCache == null)
            {
                Transform root = _runtime.TargetFemaleCha.animBody != null
                    ? _runtime.TargetFemaleCha.animBody.transform
                    : _runtime.TargetFemaleCha.transform;
                _runtime.BoneCache = root != null ? root.GetComponentsInChildren<Transform>(true) : null;
                if (_runtime.BoneCache != null)
                    LogDebug("bone cache ready count=" + _runtime.BoneCache.Length);
            }

            if (_settings.AutoEnableAllOnResolve && !_runtime.AutoEnabledInCurrentH && !_abandonedByPostureChange)
            {
                _runtime.AutoEnabledInCurrentH = true;
                for (int i = 0; i < BIK_TOTAL; i++)
                    SetBodyIK(i, true, saveSettings: false, reason: "auto-resolve");
                SaveSettings();
                LogInfo("auto enabled all bodyIK");
                LogStateSnapshot("auto-enable-all", force: true);
            }

            return true;
        }

        private void ResetHipQuickSetupOnHSceneEnter()
        {
            if (_settings == null)
                return;

            bool changed = false;

            if (_settings.SpeedHijackEnabled)
            {
                _settings.SpeedHijackEnabled = false;
                changed = true;
            }

            if (_settings.CutFemaleAnimSpeedEnabled)
            {
                _settings.CutFemaleAnimSpeedEnabled = false;
                changed = true;
            }

            if (_settings.FemaleHeadAngleGizmoVisible)
            {
                _settings.FemaleHeadAngleGizmoVisible = false;
                changed = true;
            }
            DestroyFemaleHeadGizmo();

            bool hadBodyIkState = false;
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                bool running = _bikEff[i] != null && _bikEff[i].Running;
                if (_bikWant[i] || running || (_settings.Enabled != null && i < _settings.Enabled.Length && _settings.Enabled[i]))
                    hadBodyIkState = true;

                if (_bikWant[i])
                {
                    _bikWant[i] = false;
                    changed = true;
                }

                if (_settings.Enabled != null && i < _settings.Enabled.Length && _settings.Enabled[i])
                {
                    _settings.Enabled[i] = false;
                    changed = true;
                }
            }

            if (hadBodyIkState)
            {
                DisableAllBodyIK(silent: true);
                changed = true;
            }

            if (_bodyCtrlLinkEnabled || _leftCtrlHidden)
            {
                DisableBodyCtrlLink();
                RestoreLeftControllerVisuals();
                changed = true;
            }

            if (_hipQuickSetupActive)
            {
                _hipQuickSetupActive = false;
                changed = true;
            }
            if (_hipQuickSetupSavedWant != null)
            {
                _hipQuickSetupSavedWant = null;
                changed = true;
            }
            if (_hipQuickSetupTogglePending)
            {
                _hipQuickSetupTogglePending = false;
                changed = true;
            }

            if (changed)
            {
                SaveSettings();
                LogInfo("hip quick setup reset on HScene enter");
            }
        }

        private void OnHSceneEnded()
        {
            DisableBodyCtrlLink();
            if (_vrGrabMode) ExitVRGrabMode();
            StopPoseTransitionIfRunning();
            DisableAllBodyIK(silent: true);
            ResetCachedBodyIkBindings();
            _abandonedByPostureChange = false;
            _pendingAbandonByPostureChange = false;
            _pendingAbandonTrigger = null;
            _pendingAbandonRequestTime = 0f;
            _hipQuickSetupActive = false;
            _hipQuickSetupSavedWant = null;
            _hipQuickSetupTogglePending = false;
            ResetBodyIkDiagnosticsState();
            _runtime.HSceneProc = null;
            _runtime.TargetFemaleCha = null;
            _runtime.AnimBodyCached = null;
            _runtime.HasAnimatorStateHashCached = false;
            _runtime.AnimatorStateHashCached = 0;
            _runtime.HasMotionStrengthCached = false;
            _runtime.MotionStrengthTagCached = PoseMotionStrengthUnknown;
            _runtime.Fbbik = null;
            _runtime.AutoEnabledInCurrentH = false;
            _runtime.HasNowAnimationInfoCached = false;
            _runtime.NowAnimationInfoIdCached = 0;
            _runtime.NowAnimationInfoModeCached = 0;
            _runtime.NowAnimationInfoNameCached = null;
            _runtime.HasSelectAnimationInfoCached = false;
            _runtime.SelectAnimationInfoIdCached = 0;
            _runtime.SelectAnimationInfoModeCached = 0;
            _runtime.SelectAnimationInfoNameCached = null;
            _runtime.BoneCache = null;
            ClearMaleRefs();
            ClearExternalFollowTargets();
            OnPostureContextCleared();
            LogInfo("HScene ended -> cleanup");
            LogStateSnapshot("hscene-ended", force: true);
        }

        private ChaControl ResolveMainFemale(HSceneProc proc)
        {
            if (proc == null)
                return null;

            if (FiHSceneLstFemale != null)
            {
                IList listObj = FiHSceneLstFemale.GetValue(proc) as IList;
                if (listObj != null)
                {
                    for (int i = 0; i < listObj.Count; i++)
                    {
                        ChaControl cha = listObj[i] as ChaControl;
                        if (cha != null)
                            return cha;
                    }
                }
            }

            ChaControl[] all = FindObjectsOfType<ChaControl>();
            for (int i = 0; i < all.Length; i++)
            {
                ChaControl cha = all[i];
                if (cha != null && cha.sex == 1)
                    return cha;
            }

            return null;
        }

        private static FullBodyBipedIK ResolveFbbik(ChaControl cha)
        {
            if (cha == null || cha.animBody == null)
                return null;

            FullBodyBipedIK ik = cha.animBody.GetComponent<FullBodyBipedIK>();
            if (ik != null)
                return ik;

            return cha.animBody.GetComponentInChildren<FullBodyBipedIK>(true);
        }

        private void TrackPostureByNowAnimationInfo()
        {
            object info = GetNowAnimationInfo(_runtime.HSceneProc);
            if (info == null)
            {
                if (_runtime.HasNowAnimationInfoCached)
                {
                    string prev = BuildPostureInfoText(
                        _runtime.NowAnimationInfoIdCached,
                        _runtime.NowAnimationInfoModeCached,
                        _runtime.NowAnimationInfoNameCached);
                    LogInfo("posture changed: " + prev + " -> (null)");
                    _runtime.HasNowAnimationInfoCached = false;
                    _runtime.NowAnimationInfoIdCached = 0;
                    _runtime.NowAnimationInfoModeCached = 0;
                    _runtime.NowAnimationInfoNameCached = null;
                    OnPostureContextCleared();
                }
                return;
            }

            int id = GetIntMemberValue(info, "id", int.MinValue);
            int mode = GetIntMemberValue(info, "mode", int.MinValue);
            string name = GetStringMemberValue(info, "nameAnimation");

            if (!_runtime.HasNowAnimationInfoCached)
            {
                _runtime.HasNowAnimationInfoCached = true;
                _runtime.NowAnimationInfoIdCached = id;
                _runtime.NowAnimationInfoModeCached = mode;
                _runtime.NowAnimationInfoNameCached = name;
                LogInfo("posture init: " + BuildPostureInfoText(id, mode, name));
                OnPostureContextChanged(id, mode, name, "posture-init");
                return;
            }

            bool changed = _runtime.NowAnimationInfoIdCached != id
                || _runtime.NowAnimationInfoModeCached != mode
                || !string.Equals(_runtime.NowAnimationInfoNameCached ?? string.Empty, name ?? string.Empty, StringComparison.Ordinal);
            if (!changed)
                return;

            string prevText = BuildPostureInfoText(
                _runtime.NowAnimationInfoIdCached,
                _runtime.NowAnimationInfoModeCached,
                _runtime.NowAnimationInfoNameCached);
            string nextText = BuildPostureInfoText(id, mode, name);
            LogInfo("posture changed: " + prevText + " -> " + nextText);

            _runtime.NowAnimationInfoIdCached = id;
            _runtime.NowAnimationInfoModeCached = mode;
            _runtime.NowAnimationInfoNameCached = name;
            OnPostureContextChanged(id, mode, name, "posture-changed");
        }

        private void TrackPostureBySelectAnimationInfo()
        {
            object info = GetSelectAnimationListInfo(_runtime.HSceneProc);
            if (info == null)
            {
                if (_runtime.HasSelectAnimationInfoCached)
                {
                    if (_settings != null && _settings.DetailLogEnabled)
                    {
                        string prev = BuildPostureInfoText(
                            _runtime.SelectAnimationInfoIdCached,
                            _runtime.SelectAnimationInfoModeCached,
                            _runtime.SelectAnimationInfoNameCached);
                        LogInfo("posture select cleared: " + prev + " -> (null)");
                    }

                    _runtime.HasSelectAnimationInfoCached = false;
                    _runtime.SelectAnimationInfoIdCached = 0;
                    _runtime.SelectAnimationInfoModeCached = 0;
                    _runtime.SelectAnimationInfoNameCached = null;
                }
                return;
            }

            int id = GetIntMemberValue(info, "id", int.MinValue);
            int mode = GetIntMemberValue(info, "mode", int.MinValue);
            string name = GetStringMemberValue(info, "nameAnimation");

            if (!_runtime.HasSelectAnimationInfoCached)
            {
                _runtime.HasSelectAnimationInfoCached = true;
                _runtime.SelectAnimationInfoIdCached = id;
                _runtime.SelectAnimationInfoModeCached = mode;
                _runtime.SelectAnimationInfoNameCached = name;
                LogInfo("posture select set: " + BuildPostureInfoText(id, mode, name));
                RequestAbandonAllBodyIKByPostureChange("selectAnimationListInfo set");
                return;
            }

            bool changed = _runtime.SelectAnimationInfoIdCached != id
                || _runtime.SelectAnimationInfoModeCached != mode
                || !string.Equals(_runtime.SelectAnimationInfoNameCached ?? string.Empty, name ?? string.Empty, StringComparison.Ordinal);
            if (!changed)
                return;

            string prevText = BuildPostureInfoText(
                _runtime.SelectAnimationInfoIdCached,
                _runtime.SelectAnimationInfoModeCached,
                _runtime.SelectAnimationInfoNameCached);

            _runtime.SelectAnimationInfoIdCached = id;
            _runtime.SelectAnimationInfoModeCached = mode;
            _runtime.SelectAnimationInfoNameCached = name;

            string nextText = BuildPostureInfoText(id, mode, name);
            LogInfo("posture select changed: " + prevText + " -> " + nextText);
            RequestAbandonAllBodyIKByPostureChange("selectAnimationListInfo changed");
        }

        private void TrackMotionByAnimatorState()
        {
            Animator anim = _runtime.AnimBodyCached;
            if (anim == null)
                return;

            AnimatorStateInfo stateInfo;
            try
            {
                stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            }
            catch
            {
                return;
            }

            int hash = stateInfo.fullPathHash;
            string strengthTag = ClassifyMotionStrengthTag(stateInfo);
            if (!_runtime.HasAnimatorStateHashCached)
            {
                _runtime.HasAnimatorStateHashCached = true;
                _runtime.AnimatorStateHashCached = hash;
                if (_settings != null && _settings.DetailLogEnabled)
                    LogInfo("anim state init hash=" + hash);
            }
            else if (_runtime.AnimatorStateHashCached != hash)
            {
                int prev = _runtime.AnimatorStateHashCached;
                _runtime.AnimatorStateHashCached = hash;
                HandleFemaleHeadAngleContextChange("anim-state-hash-changed");

                // Animator state hash changes during the same posture are frequent.
                // Treat this as observation only; abandoning IK here can race with ChangeAnimator.
                if (_settings != null && _settings.DetailLogEnabled)
                    LogInfo("anim state changed(observe): " + prev + " -> " + hash);
            }

            if (!_runtime.HasMotionStrengthCached)
            {
                _runtime.HasMotionStrengthCached = true;
                _runtime.MotionStrengthTagCached = strengthTag;
                if (_settings != null && _settings.DetailLogEnabled)
                    LogInfo("motion strength init: " + strengthTag);
                return;
            }

            string prevStrength = NormalizePoseMotionStrength(_runtime.MotionStrengthTagCached);
            if (string.Equals(prevStrength, strengthTag, StringComparison.Ordinal))
                return;

            _runtime.MotionStrengthTagCached = strengthTag;
            if (_settings != null && _settings.DetailLogEnabled)
                LogInfo("motion strength changed: " + prevStrength + " -> " + strengthTag);

            if (!IsStrongWeakSwitch(prevStrength, strengthTag))
                return;

            RequestAbandonAllBodyIKByPostureChange("motion strength changed " + prevStrength + "->" + strengthTag);
            if (_runtime.HasNowAnimationInfoCached)
            {
                OnPostureContextChanged(
                    _runtime.NowAnimationInfoIdCached,
                    _runtime.NowAnimationInfoModeCached,
                    _runtime.NowAnimationInfoNameCached,
                    "motion-strength-changed");
            }
        }

        private static object GetNowAnimationInfo(HSceneProc proc)
        {
            if (proc == null)
                return null;

            object flags = GetMemberValue(proc, FiHSceneFlags, PiHSceneFlags);
            if (flags == null)
                return null;

            return GetMemberValue(flags, FiHFlagNowAnimationInfo, PiHFlagNowAnimationInfo);
        }

        private static object GetSelectAnimationListInfo(HSceneProc proc)
        {
            if (proc == null)
                return null;

            object flags = GetMemberValue(proc, FiHSceneFlags, PiHSceneFlags);
            if (flags == null)
                return null;

            return GetMemberValue(flags, FiHFlagSelectAnimationListInfo, PiHFlagSelectAnimationListInfo);
        }

        private static object GetMemberValue(object owner, FieldInfo fi, PropertyInfo pi)
        {
            if (owner == null)
                return null;

            try
            {
                if (fi != null)
                    return fi.GetValue(owner);
                if (pi != null)
                    return pi.GetValue(owner, null);
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static int GetIntMemberValue(object owner, string memberName, int fallback)
        {
            object value = GetMemberValueByName(owner, memberName);
            if (value == null)
                return fallback;

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return fallback;
            }
        }

        private static string GetStringMemberValue(object owner, string memberName)
        {
            object value = GetMemberValueByName(owner, memberName);
            return value != null ? value.ToString() : string.Empty;
        }

        private static object GetMemberValueByName(object owner, string memberName)
        {
            if (owner == null || string.IsNullOrEmpty(memberName))
                return null;

            Type type = owner.GetType();
            FieldInfo fi = AccessTools.Field(type, memberName);
            if (fi != null)
            {
                try
                {
                    return fi.GetValue(owner);
                }
                catch
                {
                    // ignore
                }
            }

            PropertyInfo pi = AccessTools.Property(type, memberName);
            if (pi != null)
            {
                try
                {
                    return pi.GetValue(owner, null);
                }
                catch
                {
                    // ignore
                }
            }

            return null;
        }

        private static string BuildPostureInfoText(int id, int mode, string name)
        {
            return "id=" + id + ", mode=" + mode + ", name=" + (string.IsNullOrEmpty(name) ? "(empty)" : name);
        }

        private static string ClassifyMotionStrengthTag(AnimatorStateInfo stateInfo)
        {
            if (IsStrongMotionState(stateInfo))
                return PoseMotionStrengthStrong;
            if (IsWeakMotionState(stateInfo))
                return PoseMotionStrengthWeak;
            return PoseMotionStrengthUnknown;
        }

        private static bool IsStrongMotionState(AnimatorStateInfo stateInfo)
        {
            return stateInfo.IsName("SLoop")
                || stateInfo.IsName("A_SLoop")
                || stateInfo.IsName("SS_IN_Loop")
                || stateInfo.IsName("SF_IN_Loop")
                || stateInfo.IsName("sameS")
                || stateInfo.IsName("orgS");
        }

        private static bool IsWeakMotionState(AnimatorStateInfo stateInfo)
        {
            return stateInfo.IsName("WLoop")
                || stateInfo.IsName("A_WLoop")
                || stateInfo.IsName("WS_IN_Loop")
                || stateInfo.IsName("WF_IN_Loop")
                || stateInfo.IsName("sameW")
                || stateInfo.IsName("orgW");
        }

        private static bool IsStrongWeakSwitch(string prevTag, string nextTag)
        {
            bool prevKnown = prevTag == PoseMotionStrengthStrong || prevTag == PoseMotionStrengthWeak;
            bool nextKnown = nextTag == PoseMotionStrengthStrong || nextTag == PoseMotionStrengthWeak;
            if (!prevKnown || !nextKnown)
                return false;

            return !string.Equals(prevTag, nextTag, StringComparison.Ordinal);
        }

        private void LogResolveMissing(string key)
        {
            float now = Time.unscaledTime;
            if (string.Equals(_lastResolveMissing, key, StringComparison.Ordinal) && now < _nextResolveMissingLogTime)
                return;

            _lastResolveMissing = key;
            _nextResolveMissingLogTime = now + 1f;
            LogDebug("resolve missing: " + key);
        }

        private static string GetFemaleName(ChaControl cha)
        {
            if (cha == null)
                return "(null)";

            try
            {
                if (cha.chaFile != null && cha.chaFile.parameter != null && !string.IsNullOrEmpty(cha.chaFile.parameter.fullname))
                    return cha.chaFile.parameter.fullname;
            }
            catch
            {
                // ignore
            }

            return cha.name ?? "(unnamed)";
        }
    }
}
