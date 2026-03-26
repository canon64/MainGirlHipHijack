using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        private IEnumerator CapturePosePresetScreenshotCoroutine(PosePresetRuntime preset)
        {
            if (preset == null)
                yield break;

            yield return new WaitForEndOfFrame();

            string shotPath = GetPosePresetScreenshotPath(preset);
            if (string.IsNullOrEmpty(shotPath))
                yield break;

            try
            {
                ScreenCapture.CaptureScreenshot(shotPath);
            }
            catch (Exception ex)
            {
                LogError("[PosePreset] screenshot capture failed: " + ex.Message);
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < deadline)
            {
                try
                {
                    if (File.Exists(shotPath))
                    {
                        var fi = new FileInfo(shotPath);
                        if (fi.Length > 0)
                            break;
                    }
                }
                catch
                {
                    // ignore polling errors
                }

                yield return null;
            }

            _posePresetThumbDirty = true;
            LogInfo("[PosePreset] screenshot saved path=" + shotPath);
        }

        private Texture2D GetPosePresetThumbnail(PosePresetRuntime preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.id))
                return null;

            Texture2D cached;
            if (_posePresetThumbCache.TryGetValue(preset.id, out cached))
                return cached;

            string shotPath = GetPosePresetScreenshotPath(preset);
            if (string.IsNullOrEmpty(shotPath) || !File.Exists(shotPath))
                return null;

            try
            {
                byte[] bytes = File.ReadAllBytes(shotPath);
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!ImageConversion.LoadImage(tex, bytes))
                {
                    Destroy(tex);
                    return null;
                }

                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                _posePresetThumbCache[preset.id] = tex;
                return tex;
            }
            catch (Exception ex)
            {
                LogError("[PosePreset] thumbnail load failed: " + ex.Message);
                return null;
            }
        }

        private void RefreshPosePresetThumbCacheIfNeeded()
        {
            if (!_posePresetThumbDirty)
                return;

            DisposePosePresetThumbCache();
            _posePresetThumbDirty = false;
        }

        private void DisposePosePresetThumbCache()
        {
            foreach (var kv in _posePresetThumbCache)
            {
                if (kv.Value != null)
                    Destroy(kv.Value);
            }
            _posePresetThumbCache.Clear();
        }

        private void HandlePosePresetThumbnailClick(PosePresetRuntime preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.id))
                return;

            float now = Time.unscaledTime;
            bool isDouble = string.Equals(_lastThumbClickedPresetId, preset.id, StringComparison.Ordinal)
                && (now - _lastThumbClickTime) <= PoseThumbnailDoubleClickWindow;

            _lastThumbClickedPresetId = preset.id;
            _lastThumbClickTime = now;

            if (!isDouble)
                return;

            ApplyPosePresetById(preset.id, requireCurrentPosture: true, reason: "thumb-double-click");
        }

        private bool TryApplyRandomAutoPoseForCurrentPosture(string reason)
        {
            EnsurePosePresetsLoaded();
            if (!_runtime.HasNowAnimationInfoCached)
                return false;

            var candidates = new List<PosePresetRuntime>();
            for (int i = 0; i < _posePresets.Count; i++)
            {
                PosePresetRuntime preset = _posePresets[i];
                if (preset == null || !preset.autoApply)
                    continue;
                if (!IsCurrentPoseContextMatch(preset, out _))
                    continue;

                candidates.Add(preset);
            }

            if (candidates.Count <= 0)
            {
                LogDebug("[PosePreset] auto candidate none posture=" + BuildCurrentPostureHint());
                DisableAllBodyIK();
                return false;
            }

            int selected = UnityEngine.Random.Range(0, candidates.Count);
            if (candidates.Count > 1 && !string.IsNullOrEmpty(_autoPoseLastAppliedPresetId))
            {
                for (int i = 0; i < 4; i++)
                {
                    PosePresetRuntime probe = candidates[selected];
                    if (!string.Equals(probe.id, _autoPoseLastAppliedPresetId, StringComparison.Ordinal))
                        break;
                    selected = UnityEngine.Random.Range(0, candidates.Count);
                }
            }

            PosePresetRuntime target = candidates[selected];
            bool applied = ApplyPosePreset(target, requireCurrentPosture: true, reason: reason);
            if (applied)
            {
                _autoPosePendingApply = false;
                _autoPoseLoopCountSinceSwitch = 0;
            }

            return applied;
        }

        private int CountAutoPoseCandidatesForCurrentPosture()
        {
            EnsurePosePresetsLoaded();
            if (!_runtime.HasNowAnimationInfoCached)
                return 0;

            int count = 0;
            for (int i = 0; i < _posePresets.Count; i++)
            {
                PosePresetRuntime preset = _posePresets[i];
                if (preset == null || !preset.autoApply)
                    continue;
                if (!IsCurrentPoseContextMatch(preset, out _))
                    continue;

                count++;
            }

            return count;
        }

        private void OnPostureContextChanged(int id, int mode, string name, string source)
        {
            HandleFemaleHeadAngleContextChange(source);
            _autoPosePendingApply = _settings != null && _settings.AutoPoseEnabled;
            _autoPoseLoopReady = false;
            _autoPoseLoopCountSinceSwitch = 0;
            _autoPoseLastAppliedPresetId = null;

            if (_settings != null && _settings.DetailLogEnabled)
            {
                LogInfo("[PosePreset] posture context changed source=" + source
                    + " id=" + id
                    + " mode=" + mode
                    + " name=" + (name ?? string.Empty)
                    + " autoPending=" + _autoPosePendingApply);
            }
        }

        private void OnPostureContextCleared()
        {
            HandleFemaleHeadAngleContextChange("posture-context-cleared");
            _autoPosePendingApply = false;
            _autoPoseLoopReady = false;
            _autoPoseLoopCountSinceSwitch = 0;
            _autoPoseLastAppliedPresetId = null;
            _autoPoseLastAnimatorStateHash = 0;
            _autoPoseLastAnimatorLoop = 0;
        }

        private void ProcessAutoPoseRuntime()
        {
            if (_settings == null || !_settings.AutoPoseEnabled)
                return;
            if (!_runtime.HasNowAnimationInfoCached)
                return;

            if (_autoPosePendingApply)
            {
                TryApplyRandomAutoPoseForCurrentPosture("auto-posture-enter");
                _autoPosePendingApply = false;
            }

            UpdateAutoPoseLoopCounter();
        }

        private void UpdateAutoPoseLoopCounter()
        {
            Animator anim = _runtime.AnimBodyCached;
            if (anim == null)
                anim = _runtime.TargetFemaleCha != null ? _runtime.TargetFemaleCha.animBody : null;
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
            int loop = Mathf.Max(0, Mathf.FloorToInt(stateInfo.normalizedTime));

            if (!_autoPoseLoopReady || hash != _autoPoseLastAnimatorStateHash)
            {
                bool isMotionChange = _autoPoseLoopReady && hash != _autoPoseLastAnimatorStateHash;
                _autoPoseLoopReady = true;
                _autoPoseLastAnimatorStateHash = hash;
                _autoPoseLastAnimatorLoop = loop;
                _autoPoseLoopCountSinceSwitch = 0;

                if (isMotionChange && _settings != null && _settings.AutoPoseEnabled && _runtime.HasNowAnimationInfoCached)
                {
                    RequestAbandonAllBodyIKByPostureChange("animator-state-changed");
                    OnPostureContextChanged(
                        _runtime.NowAnimationInfoIdCached,
                        _runtime.NowAnimationInfoModeCached,
                        _runtime.NowAnimationInfoNameCached,
                        "animator-state-changed");
                }
                return;
            }

            if (loop <= _autoPoseLastAnimatorLoop)
                return;

            int delta = loop - _autoPoseLastAnimatorLoop;
            _autoPoseLastAnimatorLoop = loop;
            _autoPoseLoopCountSinceSwitch += delta;

            int candidateCount = CountAutoPoseCandidatesForCurrentPosture();
            if (candidateCount <= 1)
            {
                _autoPoseLoopCountSinceSwitch = 0;
                return;
            }

            int threshold = Mathf.Max(1, _settings.AutoPoseSwitchAnimationLoops);
            if (_settings.DetailLogEnabled)
            {
                LogInfo("[PosePreset] auto loop +" + delta
                    + " total=" + _autoPoseLoopCountSinceSwitch + "/" + threshold
                    + " posture=" + BuildCurrentPostureHint());
            }

            if (_autoPoseLoopCountSinceSwitch < threshold)
                return;

            _autoPoseLoopCountSinceSwitch = 0;
            TryApplyRandomAutoPoseForCurrentPosture("auto-loop-threshold");
        }

        private float GetPoseTransitionSeconds()
        {
            if (_settings == null)
                return 0.5f;

            return Mathf.Clamp(_settings.PoseTransitionSeconds, 0f, 5f);
        }

        private PoseTransitionEasing GetPoseTransitionEasing()
        {
            if (_settings == null)
                return PoseTransitionEasing.SmoothStep;

            PoseTransitionEasing easing = _settings.PoseTransitionEasing;
            if (!Enum.IsDefined(typeof(PoseTransitionEasing), easing))
                easing = PoseTransitionEasing.SmoothStep;
            return easing;
        }

        private float EvaluatePoseTransitionEasing(float t01)
        {
            float t = Mathf.Clamp01(t01);
            PoseTransitionEasing easing = GetPoseTransitionEasing();

            switch (easing)
            {
                case PoseTransitionEasing.Linear:
                    return t;
                case PoseTransitionEasing.EaseOutCubic:
                    return 1f - Mathf.Pow(1f - t, 3f);
                case PoseTransitionEasing.SmoothStep:
                default:
                    return Mathf.SmoothStep(0f, 1f, t);
            }
        }
    }
}
