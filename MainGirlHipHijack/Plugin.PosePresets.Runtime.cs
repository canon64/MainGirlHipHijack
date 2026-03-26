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
            // 追従ボーンローカル空間での遷移
            public bool UseFollowLocalTransition;
            public Vector3 StartFollowPosOffset;
            public Quaternion StartFollowRotOffset;
            public Vector3 TargetFollowPosOffset;
            public Quaternion TargetFollowRotOffset;
            // LateUpdate相でキャッシュされた追従ボーン位置
            public bool HasFollowBoneLateCache;
            public Vector3 FollowBoneLateCachePos;
            public Quaternion FollowBoneLateCacheRot;
            // 開始オフセット未確定フラグ（LateUpdateで確定する）
            public bool PendingStartOffsetCalc;
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
                hasFemaleHeadAngle = _settings != null && _settings.FemaleHeadAngleEnabled,
                femaleHeadAngleX = _settings != null ? _settings.FemaleHeadAngleX : 0f,
                femaleHeadAngleY = _settings != null ? _settings.FemaleHeadAngleY : 0f,
                femaleHeadAngleZ = _settings != null ? _settings.FemaleHeadAngleZ : 0f,
                entries = new PosePresetEntryRuntime[BIK_TOTAL]
            };
            Transform femaleRoot = GetPosePresetRootTransform();
            Transform maleRoot = GetMalePosePresetRootTransform();

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
                if (CanUseBoneFollow(i) && _bikEff[i].FollowBone != null)
                {
                    // 男ボーンか女ボーンかを判定してルートを選択
                    bool isMale = _runtime.MaleBoneCache != null
                        && IsBoneInCache(_runtime.MaleBoneCache, _bikEff[i].FollowBone);
                    Transform root = isMale ? maleRoot : femaleRoot;
                    string path = root != null ? BuildRelativePath(root, _bikEff[i].FollowBone) : null;
                    if (path != null)
                    {
                        entry.hasFollowBone = true;
                        entry.followIsMale = isMale;
                        entry.followBonePath = path;
                        entry.followPositionOffset = _bikEff[i].FollowBonePositionOffset;
                        entry.followRotationOffset = _bikEff[i].FollowBoneRotationOffset;
                    }
                    else
                    {
                        LogWarn("[PosePreset] follow save skipped idx=" + i + " bone=" + _bikEff[i].FollowBone.name
                            + " isMale=" + isMale + " reason=path-outside-root");
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
            Transform femaleFollowRoot = GetPosePresetRootTransform();
            Transform maleFollowRoot = GetMalePosePresetRootTransform();

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
                Transform followRoot = entry.followIsMale ? maleFollowRoot : femaleFollowRoot;
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

                // 追従ボーンがある場合はローカル空間ベースの遷移にする
                // 開始オフセットはUpdate相ではアニメ生値になるため、LateUpdateで確定する
                if (hasFollowBinding && followBone != null && _bikEff[i].Proxy != null)
                {
                    point.UseFollowLocalTransition = true;
                    point.PendingStartOffsetCalc = true;
                    point.TargetFollowPosOffset = followPosOffset;
                    point.TargetFollowRotOffset = followRotOffset;
                }

                transitionPoints.Add(point);
            }

            _autoPoseLastAppliedPresetId = preset.id;
            _autoPoseLoopCountSinceSwitch = 0;
            _autoPoseLoopReady = false;
            _poseTransitionPresetId = preset.id;

            bool hasFemaleHeadAngleTransition = _settings != null;
            Vector3 femaleHeadAngleStart = Vector3.zero;
            Vector3 femaleHeadAngleTarget = Vector3.zero;
            if (hasFemaleHeadAngleTransition)
            {
                femaleHeadAngleStart = new Vector3(
                    _settings.FemaleHeadAngleX,
                    _settings.FemaleHeadAngleY,
                    _settings.FemaleHeadAngleZ);
                femaleHeadAngleTarget = preset.hasFemaleHeadAngle
                    ? new Vector3(
                        Mathf.Clamp(preset.femaleHeadAngleX, -60f, 60f),
                        Mathf.Clamp(preset.femaleHeadAngleY, -60f, 60f),
                        Mathf.Clamp(preset.femaleHeadAngleZ, -60f, 60f))
                    : Vector3.zero;
            }
            else
            {
                SetFemaleHeadAngleFromPreset(preset);
            }
            SetFemaleHeadAdditiveFromPreset(preset);

            _activeTransitionPoints = transitionPoints;
            _poseTransitionCoroutine = StartCoroutine(ApplyPoseTransitionCoroutine(
                transitionPoints,
                transitionSeconds,
                preset.id,
                hasFemaleHeadAngleTransition,
                femaleHeadAngleStart,
                femaleHeadAngleTarget));

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

        private void SetFemaleHeadAngleFromPreset(PosePresetRuntime preset)
        {
            if (preset == null || _settings == null)
                return;

            if (!preset.hasFemaleHeadAngle)
            {
                SetFemaleHeadAngleValues(0f, 0f, 0f);
                return;
            }

            SetFemaleHeadAngleValues(preset.femaleHeadAngleX, preset.femaleHeadAngleY, preset.femaleHeadAngleZ);
        }

        private void SetFemaleHeadAngleValues(float x, float y, float z)
        {
            if (_settings == null)
                return;

            _settings.FemaleHeadAngleX = Mathf.Clamp(x, -60f, 60f);
            _settings.FemaleHeadAngleY = Mathf.Clamp(y, -60f, 60f);
            _settings.FemaleHeadAngleZ = Mathf.Clamp(z, -60f, 60f);
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

        private IEnumerator ApplyPoseTransitionCoroutine(
            List<PoseTransitionPoint> points,
            float seconds,
            string presetId,
            bool hasFemaleHeadAngleTransition,
            Vector3 femaleHeadAngleStart,
            Vector3 femaleHeadAngleTarget)
        {
            // LateUpdateで開始オフセットが確定するまで待つ（1フレーム）
            bool hasPending = false;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].PendingStartOffsetCalc)
                {
                    hasPending = true;
                    break;
                }
            }
            if (hasPending)
            {
                yield return null; // LateUpdateが走って開始オフセットが確定する
            }

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

                    if (point.UseFollowLocalTransition && point.HasFollowBoneLateCache)
                    {
                        // LateUpdateでキャッシュされた追従ボーン位置を使い、ローカル空間で補間
                        Vector3 bonePos = point.FollowBoneLateCachePos;
                        Quaternion boneRot = point.FollowBoneLateCacheRot;
                        Quaternion offsetRot = GetFollowOffsetRotation(point.FollowBone);

                        Vector3 localPos = Vector3.Lerp(point.StartFollowPosOffset, point.TargetFollowPosOffset, t);
                        Vector3 pos = bonePos + offsetRot * localPos;

                        Quaternion rot;
                        if (IsRotationDrivenEffector(point.Index))
                        {
                            Quaternion localRot = Quaternion.Slerp(point.StartFollowRotOffset, point.TargetFollowRotOffset, t);
                            rot = boneRot * localRot;
                        }
                        else
                        {
                            rot = Quaternion.Slerp(point.StartRot, point.TargetRot, t);
                        }

                        _bikEff[point.Index].Proxy.SetPositionAndRotation(pos, rot);
                    }
                    else
                    {
                        Vector3 pos = Vector3.Lerp(point.StartPos, point.TargetPos, t);
                        Quaternion rot = Quaternion.Slerp(point.StartRot, point.TargetRot, t);
                        _bikEff[point.Index].Proxy.SetPositionAndRotation(pos, rot);
                    }
                }

                if (hasFemaleHeadAngleTransition)
                {
                    Vector3 head = Vector3.Lerp(femaleHeadAngleStart, femaleHeadAngleTarget, t);
                    SetFemaleHeadAngleValues(head.x, head.y, head.z);
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

                if (point.UseFollowLocalTransition && point.HasFollowBoneLateCache)
                {
                    Vector3 bonePos = point.FollowBoneLateCachePos;
                    Quaternion boneRot = point.FollowBoneLateCacheRot;
                    Quaternion offsetRot = GetFollowOffsetRotation(point.FollowBone);
                    Vector3 pos = bonePos + offsetRot * point.TargetFollowPosOffset;
                    Quaternion rot = IsRotationDrivenEffector(point.Index)
                        ? boneRot * point.TargetFollowRotOffset
                        : _bikEff[point.Index].Proxy.rotation;
                    _bikEff[point.Index].Proxy.SetPositionAndRotation(pos, rot);
                }
                else
                {
                    _bikEff[point.Index].Proxy.SetPositionAndRotation(point.TargetPos, point.TargetRot);
                }
            }

            if (hasFemaleHeadAngleTransition)
                SetFemaleHeadAngleValues(femaleHeadAngleTarget.x, femaleHeadAngleTarget.y, femaleHeadAngleTarget.z);

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

                // Update相ではFollowBoneの位置がアニメ生値（IK前）なのでオフセット計算が狂う。
                // LateUpdate（IK適用後）に遅延して再バインドする。
                _bikEff[point.Index].PendingFollowRebind = true;
                _bikEff[point.Index].PendingFollowBone = point.FollowBone;
                _bikEff[point.Index].PendingFollowHasPresetOffset = true;
                _bikEff[point.Index].PendingFollowPosOffset = point.FollowPositionOffset;
                _bikEff[point.Index].PendingFollowRotOffset = point.FollowRotationOffset;
                _bikEff[point.Index].CandidateBone = null;
            }

            _poseTransitionCoroutine = null;
            _poseTransitionPresetId = null;
            _activeTransitionPoints = null;
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
            _activeTransitionPoints = null;
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

        private Transform GetMalePosePresetRootTransform()
        {
            // TargetMaleCha と MaleBoneCache を確実に初期化する
            // （骨スナップやMaleHMD以外のパスでも男ボーンを扱えるようにする）
            EnsureMaleBoneCacheForFollow();

            if (_runtime.TargetMaleCha == null)
                return null;

            if (_runtime.TargetMaleCha.animBody != null)
                return _runtime.TargetMaleCha.animBody.transform;

            return _runtime.TargetMaleCha.transform;
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
