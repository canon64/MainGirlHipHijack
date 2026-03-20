using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        private sealed class PoseTransitionPoint
        {
            public int Index;
            public float StartWeight;
            public float TargetWeight;
            public bool DisableAfterTransition;
            public bool HasPose;
            public Vector3 StartPos;
            public Quaternion StartRot;
            public Vector3 TargetPos;
            public Quaternion TargetRot;
            public bool RebindFollowAfterTransition;
            public Transform FollowBone;
            public Vector3 FollowPositionOffset;
            public Quaternion FollowRotationOffset;
        }

        private void SaveCurrentPosePresetWithScreenshot(string requestedName)
        {
            EnsurePosePresetsLoaded();

            if (!TryResolveRuntimeRefs())
            {
                LogWarn("[PosePreset] save failed: runtime refs not ready");
                return;
            }
            if (!_runtime.HasNowAnimationInfoCached)
            {
                LogWarn("[PosePreset] save failed: posture not ready current=" + BuildCurrentPostureHint());
                return;
            }

            PosePresetRuntime preset = BuildCurrentPosePresetSnapshot(requestedName);
            _posePresets.Insert(0, preset);
            SavePosePresetIndex();
            _posePresetThumbDirty = true;

            StartCoroutine(CapturePosePresetScreenshotCoroutine(preset));
            LogInfo("[PosePreset] saved id=" + preset.id + " name=" + preset.name + " posture=" + BuildPosePostureHint(preset));
        }

        private PosePresetRuntime BuildCurrentPosePresetSnapshot(string requestedName)
        {
            DateTime now = DateTime.Now;
            string id = now.ToString("yyyyMMdd_HHmmss_fff");
            string name = BuildNumberedPosePresetName(requestedName);

            var preset = new PosePresetRuntime
            {
                id = id,
                name = name,
                createdAt = now.ToString("yyyy-MM-dd HH:mm:ss"),
                screenshotFile = id + ".png",
                postureId = _runtime.NowAnimationInfoIdCached,
                postureMode = _runtime.NowAnimationInfoModeCached,
                postureName = _runtime.NowAnimationInfoNameCached,
                postureStrength = GetCurrentMotionStrengthTagForPoseContext(),
                autoApply = false,
                hasFemaleHeadAdditive = _femaleHeadHasAdditive,
                femaleHeadAdditiveOffset = _femaleHeadHasAdditive
                    ? _femaleHeadAdditiveOffset
                    : Quaternion.identity,
                entries = new PosePresetEntryRuntime[BIK_TOTAL]
            };
            Transform followRoot = GetPosePresetRootTransform();

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                var entry = new PosePresetEntryRuntime
                {
                    enabled = _bikWant[i] || _bikEff[i].Running,
                    weight = GetBodyIKWeight(i)
                };

                if (_bikEff[i].Proxy != null)
                {
                    entry.hasProxyPose = true;
                    entry.proxyPosition = _bikEff[i].Proxy.position;
                    entry.proxyRotation = _bikEff[i].Proxy.rotation;
                }
                if (CanUseBoneFollow(i) && _bikEff[i].FollowBone != null && followRoot != null)
                {
                    string path = BuildRelativePath(followRoot, _bikEff[i].FollowBone);
                    if (path != null)
                    {
                        entry.hasFollowBone = true;
                        entry.followBonePath = path;
                        entry.followPositionOffset = _bikEff[i].FollowBonePositionOffset;
                        entry.followRotationOffset = _bikEff[i].FollowBoneRotationOffset;
                    }
                    else
                    {
                        LogWarn("[PosePreset] follow save skipped idx=" + i + " bone=" + _bikEff[i].FollowBone.name + " reason=path-outside-root");
                    }
                }

                preset.entries[i] = entry;
            }

            NormalizePosePreset(preset);
            return preset;
        }

        private bool ApplyPosePresetById(string id, bool requireCurrentPosture, string reason)
        {
            EnsurePosePresetsLoaded();
            if (string.IsNullOrEmpty(id))
                return false;

            for (int i = 0; i < _posePresets.Count; i++)
            {
                PosePresetRuntime preset = _posePresets[i];
                if (preset == null || !string.Equals(preset.id, id, StringComparison.Ordinal))
                    continue;

                return ApplyPosePreset(preset, requireCurrentPosture, reason);
            }

            LogWarn("[PosePreset] apply failed: not found id=" + id);
            return false;
        }

        private bool ApplyPosePreset(PosePresetRuntime preset, bool requireCurrentPosture, string reason)
        {
            if (preset == null)
                return false;

            NormalizePosePreset(preset);

            if (!TryResolveRuntimeRefs())
            {
                LogWarn("[PosePreset] apply failed: runtime refs not ready");
                return false;
            }
            string contextMismatch;
            if (requireCurrentPosture && !IsCurrentPoseContextMatch(preset, out contextMismatch))
            {
                LogWarn("[PosePreset] context mismatch: " + contextMismatch
                    + " preset=" + BuildPosePostureHint(preset)
                    + " current=" + BuildCurrentPostureHint());
                return false;
            }

            StopPoseTransitionIfRunning();
            Transform followRoot = GetPosePresetRootTransform();

            float transitionSeconds = GetEffectivePoseTransitionSeconds();
            var transitionPoints = new List<PoseTransitionPoint>();
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                // Keep hip-center IK (Body) out of pose apply flow.
                // User-operated state should persist across manual/auto preset apply.
                if (i == BIK_BODY)
                    continue;

                PosePresetEntryRuntime entry = preset.entries[i] ?? new PosePresetEntryRuntime();
                bool enableInPreset = entry.enabled;
                float targetWeight = enableInPreset ? Mathf.Clamp01(entry.weight) : 0f;

                if (!enableInPreset)
                {
                    ClearBodyIKFollowBone(i);
                    if (transitionSeconds > 0f && _bikEff[i].Running)
                    {
                        // Keep IK alive while blending out to zero, then disable at transition end.
                        SetBodyIK(i, true, saveSettings: false, reason: reason + "-off-blend-keep");

                        var fadeOutPoint = new PoseTransitionPoint
                        {
                            Index = i,
                            StartWeight = GetBodyIKWeight(i),
                            TargetWeight = 0f,
                            DisableAfterTransition = true,
                            HasPose = false
                        };
                        transitionPoints.Add(fadeOutPoint);
                    }
                    else
                    {
                        SetBodyIKWeight(i, 0f, saveSettings: false);
                        SetBodyIK(i, false, saveSettings: false, reason: reason + "-off");
                    }
                    continue;
                }

                SetBodyIK(i, true, saveSettings: false, reason: reason + "-on");
                Transform followBone;
                Vector3 followPosOffset;
                Quaternion followRotOffset;
                bool hasFollowBinding = TryResolvePosePresetFollowBinding(
                    i,
                    followRoot,
                    entry,
                    out followBone,
                    out followPosOffset,
                    out followRotOffset);

                // During transition, keep effector free so easing on proxy pose is visible and stable.
                ClearBodyIKFollowBone(i);
                float startWeight = GetBodyIKWeight(i);

                var point = new PoseTransitionPoint
                {
                    Index = i,
                    StartWeight = startWeight,
                    TargetWeight = targetWeight,
                    DisableAfterTransition = false,
                    HasPose = entry.hasProxyPose,
                    TargetPos = entry.proxyPosition,
                    TargetRot = entry.proxyRotation,
                    RebindFollowAfterTransition = hasFollowBinding,
                    FollowBone = followBone,
                    FollowPositionOffset = followPosOffset,
                    FollowRotationOffset = followRotOffset
                };

                if (_bikEff[i].Proxy != null)
                {
                    point.StartPos = _bikEff[i].Proxy.position;
                    point.StartRot = _bikEff[i].Proxy.rotation;
                }
                else
                {
                    point.StartPos = entry.proxyPosition;
                    point.StartRot = entry.proxyRotation;
                }

                transitionPoints.Add(point);
            }

            _autoPoseLastAppliedPresetId = preset.id;
            _autoPoseLoopCountSinceSwitch = 0;
            _autoPoseLoopReady = false;
            _poseTransitionPresetId = preset.id;
            SetFemaleHeadAdditiveFromPreset(preset);

            _poseTransitionCoroutine = StartCoroutine(ApplyPoseTransitionCoroutine(transitionPoints, transitionSeconds, preset.id));

            LogInfo("[PosePreset] applied id=" + preset.id + " name=" + preset.name + " reason=" + reason
                + " transition=" + transitionSeconds.ToString("F3")
                + " easing=" + GetPoseTransitionEasing());
            return true;
        }

        private void SetFemaleHeadAdditiveFromPreset(PosePresetRuntime preset)
        {
            if (preset == null)
                return;

            SetFemaleHeadAdditiveRotForPreset(
                preset.hasFemaleHeadAdditive,
                preset.femaleHeadAdditiveOffset);
        }

        private bool TryApplyPosePresetFollowBinding(int idx, Transform root, PosePresetEntryRuntime entry)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return false;
            if (!CanUseBoneFollow(idx))
                return false;
            if (!_bikEff[idx].Running || _bikEff[idx].Proxy == null)
                return false;
            if (root == null || entry == null || !entry.hasFollowBone || entry.followBonePath == null)
                return false;

            Transform bone = FindByRelativePath(root, entry.followBonePath);
            if (bone == null)
            {
                LogWarn("[PosePreset] follow bone missing idx=" + idx + " path=" + entry.followBonePath);
                return false;
            }

            _bikEff[idx].FollowBone = bone;
            _bikEff[idx].CandidateBone = null;
            _bikEff[idx].FollowBonePositionOffset = entry.followPositionOffset;
            _bikEff[idx].FollowBoneRotationOffset = IsRotationDrivenEffector(idx)
                ? entry.followRotationOffset
                : Quaternion.identity;
            return true;
        }

        private bool TryResolvePosePresetFollowBinding(
            int idx,
            Transform root,
            PosePresetEntryRuntime entry,
            out Transform bone,
            out Vector3 positionOffset,
            out Quaternion rotationOffset)
        {
            bone = null;
            positionOffset = Vector3.zero;
            rotationOffset = Quaternion.identity;

            if (idx < 0 || idx >= BIK_TOTAL)
                return false;
            if (!CanUseBoneFollow(idx))
                return false;
            if (!_bikEff[idx].Running || _bikEff[idx].Proxy == null)
                return false;
            if (root == null || entry == null || !entry.hasFollowBone || entry.followBonePath == null)
                return false;

            Transform resolved = FindByRelativePath(root, entry.followBonePath);
            if (resolved == null)
            {
                LogWarn("[PosePreset] follow bone missing idx=" + idx + " path=" + entry.followBonePath);
                return false;
            }

            bone = resolved;
            positionOffset = entry.followPositionOffset;
            rotationOffset = IsRotationDrivenEffector(idx)
                ? entry.followRotationOffset
                : Quaternion.identity;
            return true;
        }

        private IEnumerator ApplyPoseTransitionCoroutine(List<PoseTransitionPoint> points, float seconds, string presetId)
        {
            float duration = Mathf.Max(0.01f, seconds);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float tLinear = Mathf.Clamp01(elapsed / duration);
                float t = EvaluatePoseTransitionEasing(tLinear);

                for (int i = 0; i < points.Count; i++)
                {
                    PoseTransitionPoint point = points[i];
                    float w = Mathf.Lerp(point.StartWeight, point.TargetWeight, t);
                    SetBodyIKWeight(point.Index, w, saveSettings: false);

                    if (!point.HasPose)
                        continue;
                    if (!_bikEff[point.Index].Running || _bikEff[point.Index].Proxy == null)
                        continue;

                    Vector3 pos = Vector3.Lerp(point.StartPos, point.TargetPos, t);
                    Quaternion rot = Quaternion.Slerp(point.StartRot, point.TargetRot, t);
                    _bikEff[point.Index].Proxy.SetPositionAndRotation(pos, rot);
                }

                yield return null;
            }

            for (int i = 0; i < points.Count; i++)
            {
                PoseTransitionPoint point = points[i];
                SetBodyIKWeight(point.Index, point.TargetWeight, saveSettings: false);

                if (!point.HasPose)
                    continue;
                if (!_bikEff[point.Index].Running || _bikEff[point.Index].Proxy == null)
                    continue;

                _bikEff[point.Index].Proxy.SetPositionAndRotation(point.TargetPos, point.TargetRot);
            }

            for (int i = 0; i < points.Count; i++)
            {
                PoseTransitionPoint point = points[i];
                if (!point.DisableAfterTransition)
                    continue;

                SetBodyIK(point.Index, false, saveSettings: false, reason: "transition-off");
            }

            for (int i = 0; i < points.Count; i++)
            {
                PoseTransitionPoint point = points[i];
                if (!point.RebindFollowAfterTransition)
                    continue;
                if (point.Index < 0 || point.Index >= BIK_TOTAL)
                    continue;
                if (!_bikEff[point.Index].Running || _bikEff[point.Index].Proxy == null)
                    continue;
                if (point.FollowBone == null)
                    continue;

                _bikEff[point.Index].FollowBone = point.FollowBone;
                _bikEff[point.Index].CandidateBone = null;
                _bikEff[point.Index].FollowBonePositionOffset = point.FollowPositionOffset;
                _bikEff[point.Index].FollowBoneRotationOffset = IsRotationDrivenEffector(point.Index)
                    ? point.FollowRotationOffset
                    : Quaternion.identity;
            }

            _poseTransitionCoroutine = null;
            _poseTransitionPresetId = null;
            SaveSettings();
            LogInfo("[PosePreset] transition complete id=" + (presetId ?? "(null)")
                + " sec=" + duration.ToString("F3")
                + " easing=" + GetPoseTransitionEasing());
        }

        private float GetEffectivePoseTransitionSeconds()
        {
            // Always route through transition/easing path to avoid auto-apply teleport.
            return Mathf.Max(0.01f, GetPoseTransitionSeconds());
        }

        private void StopPoseTransitionIfRunning()
        {
            if (_poseTransitionCoroutine != null)
            {
                StopCoroutine(_poseTransitionCoroutine);
                _poseTransitionCoroutine = null;
            }
            _poseTransitionPresetId = null;
        }

        private void DeletePosePresetById(string id)
        {
            EnsurePosePresetsLoaded();
            if (string.IsNullOrEmpty(id))
                return;

            int removeIndex = -1;
            PosePresetRuntime preset = null;
            for (int i = 0; i < _posePresets.Count; i++)
            {
                if (_posePresets[i] == null || !string.Equals(_posePresets[i].id, id, StringComparison.Ordinal))
                    continue;

                removeIndex = i;
                preset = _posePresets[i];
                break;
            }

            if (removeIndex < 0 || preset == null)
                return;

            _posePresets.RemoveAt(removeIndex);
            SavePosePresetIndex();

            Texture2D tex;
            if (_posePresetThumbCache.TryGetValue(id, out tex) && tex != null)
                Destroy(tex);
            _posePresetThumbCache.Remove(id);

            string shotPath = GetPosePresetScreenshotPath(preset);
            try
            {
                if (!string.IsNullOrEmpty(shotPath) && File.Exists(shotPath))
                    File.Delete(shotPath);
            }
            catch (Exception ex)
            {
                LogError("[PosePreset] screenshot delete failed: " + ex.Message);
            }

            _posePresetThumbDirty = true;
            LogInfo("[PosePreset] deleted id=" + id);
        }

        private void OverwritePosePresetById(string id)
        {
            EnsurePosePresetsLoaded();
            if (string.IsNullOrEmpty(id))
                return;

            PosePresetRuntime existing = null;
            for (int i = 0; i < _posePresets.Count; i++)
            {
                if (_posePresets[i] != null && string.Equals(_posePresets[i].id, id, StringComparison.Ordinal))
                {
                    existing = _posePresets[i];
                    break;
                }
            }

            if (existing == null)
                return;

            if (!TryResolveRuntimeRefs())
            {
                LogWarn("[PosePreset] overwrite failed: runtime refs not ready");
                return;
            }

            PosePresetRuntime updated = BuildCurrentPosePresetSnapshot(existing.name);
            updated.id = existing.id;
            updated.screenshotFile = existing.screenshotFile;
            updated.createdAt = existing.createdAt;
            updated.autoApply = existing.autoApply;

            for (int i = 0; i < _posePresets.Count; i++)
            {
                if (_posePresets[i] != null && string.Equals(_posePresets[i].id, id, StringComparison.Ordinal))
                {
                    _posePresets[i] = updated;
                    break;
                }
            }

            SavePosePresetIndex();
            _posePresetThumbDirty = true;
            StartCoroutine(CapturePosePresetScreenshotCoroutine(updated));
            LogInfo("[PosePreset] overwritten id=" + id + " name=" + updated.name);
        }

        private bool IsCurrentPostureMatch(PosePresetRuntime preset)
        {
            if (preset == null || !_runtime.HasNowAnimationInfoCached)
                return false;

            return preset.postureId == _runtime.NowAnimationInfoIdCached
                && preset.postureMode == _runtime.NowAnimationInfoModeCached
                && string.Equals(
                    preset.postureName ?? string.Empty,
                    _runtime.NowAnimationInfoNameCached ?? string.Empty,
                    StringComparison.Ordinal)
                && IsPoseMotionStrengthMatch(preset.postureStrength, GetCurrentMotionStrengthTagForPoseContext());
        }

        private bool IsCurrentPoseContextMatch(PosePresetRuntime preset, out string reason)
        {
            reason = null;
            if (!IsCurrentPostureMatch(preset))
            {
                reason = "posture";
                return false;
            }

            return true;
        }

        private string BuildCurrentPostureHint()
        {
            if (!_runtime.HasNowAnimationInfoCached)
                return "id=(none), mode=(none), name=(none), strength=(none)";

            return "id=" + _runtime.NowAnimationInfoIdCached
                + ", mode=" + _runtime.NowAnimationInfoModeCached
                + ", name=" + (_runtime.NowAnimationInfoNameCached ?? string.Empty)
                + ", strength=" + GetCurrentMotionStrengthTagForPoseContext();
        }

        private static string BuildPosePostureHint(PosePresetRuntime preset)
        {
            if (preset == null)
                return "id=(none), mode=(none), name=(none), strength=(none)";

            return "id=" + preset.postureId
                + ", mode=" + preset.postureMode
                + ", name=" + (preset.postureName ?? string.Empty)
                + ", strength=" + NormalizePoseMotionStrength(preset.postureStrength);
        }

        private string GetCurrentMotionStrengthTagForPoseContext()
        {
            if (_runtime == null || !_runtime.HasMotionStrengthCached)
                return PoseMotionStrengthUnknown;

            return NormalizePoseMotionStrength(_runtime.MotionStrengthTagCached);
        }

        private static bool IsPoseMotionStrengthMatch(string presetStrength, string currentStrength)
        {
            string saved = NormalizePoseMotionStrength(presetStrength);
            if (saved == PoseMotionStrengthUnknown)
                return true; // backward-compatible: legacy presets without strength still match by posture

            string current = NormalizePoseMotionStrength(currentStrength);
            if (current == PoseMotionStrengthUnknown)
                return false;

            return string.Equals(saved, current, StringComparison.Ordinal);
        }

        private Transform GetPosePresetRootTransform()
        {
            if (_runtime.TargetFemaleCha == null)
                return null;

            if (_runtime.TargetFemaleCha.animBody != null)
                return _runtime.TargetFemaleCha.animBody.transform;

            return _runtime.TargetFemaleCha.transform;
        }

        private static string BuildRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null)
                return null;
            if (ReferenceEquals(root, target))
                return string.Empty;

            var names = new List<string>();
            Transform cur = target;
            while (cur != null && !ReferenceEquals(cur, root))
            {
                names.Add(cur.name);
                cur = cur.parent;
            }
            if (!ReferenceEquals(cur, root))
                return null;

            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static Transform FindByRelativePath(Transform root, string path)
        {
            if (root == null)
                return null;
            if (string.IsNullOrEmpty(path))
                return root;

            string[] parts = path.Split('/');
            Transform cur = root;
            for (int i = 0; i < parts.Length; i++)
            {
                cur = FindDirectChildByName(cur, parts[i]);
                if (cur == null)
                    return null;
            }

            return cur;
        }

        private static Transform FindDirectChildByName(Transform parent, string childName)
        {
            if (parent == null || string.IsNullOrEmpty(childName))
                return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform c = parent.GetChild(i);
                if (string.Equals(c.name, childName, StringComparison.Ordinal))
                    return c;
            }

            return null;
        }
    }
}
