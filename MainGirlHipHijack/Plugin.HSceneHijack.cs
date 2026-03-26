using System;
using UnityEngine;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        // ── 速さハイジャック ──────────────────────────────────────────────────

        private void TickSpeedHijack()
        {
            if (_settings == null || !_settings.SpeedHijackEnabled)
            {
                _runtime.SpeedHasLastPos = false;
                _runtime.SpeedIsMoving   = false;
                _runtime.SpeedBootstrapSent = false;
                _runtime.InsertBootstrapSent = false;
                return;
            }

            HFlag flags = GetHFlags();
            if (flags == null)
                return;

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f)
                return;

            Vector3 current = GetSpeedTrackingPosition();

            if (!_runtime.SpeedHasLastPos)
            {
                _runtime.SpeedLastPos    = current;
                _runtime.SpeedHasLastPos = true;
                _runtime.SpeedLastMoveTime = Time.unscaledTime;
                return;
            }

            Vector3 delta   = current - _runtime.SpeedLastPos;
            _runtime.SpeedLastPos = current;

            float threshold = _settings.SpeedMovementThreshold;
            bool  moved     = delta.sqrMagnitude >= threshold * threshold;
            float now       = Time.unscaledTime;

            TryQueueSpeedBootstrap(flags, moved, delta.sqrMagnitude);
            TryQueueIdleInsert(flags, moved);

            if (moved)
            {
                if (!_runtime.SpeedIsMoving)
                {
                    _runtime.SpeedIsMoving = true;
                    LogDebug("[Speed] move start");
                }
                _runtime.SpeedLastMoveTime = now;
                ApplySpeedByMotion(flags, dt, moving: true);
                return;
            }

            if (_runtime.SpeedIsMoving && now - _runtime.SpeedLastMoveTime >= _settings.SpeedIdleDelay)
            {
                _runtime.SpeedIsMoving = false;
                LogDebug("[Speed] move stop -> decay");
            }

            if (!_runtime.SpeedIsMoving)
                ApplySpeedByMotion(flags, dt, moving: false);
        }

        private void ApplySpeedByMotion(HFlag flags, float dt, bool moving)
        {
            float addPerSec    = moving ? _settings.SpeedMoveAddPerSecond : -_settings.SpeedDecayPerSecond;
            float deltaPerFrame = addPerSec * dt;

            if (flags.mode == HFlag.EMode.aibu)
            {
                float max = Mathf.Max(0.0001f, flags.speedMaxAibuBody);
                flags.SpeedUpClickAibu(deltaPerFrame, max, _drag: false);
                flags.speed     = Mathf.Clamp(flags.speed, 0f, max);
                flags.speedCalc = Mathf.Clamp01(flags.speed / max);
                return;
            }

            flags.SpeedUpClick(deltaPerFrame, 1f);
            flags.speedCalc = Mathf.Clamp01(flags.speedCalc);
            flags.speed     = EvaluateSpeedByModeCurve(flags, flags.speedCalc);
        }

        private void TryQueueSpeedBootstrap(HFlag flags, bool moved, float moveSqr)
        {
            if (!moved || _runtime.SpeedBootstrapSent)
                return;

            var mode = flags.mode;
            if (mode != HFlag.EMode.sonyu && mode != HFlag.EMode.sonyu3P && mode != HFlag.EMode.sonyu3PMMF)
            {
                _runtime.SpeedBootstrapSent = false;
                return;
            }

            if (_runtime.TargetFemaleCha == null)
                return;

            AnimatorStateInfo animState;
            try { animState = _runtime.TargetFemaleCha.getAnimatorStateInfo(0); }
            catch { return; }

            if (!animState.IsName("InsertIdle") && !animState.IsName("A_InsertIdle"))
            {
                _runtime.SpeedBootstrapSent = false;
                return;
            }

            if (flags.click != HFlag.ClickKind.none)
                return;

            flags.click = HFlag.ClickKind.speedup;
            _runtime.SpeedBootstrapSent = true;
            LogDebug("[Speed] bootstrap click=speedup");
        }

        private void TryQueueIdleInsert(HFlag flags, bool moved)
        {
            if (!moved || _runtime.InsertBootstrapSent)
                return;
            if (_settings == null || !_settings.AutoInsertOnMoveEnabled)
                return;

            var mode = flags.mode;
            if (mode != HFlag.EMode.sonyu && mode != HFlag.EMode.sonyu3P && mode != HFlag.EMode.sonyu3PMMF)
            {
                _runtime.InsertBootstrapSent = false;
                return;
            }

            if (_runtime.TargetFemaleCha == null)
                return;

            AnimatorStateInfo animState;
            try { animState = _runtime.TargetFemaleCha.getAnimatorStateInfo(0); }
            catch { return; }

            if (!animState.IsName("Idle") && !animState.IsName("A_Idle"))
            {
                _runtime.InsertBootstrapSent = false;
                return;
            }

            if (flags.click != HFlag.ClickKind.none)
                return;

            flags.click = HFlag.ClickKind.insert;
            _runtime.InsertBootstrapSent = true;
            LogInfo("[AutoInsert] idle move detected -> click=insert");
        }

        private static float EvaluateSpeedByModeCurve(HFlag flags, float speedCalc)
        {
            float x = Mathf.Clamp01(speedCalc);
            AnimationCurve curve = null;
            switch (flags.mode)
            {
                case HFlag.EMode.houshi:
                case HFlag.EMode.houshi3P:
                case HFlag.EMode.houshi3PMMF:
                    curve = flags.speedHoushiCurve;
                    break;
                default:
                    curve = flags.speedSonyuCurve;
                    break;
            }
            return (curve != null && curve.length > 0) ? curve.Evaluate(x) : x;
        }

        private Vector3 GetSpeedTrackingPosition()
        {
            if (_bikEff[BIK_BODY].Running && _bikEff[BIK_BODY].Proxy != null)
                return _bikEff[BIK_BODY].Proxy.position;

            if (_runtime.Fbbik != null && _runtime.Fbbik.solver.bodyEffector.bone != null)
                return _runtime.Fbbik.solver.bodyEffector.bone.position;

            return Vector3.zero;
        }

        private HFlag GetHFlags()
        {
            if (_runtime.HSceneProc == null)
                return null;
            return GetMemberValue(_runtime.HSceneProc, FiHSceneFlags, PiHSceneFlags) as HFlag;
        }

        // ── アニメーション速度切断 ────────────────────────────────────────────

        internal bool TryApplyFemaleAnimSpeedCut(
            HActionBase action, string param, float value, bool isMale, bool isFemale1)
        {
            if (_settings == null || !_settings.CutFemaleAnimSpeedEnabled || action == null)
                return false;

            if (!string.Equals(param, "speed",     StringComparison.Ordinal) &&
                !string.Equals(param, "speedBody", StringComparison.Ordinal))
                return false;

            if (FiHActionFemale == null || FiHActionFemale1 == null ||
                FiHActionMale   == null || FiHActionMale1   == null || FiHActionItem == null)
                return false;

            int paramHash = Animator.StringToHash(param);

            var female  = FiHActionFemale.GetValue(action)  as ChaControl;
            var female1 = FiHActionFemale1.GetValue(action) as ChaControl;
            var male    = FiHActionMale.GetValue(action)    as ChaControl;
            var male1   = FiHActionMale1.GetValue(action)   as ChaControl;
            var item    = FiHActionItem.GetValue(action)    as ItemObject;

            // female/female1 はカット（no-op）
            if (male  != null && male.visibleAll  && isMale)  male.setAnimatorParamFloat(paramHash, value);
            if (male1 != null && isMale)                      male1.setAnimatorParamFloat(paramHash, value);
            if (item  != null)                                 item.SetAnimatorParamFloat(paramHash, value);

            return true;
        }

        // ── 男女アニメ同期リセット ──────────────────────────────────────────
        private void SyncResetAnimators()
        {
            // 強弱ループモーション中のみリセット（他のモーションは触らない）
            if (_runtime.TargetFemaleCha == null) return;

            AnimatorStateInfo femaleState;
            try { femaleState = _runtime.TargetFemaleCha.getAnimatorStateInfo(0); }
            catch { return; }

            if (!IsLoopMotion(femaleState))
            {
                LogDebug("[SyncReset] skipped — not in loop motion");
                return;
            }

            ResetAnimatorToStart(_runtime.TargetFemaleCha, "female");
            ResetAnimatorToStart(_runtime.TargetMaleCha, "male");
        }

        private static bool IsLoopMotion(AnimatorStateInfo state)
        {
            return state.IsName("SLoop") || state.IsName("A_SLoop")
                || state.IsName("SS_IN_Loop") || state.IsName("SF_IN_Loop")
                || state.IsName("WLoop") || state.IsName("A_WLoop")
                || state.IsName("WS_IN_Loop") || state.IsName("WF_IN_Loop");
        }

        private void ResetAnimatorToStart(ChaControl cha, string label)
        {
            if (cha == null) return;

            var animator = cha.animBody != null
                ? cha.animBody.GetComponent<Animator>()
                : cha.GetComponentInChildren<Animator>(true);
            if (animator == null) return;

            var state = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(state.fullPathHash, 0, 0f);
            LogDebug($"[SyncReset] {label} animator reset to 0");
        }
    }
}
