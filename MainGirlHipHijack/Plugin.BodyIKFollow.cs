using System;
using UnityEngine;
using VRGIN.Core;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        private const string FollowHmdTargetName = "HMD";

        private static bool CanUseBoneFollow(int idx)
        {
            return idx == BIK_LH || idx == BIK_RH || idx == BIK_LF || idx == BIK_RF;
        }

        private void UpdateFollowBones()
        {
            UpdateExternalFollowTargets();
            ApplyHeadBoneToHmdConversion();
            UpdateFollowDistanceThreshold();

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (!_bikEff[i].Running)
                    continue;
                if (_bikEff[i].FollowBone == null)
                    continue;
                if (_bikEff[i].GizmoDragging)
                    continue;
                if (_bikEff[i].Proxy == null)
                    continue;
                if (_bikEff[i].HasPostDragHold && _bikEff[i].PostDragHoldFrames > 0)
                    continue;

                // 距離閾値ウェイトでソルバーウェイトを直接制御
                // プロキシは常に追従先を追い続ける（ウェイト0でもスキップしない）
                // ソルバーウェイトだけでアニメポーズ↔追従位置のブレンドを制御する
                if (_settings != null && _settings.FollowDistanceThresholdEnabled)
                {
                    float distWeight = _bikEff[i].FollowDistanceWeight;
                    float baseWeight = GetBodyIKWeight(i);
                    float effectiveWeight = baseWeight * distWeight;
                    ApplyBodyIKWeightDirect(i, effectiveWeight);
                }

                // オフセット回転の決定: HMD→HMD回転、ボーン→キャラルート回転
                Quaternion offsetRot = GetFollowOffsetRotation(_bikEff[i].FollowBone);
                Vector3 targetPos = _bikEff[i].FollowBone.position
                    + (offsetRot * _bikEff[i].FollowBonePositionOffset);

                if (_bikEff[i].IsBendGoal)
                    SetBendGoalProxyByDirection(i, targetPos);
                else
                    _bikEff[i].Proxy.position = targetPos;

                if (IsRotationDrivenEffector(i))
                    _bikEff[i].Proxy.rotation = _bikEff[i].FollowBone.rotation * _bikEff[i].FollowBoneRotationOffset;
            }
        }

        private void ClearBodyIKFollowBone(int idx)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return;

            _bikEff[idx].FollowBone = null;
            _bikEff[idx].FollowBonePositionOffset = Vector3.zero;
            _bikEff[idx].FollowBoneRotationOffset = Quaternion.identity;
            _bikEff[idx].CandidateBone = null;
        }

        private bool TrySetNearestFollowBone(int idx)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return false;
            if (!CanUseBoneFollow(idx))
                return false;
            if (!_bikEff[idx].Running || _bikEff[idx].Proxy == null)
                return false;

            Transform bone = _bikEff[idx].CandidateBone;
            if (bone == null)
                bone = FindNearestBone(idx, _bikEff[idx].Proxy.position);
            if (bone == null)
            {
                LogWarn("follow bone not found idx=" + idx + " snapDist=" + _settings.FollowSnapDistance.ToString("F3"));
                return false;
            }

            _bikEff[idx].FollowBone = bone;
            _bikEff[idx].CandidateBone = null;
            Quaternion offsetRot = GetFollowOffsetRotation(bone);
            _bikEff[idx].FollowBonePositionOffset =
                Quaternion.Inverse(offsetRot) * (_bikEff[idx].Proxy.position - bone.position);
            if (IsRotationDrivenEffector(idx))
            {
                _bikEff[idx].FollowBoneRotationOffset =
                    Quaternion.Inverse(bone.rotation) * _bikEff[idx].Proxy.rotation;
            }
            else
            {
                _bikEff[idx].FollowBoneRotationOffset = Quaternion.identity;
            }

            string cacheType = "UNKNOWN";
            if (_runtime.MaleBoneCache != null && IsBoneInCache(_runtime.MaleBoneCache, bone))
                cacheType = "MALE";
            else if (_runtime.BoneCache != null && IsBoneInCache(_runtime.BoneCache, bone))
                cacheType = "FEMALE";
            var po = _bikEff[idx].FollowBonePositionOffset;
            var ro = _bikEff[idx].FollowBoneRotationOffset;
            LogInfo($"follow bone set idx={idx} bone={bone.name} cache={cacheType} offsetRot=({offsetRot.eulerAngles.x:F3},{offsetRot.eulerAngles.y:F3},{offsetRot.eulerAngles.z:F3}) posOffset=({po.x:F4},{po.y:F4},{po.z:F4}) rotOffset=({ro.x:F4},{ro.y:F4},{ro.z:F4},{ro.w:F4}) bonePos=({bone.position.x:F4},{bone.position.y:F4},{bone.position.z:F4}) proxyPos=({_bikEff[idx].Proxy.position.x:F4},{_bikEff[idx].Proxy.position.y:F4},{_bikEff[idx].Proxy.position.z:F4})");
            return true;
        }

        private Transform FindNearestBone(int ikIdx, Vector3 pos)
        {
            if (!CanUseBoneFollow(ikIdx))
                return null;

            float bestDist = Mathf.Max(0.02f, _settings.FollowSnapDistance);
            Transform best = null;

            FindNearestBoneInCache(ikIdx, pos, _runtime.BoneCache, ref best, ref bestDist);
            FindNearestBoneInCache(ikIdx, pos, EnsureMaleBoneCacheForFollow(), ref best, ref bestDist);

            Transform hmd = GetOrCreateHmdFollowTarget();
            if (hmd != null)
            {
                float d = Vector3.Distance(hmd.position, pos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = hmd;
                }
            }

            return best;
        }

        private void FindNearestBoneInCache(int ikIdx, Vector3 pos, Transform[] cache, ref Transform best, ref float bestDist)
        {
            if (cache == null || cache.Length == 0)
                return;

            for (int i = 0; i < cache.Length; i++)
            {
                Transform t = cache[i];
                if (!CanSnapToBone(ikIdx, t))
                    continue;

                float d = Vector3.Distance(t.position, pos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = t;
                }
            }
        }

        private bool CanSnapToBone(int ikIdx, Transform bone)
        {
            if (bone == null)
                return false;

            string boneName = bone.name ?? string.Empty;
            bool isCheekSub = boneName.StartsWith("cf_J_CheekLow_s_", StringComparison.Ordinal);
            bool isCf = boneName.StartsWith("cf_j_", StringComparison.Ordinal);
            bool isCm = boneName.StartsWith("cm_j_", StringComparison.Ordinal);
            if (!isCf && !isCm && !isCheekSub)
                return false;
            if (IsFingerBoneName(boneName))
                return false;
            if (IsExcludedBoneName(boneName))
                return false;

            Transform lowerBoundary = GetLowerBoundaryBone(ikIdx);
            if (lowerBoundary != null && IsSameOrDescendantOf(bone, lowerBoundary))
                return false;

            switch (ikIdx)
            {
                case BIK_LH: return !IsLeftArmLowerBoneName(boneName);
                case BIK_RH: return !IsRightArmLowerBoneName(boneName);
                case BIK_LF: return !IsLeftLegLowerBoneName(boneName);
                case BIK_RF: return !IsRightLegLowerBoneName(boneName);
                default: return false;
            }
        }

        private Transform[] EnsureMaleBoneCacheForFollow()
        {
            if (_runtime.HSceneProc == null)
                return null;

            if (_runtime.TargetMaleCha != null && !_runtime.TargetMaleCha)
            {
                _runtime.TargetMaleCha = null;
                _runtime.MaleBoneCache = null;
            }

            if (_runtime.TargetMaleCha == null)
            {
                _runtime.TargetMaleCha = ResolveMainMale(_runtime.HSceneProc);
                if (_runtime.TargetMaleCha != null)
                    _runtime.MaleBoneCache = null;
            }

            if (_runtime.TargetMaleCha == null)
                return null;

            if (_runtime.MaleBoneCache == null || _runtime.MaleBoneCache.Length == 0)
            {
                Transform root = _runtime.TargetMaleCha.objBodyBone != null
                    ? _runtime.TargetMaleCha.objBodyBone.transform
                    : _runtime.TargetMaleCha.transform;
                _runtime.MaleBoneCache = root != null ? root.GetComponentsInChildren<Transform>(true) : null;
            }

            return _runtime.MaleBoneCache;
        }

        private void UpdateExternalFollowTargets()
        {
            if (!VR.Active)
                return;

            GetOrCreateHmdFollowTarget();
        }

        private Transform GetOrCreateHmdFollowTarget()
        {
            if (!VR.Active)
                return null;

            uint hmdIdx = GetHMDDeviceIndex();
            if (!TryGetDevicePose(hmdIdx, out Vector3 hmdPos, out Quaternion hmdRot))
                return null;

            if (_runtime.FollowHmdTargetGo == null)
            {
                _runtime.FollowHmdTargetGo = new GameObject(FollowHmdTargetName);
                _runtime.FollowHmdTargetGo.hideFlags = HideFlags.HideAndDontSave;
                _runtime.FollowHmdTarget = _runtime.FollowHmdTargetGo.transform;
            }

            _runtime.FollowHmdTarget.SetPositionAndRotation(hmdPos, hmdRot);
            return _runtime.FollowHmdTarget;
        }

        private void ClearExternalFollowTargets()
        {
            if (_runtime.FollowHmdTargetGo != null)
                Destroy(_runtime.FollowHmdTargetGo);
            _runtime.FollowHmdTargetGo = null;
            _runtime.FollowHmdTarget = null;
        }

        private Transform GetLowerBoundaryBone(int ikIdx)
        {
            if (_runtime.Fbbik == null || _runtime.Fbbik.references == null)
                return null;

            var refs = _runtime.Fbbik.references;
            switch (ikIdx)
            {
                case BIK_LH: return refs.leftForearm != null ? refs.leftForearm : refs.leftHand;
                case BIK_RH: return refs.rightForearm != null ? refs.rightForearm : refs.rightHand;
                case BIK_LF: return refs.leftCalf != null ? refs.leftCalf : refs.leftFoot;
                case BIK_RF: return refs.rightCalf != null ? refs.rightCalf : refs.rightFoot;
                default: return null;
            }
        }

        private static bool IsSameOrDescendantOf(Transform candidate, Transform ancestor)
        {
            if (candidate == null || ancestor == null)
                return false;

            Transform t = candidate;
            while (t != null)
            {
                if (ReferenceEquals(t, ancestor))
                    return true;
                t = t.parent;
            }

            return false;
        }

        private static bool IsFingerBoneName(string boneName)
        {
            string n = (boneName ?? string.Empty).ToLowerInvariant();
            return n.Contains("finger")
                || n.Contains("thumb")
                || n.Contains("index")
                || n.Contains("middle")
                || n.Contains("ring")
                || n.Contains("little")
                || n.Contains("yubi")
                || n.Contains("tang")
                || n.Contains("toes");
        }

        private static bool IsExcludedBoneName(string boneName)
        {
            string n = (boneName ?? string.Empty).ToLowerInvariant();
            if (n.Contains("cf_pv_"))
                return true;
            if (n.Contains("cf_j_sk_"))
                return true;
            if (n.Contains("cf_j_backsk_"))
                return true;
            if (n.Contains("cf_j_spinesk_"))
                return true;
            if (n.Contains("cf_j_bnip"))
                return true;
            if (n == "cf_j_root")
                return true;
            if (n == "cf_j_ana")
                return true;
            // _L_01 / _R_01 など、左右サフィックスの後に数字がつくサブボーンを除外
            if (System.Text.RegularExpressions.Regex.IsMatch(n, @"_[lr]_\d+$"))
                return true;
            return false;
        }

        private static bool IsLeftArmLowerBoneName(string boneName)
        {
            return IsLeftSideBoneName(boneName) && IsArmLowerBoneNameCommon(boneName);
        }

        private static bool IsRightArmLowerBoneName(string boneName)
        {
            return IsRightSideBoneName(boneName) && IsArmLowerBoneNameCommon(boneName);
        }

        private static bool IsLeftLegLowerBoneName(string boneName)
        {
            return IsLeftSideBoneName(boneName) && IsLegLowerBoneNameCommon(boneName);
        }

        private static bool IsRightLegLowerBoneName(string boneName)
        {
            return IsRightSideBoneName(boneName) && IsLegLowerBoneNameCommon(boneName);
        }

        private static bool IsArmLowerBoneNameCommon(string boneName)
        {
            string n = (boneName ?? string.Empty).ToLowerInvariant();
            return n.Contains("arm") || n.Contains("elbo") || n.Contains("elbow")
                || n.Contains("forearm") || n.Contains("wrist") || n.Contains("hand");
        }

        private static bool IsLegLowerBoneNameCommon(string boneName)
        {
            string n = (boneName ?? string.Empty).ToLowerInvariant();
            return n.Contains("knee") || n.Contains("leg") || n.Contains("ankle")
                || n.Contains("foot") || n.Contains("toe");
        }

        private static bool IsLeftSideBoneName(string boneName)
        {
            string u = (boneName ?? string.Empty).ToUpperInvariant();
            return u.EndsWith("_L", StringComparison.Ordinal) || u.Contains("_L_");
        }

        private static bool IsRightSideBoneName(string boneName)
        {
            string u = (boneName ?? string.Empty).ToUpperInvariant();
            return u.EndsWith("_R", StringComparison.Ordinal) || u.Contains("_R_");
        }

        private void UpdateFollowBoneVisuals()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                BIKEffectorState state = _bikEff[i];
                if (state == null)
                    continue;

                // ドラッグ中かつ追従未確定時のみ候補ボーン検索
                if (state.Running && CanUseBoneFollow(i) && state.Proxy != null && state.GizmoDragging && state.FollowBone == null)
                    state.CandidateBone = FindNearestBone(i, state.Proxy.position);
                else if (state.FollowBone != null)
                    state.CandidateBone = null;

                // 表示対象ボーン: 追従確定中はFollowBoneを優先、未確定時はCandidateBone
                Transform displayBone = state.FollowBone ?? state.CandidateBone;
                bool shouldShow = state.Running && displayBone != null && (IsGizmoVisible(i) || _vrGrabMode);

                UpdateBoneMarker(i, state, displayBone, shouldShow);
                UpdateFollowLine(i, state, displayBone, shouldShow);

                // ギズモ中央球の色: 追従確定中はシアン
                if (state.Gizmo != null)
                    state.Gizmo.SetFollowActive(state.FollowBone != null);
            }
        }

        private void UpdateBoneMarker(int idx, BIKEffectorState state, Transform displayBone, bool shouldShow)
        {
            float markerSize = _settings != null ? _settings.BoneMarkerSize : 0.04f;

            if (shouldShow)
            {
                if (state.BoneMarkerGo == null)
                {
                    state.BoneMarkerGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    state.BoneMarkerGo.name = "__BoneMarker_" + idx;
                    state.BoneMarkerGo.hideFlags = HideFlags.HideAndDontSave;
                    Destroy(state.BoneMarkerGo.GetComponent<Collider>());
                    var mr = state.BoneMarkerGo.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        var mat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
                        mat.color = new Color(0.2f, 0.4f, 1f);
                        mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                        mr.sharedMaterial = mat;
                    }
                    state.BoneMarkerGo.layer = VR.Active ? 0 : 31;
                }

                state.BoneMarkerGo.SetActive(true);
                state.BoneMarkerGo.transform.position = displayBone.position;
                state.BoneMarkerGo.transform.localScale = Vector3.one * markerSize;
            }
            else
            {
                if (state.BoneMarkerGo != null)
                    state.BoneMarkerGo.SetActive(false);
            }
        }

        private void UpdateFollowLine(int idx, BIKEffectorState state, Transform displayBone, bool shouldShow)
        {
            if (shouldShow && state.Proxy != null)
            {
                if (state.FollowLine == null)
                {
                    var go = new GameObject("__FollowLine_" + idx);
                    go.hideFlags = HideFlags.HideAndDontSave;
                    go.layer = VR.Active ? 0 : 31;
                    state.FollowLine = go.AddComponent<LineRenderer>();
                    state.FollowLine.useWorldSpace = true;
                    state.FollowLine.positionCount = 2;
                    state.FollowLine.startWidth = 0.008f;
                    state.FollowLine.endWidth = 0.008f;
                    state.FollowLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    state.FollowLine.receiveShadows = false;
                    var mat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
                    if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", new Color(0.2f, 0.4f, 1f, 0.8f));
                    state.FollowLine.sharedMaterial = mat;
                    state.FollowLine.startColor = new Color(0.2f, 0.4f, 1f, 0.8f);
                    state.FollowLine.endColor = new Color(0.2f, 0.4f, 1f, 0.8f);
                }

                state.FollowLine.gameObject.SetActive(true);
                state.FollowLine.SetPosition(0, state.Proxy.position);
                state.FollowLine.SetPosition(1, displayBone.position);
            }
            else
            {
                if (state.FollowLine != null)
                    state.FollowLine.gameObject.SetActive(false);
            }
        }

        internal void DestroyFollowVisuals(int idx)
        {
            BIKEffectorState state = _bikEff[idx];
            if (state == null) return;

            if (state.BoneMarkerGo != null)
            {
                var mr = state.BoneMarkerGo.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial != null)
                {
                    if (mr.sharedMaterial.mainTexture != null)
                        Destroy(mr.sharedMaterial.mainTexture);
                    Destroy(mr.sharedMaterial);
                }
                Destroy(state.BoneMarkerGo);
                state.BoneMarkerGo = null;
            }

            if (state.FollowLine != null)
            {
                if (state.FollowLine.sharedMaterial != null)
                    Destroy(state.FollowLine.sharedMaterial);
                Destroy(state.FollowLine.gameObject);
                state.FollowLine = null;
            }

            state.CandidateBone = null;
        }

        private void DrawBodyIkFollowSection()
        {
            DrawFollowExtensionsSection();

            GUILayout.Space(4f);
            GUILayout.Label("── IK追従設定 ──");

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("追従スナップ距離", "近傍追従でボーンにスナップする距離閾値（メートル）"), GUILayout.Width(100f));
            float snapDist = _settings.FollowSnapDistance;
            float nextSnapDist = GUILayout.HorizontalSlider(snapDist, 0.02f, 0.6f, GUILayout.Width(160f));
            GUILayout.Label(nextSnapDist.ToString("F2"), GUILayout.Width(40f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(snapDist, nextSnapDist))
            {
                _settings.FollowSnapDistance = nextSnapDist;
                SaveSettings();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("ボーンマーカーサイズ", "追従先ボーンを示すキューブマーカーのサイズ"), GUILayout.Width(120f));
            float markerSize = _settings.BoneMarkerSize;
            float nextMarkerSize = GUILayout.HorizontalSlider(markerSize, 0.01f, 0.15f, GUILayout.Width(160f));
            GUILayout.Label(nextMarkerSize.ToString("F2"), GUILayout.Width(40f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(markerSize, nextMarkerSize))
            {
                _settings.BoneMarkerSize = nextMarkerSize;
                SaveSettings();
            }

        }

        private void DrawBodyIkFollowRow(int idx)
        {
            if (!CanUseBoneFollow(idx))
                return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(16f);

            bool prevEnabled = GUI.enabled;
            GUI.enabled = _bikEff[idx].Running;
            if (GUILayout.Button(new GUIContent("近傍追従", "最も近い追従先（女/男ボーン + HMD）を自動検索してIKエフェクターを追従させる"), GUILayout.Width(72f)))
                TrySetNearestFollowBone(idx);

            GUI.enabled = _bikEff[idx].FollowBone != null;
            if (GUILayout.Button(new GUIContent("解除", "ボーン追従を解除してIKエフェクターを自由に動かせる状態に戻す"), GUILayout.Width(52f)))
                ClearBodyIKFollowBone(idx);

            // ミラーボタン（左手⇔右手、左足⇔右足のみ）
            if (GetMirrorIndex(idx) >= 0)
            {
                GUI.enabled = _bikEff[idx].FollowBone != null;
                if (GUILayout.Button(new GUIContent("ミラー", "現在のオフセットを反対側にコピー"), GUILayout.Width(52f)))
                    ApplyMirrorFollow(idx);
            }
            GUI.enabled = prevEnabled;

            string followName = _bikEff[idx].FollowBone != null ? _bikEff[idx].FollowBone.name : "-";
            GUILayout.Label(followName);
            GUILayout.EndHorizontal();
        }

        // ── オフセット回転決定 ─────────────────────────────────────────────

        private Quaternion GetFollowOffsetRotation(Transform followBone)
        {
            if (followBone == null)
                return Quaternion.identity;

            string name = followBone.name ?? string.Empty;

            // HMD追従時 → HMD自身の回転を使う
            if (name == FollowHmdTargetName)
            {
                LogDebug($"[FollowOffsetRot] HMD bone={name} rot={followBone.rotation.eulerAngles}");
                return followBone.rotation;
            }

            // ボーン追従時 → そのボーンが属するキャラのルート回転を使う
            // 男ボーンか判定
            if (_runtime.TargetMaleCha != null && _runtime.MaleBoneCache != null
                && IsBoneInCache(_runtime.MaleBoneCache, followBone))
            {
                Quaternion rot = _runtime.TargetMaleCha.transform.rotation;
                LogDebug($"[FollowOffsetRot] MALE bone={name} rootRot={rot.eulerAngles}");
                return rot;
            }

            // 女ボーンか判定（BoneCacheに存在するか確認）
            if (_runtime.TargetFemaleCha != null && _runtime.BoneCache != null
                && IsBoneInCache(_runtime.BoneCache, followBone))
            {
                Quaternion rot = _runtime.TargetFemaleCha.transform.rotation;
                LogDebug($"[FollowOffsetRot] FEMALE bone={name} rootRot={rot.eulerAngles}");
                return rot;
            }

            // フォールバック: ボーン自身の回転
            LogWarn($"[FollowOffsetRot] FALLBACK bone={name} — not in male or female cache, boneRot={followBone.rotation.eulerAngles}");
            return followBone.rotation;
        }

        // ── 追従拡張設定 UI ──────────────────────────────────────────────

        private void DrawFollowExtensionsSection()
        {
            GUILayout.Space(4f);
            GUILayout.Label("── 追従拡張 ──");

            // 距離閾値
            {
                bool distEnabled = GUILayout.Toggle(_settings.FollowDistanceThresholdEnabled,
                    new GUIContent("距離閾値で自動切断/復帰", "追従先が閾値距離を超えたら滑らかに切断、戻ったら復帰"));
                if (distEnabled != _settings.FollowDistanceThresholdEnabled)
                {
                    _settings.FollowDistanceThresholdEnabled = distEnabled;
                    SaveSettings();
                }

                if (_settings.FollowDistanceThresholdEnabled)
                {
                    float threshold = DrawSliderWithField("切断距離", _settings.FollowDistanceThreshold, 0.05f, 10f, "F2",
                        tooltip: "追従先がこの距離を超えたら切断（メートル）");
                    if (!Mathf.Approximately(threshold, _settings.FollowDistanceThreshold))
                    {
                        _settings.FollowDistanceThreshold = threshold;
                        SaveSettings();
                    }

                    float blendSpeed = DrawSliderWithField("ブレンド速度", _settings.FollowDistanceBlendSpeed, 0.5f, 20f, "F1",
                        tooltip: "切断/復帰時のブレンド速度（大きいほど速い）");
                    if (!Mathf.Approximately(blendSpeed, _settings.FollowDistanceBlendSpeed))
                    {
                        _settings.FollowDistanceBlendSpeed = blendSpeed;
                        SaveSettings();
                    }
                }
            }

            // VR時 頭ボーン→HMD変換
            {
                bool headToHmd = GUILayout.Toggle(_settings.FollowHeadBoneToHmdEnabled,
                    new GUIContent("VR時 頭ボーン→HMD変換", "追従先が男の頭ボーンの時、VRではHMD位置に自動差し替え"));
                if (headToHmd != _settings.FollowHeadBoneToHmdEnabled)
                {
                    _settings.FollowHeadBoneToHmdEnabled = headToHmd;
                    SaveSettings();
                }
            }
        }

        // ── 女の頭角度 UI ────────────────────────────────────────────────

        private void DrawFemaleHeadAngleSection()
        {
            GUILayout.Space(4f);
            GUILayout.Label("── 女の頭角度 ──");

            {
                bool enabled = GUILayout.Toggle(_settings.FemaleHeadAngleEnabled,
                    new GUIContent("頭角度スライダー有効", "デスクトップで女キャラの頭角度をスライダーで操作する"));
                if (enabled != _settings.FemaleHeadAngleEnabled)
                {
                    _settings.FemaleHeadAngleEnabled = enabled;
                    SaveSettings();
                }
            }

            if (_settings.FemaleHeadAngleEnabled)
            {
                float ax = DrawSliderWithField("X (上下)", _settings.FemaleHeadAngleX, -60f, 60f, "F1");
                if (!Mathf.Approximately(ax, _settings.FemaleHeadAngleX))
                {
                    _settings.FemaleHeadAngleX = ax;
                    SaveSettings();
                }

                float ay = DrawSliderWithField("Y (左右)", _settings.FemaleHeadAngleY, -60f, 60f, "F1");
                if (!Mathf.Approximately(ay, _settings.FemaleHeadAngleY))
                {
                    _settings.FemaleHeadAngleY = ay;
                    SaveSettings();
                }

                float az = DrawSliderWithField("Z (傾き)", _settings.FemaleHeadAngleZ, -60f, 60f, "F1");
                if (!Mathf.Approximately(az, _settings.FemaleHeadAngleZ))
                {
                    _settings.FemaleHeadAngleZ = az;
                    SaveSettings();
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("リセット", GUILayout.Width(72f)))
                {
                    _settings.FemaleHeadAngleX = 0f;
                    _settings.FemaleHeadAngleY = 0f;
                    _settings.FemaleHeadAngleZ = 0f;
                    SaveSettings();
                }

                bool gizmo = GUILayout.Toggle(_settings.FemaleHeadAngleGizmoVisible,
                    new GUIContent("ギズモ表示", "頭の回転ギズモを表示"));
                if (gizmo != _settings.FemaleHeadAngleGizmoVisible)
                {
                    _settings.FemaleHeadAngleGizmoVisible = gizmo;
                    SaveSettings();
                }
                GUILayout.EndHorizontal();

                bool keepOnContextChange = GUILayout.Toggle(
                    _settings.FemaleHeadAngleKeepOnMotionOrPostureChange,
                    new GUIContent("体位/モーション変更後も維持", "OFF時は体位やモーションの変化で頭角度をリセット"));
                if (keepOnContextChange != _settings.FemaleHeadAngleKeepOnMotionOrPostureChange)
                {
                    _settings.FemaleHeadAngleKeepOnMotionOrPostureChange = keepOnContextChange;
                    SaveSettings();
                }
            }
        }

        // ── ミラーロジック ──────────────────────────────────────────

        private void ApplyMirrorFollow(int sourceIdx)
        {
            if (_settings == null)
                return;

            int mirrorIdx = GetMirrorIndex(sourceIdx);
            if (mirrorIdx < 0)
                return;

            Transform sourceBone = _bikEff[sourceIdx].FollowBone;
            if (sourceBone == null)
                return;

            // IKが有効でなければ有効化
            if (!_bikEff[mirrorIdx].Running)
            {
                SetBodyIK(mirrorIdx, on: true, saveSettings: false, reason: "mirror-auto");
            }

            string boneName = sourceBone.name ?? string.Empty;
            Transform mirrorBone = FindMirrorBone(boneName, sourceBone);

            // 位置オフセットX反転（左右対称）
            var posOff = _bikEff[sourceIdx].FollowBonePositionOffset;
            posOff.x = -posOff.x;

            // 回転オフセットの鏡像化（Y,Z成分を反転 = X軸ミラー）
            var rotOff = _bikEff[sourceIdx].FollowBoneRotationOffset;
            rotOff.y = -rotOff.y;
            rotOff.z = -rotOff.z;

            if (mirrorBone != null)
            {
                // L/R対称ボーンがある → 反対側ボーンを追従先にセット
                _bikEff[mirrorIdx].FollowBone = mirrorBone;
                _bikEff[mirrorIdx].CandidateBone = null;
                _bikEff[mirrorIdx].FollowBonePositionOffset = posOff;
                _bikEff[mirrorIdx].FollowBoneRotationOffset = rotOff;
            }
            else
            {
                // 対称ボーンなし（頭など）→ 同じボーンでオフセットX反転
                _bikEff[mirrorIdx].FollowBone = sourceBone;
                _bikEff[mirrorIdx].CandidateBone = null;
                _bikEff[mirrorIdx].FollowBonePositionOffset = posOff;
                _bikEff[mirrorIdx].FollowBoneRotationOffset = rotOff;
            }

            string mirrorCacheType = "UNKNOWN";
            if (_runtime.MaleBoneCache != null && IsBoneInCache(_runtime.MaleBoneCache, _bikEff[mirrorIdx].FollowBone))
                mirrorCacheType = "MALE";
            else if (_runtime.BoneCache != null && IsBoneInCache(_runtime.BoneCache, _bikEff[mirrorIdx].FollowBone))
                mirrorCacheType = "FEMALE";
            var mpo = _bikEff[mirrorIdx].FollowBonePositionOffset;
            var mro = _bikEff[mirrorIdx].FollowBoneRotationOffset;
            var spo = _bikEff[sourceIdx].FollowBonePositionOffset;
            var sro = _bikEff[sourceIdx].FollowBoneRotationOffset;
            var mbp = _bikEff[mirrorIdx].FollowBone.position;
            LogInfo($"[Mirror] {sourceIdx} -> {mirrorIdx} bone={_bikEff[mirrorIdx].FollowBone.name} cache={mirrorCacheType} posOffset=({mpo.x:F4},{mpo.y:F4},{mpo.z:F4}) srcPosOffset=({spo.x:F4},{spo.y:F4},{spo.z:F4}) rotQ=({mro.x:F4},{mro.y:F4},{mro.z:F4},{mro.w:F4}) srcRotQ=({sro.x:F4},{sro.y:F4},{sro.z:F4},{sro.w:F4}) mirrorBonePos=({mbp.x:F4},{mbp.y:F4},{mbp.z:F4})");
        }

        private static int GetMirrorIndex(int idx)
        {
            switch (idx)
            {
                case BIK_LH: return BIK_RH;
                case BIK_RH: return BIK_LH;
                case BIK_LF: return BIK_RF;
                case BIK_RF: return BIK_LF;
                default: return -1;
            }
        }

        private Transform FindMirrorBone(string boneName, Transform sourceBone)
        {
            if (string.IsNullOrEmpty(boneName))
                return null;

            string mirrorName = null;
            if (boneName.Contains("_L"))
                mirrorName = boneName.Replace("_L", "_R");
            else if (boneName.Contains("_R"))
                mirrorName = boneName.Replace("_R", "_L");

            if (mirrorName == null || mirrorName == boneName)
                return null;

            // ソースボーンがどちらのキャッシュに属するか判定し、同じキャッシュから検索
            Transform[] sourceCache = DetermineBoneCache(sourceBone);
            if (sourceCache != null)
                return FindBoneByName(sourceCache, mirrorName);

            // フォールバック: 両方検索
            Transform found = FindBoneByName(_runtime.BoneCache, mirrorName);
            if (found != null) return found;
            return FindBoneByName(_runtime.MaleBoneCache, mirrorName);
        }

        private Transform[] DetermineBoneCache(Transform bone)
        {
            if (bone == null) return null;

            if (_runtime.MaleBoneCache != null && IsBoneInCache(_runtime.MaleBoneCache, bone))
                return _runtime.MaleBoneCache;
            if (_runtime.BoneCache != null && IsBoneInCache(_runtime.BoneCache, bone))
                return _runtime.BoneCache;

            return null;
        }

        private static bool IsBoneInCache(Transform[] cache, Transform bone)
        {
            for (int i = 0; i < cache.Length; i++)
            {
                if (ReferenceEquals(cache[i], bone))
                    return true;
            }
            return false;
        }

        private static Transform FindBoneByName(Transform[] cache, string name)
        {
            if (cache == null) return null;
            for (int i = 0; i < cache.Length; i++)
            {
                if (cache[i] != null && cache[i].name == name)
                    return cache[i];
            }
            return null;
        }

        // ── 距離閾値ロジック ─────────────────────────────────────────────

        private void UpdateFollowDistanceThreshold()
        {
            if (_settings == null || !_settings.FollowDistanceThresholdEnabled)
                return;

            Transform femaleHead = _femaleHeadBoneCached;
            if (femaleHead == null && _runtime.BoneCache != null)
                femaleHead = FindBoneInCache(_runtime.BoneCache, "cf_j_head");
            if (femaleHead == null)
                return;

            float dt = Time.unscaledDeltaTime;
            float threshold = Mathf.Max(0.05f, _settings.FollowDistanceThreshold);
            float speed = Mathf.Max(0.5f, _settings.FollowDistanceBlendSpeed);

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (!_bikEff[i].Running || _bikEff[i].FollowBone == null)
                    continue;

                // 女の頭から追従先ボーンへのベクトル
                Vector3 headToTarget = _bikEff[i].FollowBone.position - femaleHead.position;
                float dist = headToTarget.magnitude;

                if (dist > threshold)
                {
                    // 閾値超過 → ウェイトを徐々に下げる
                    // プリセット遷移中なら距離トリガー優先で中止
                    if (_bikEff[i].FollowDistanceWeight > 0.99f)
                        StopPoseTransitionIfRunning();
                    float target = 0f;
                    float current = _bikEff[i].FollowDistanceWeight;
                    _bikEff[i].FollowDistanceWeight = Mathf.MoveTowards(current, target, speed * dt);
                }
                else
                {
                    // 閾値内 → ウェイトを徐々に戻す
                    float target = 1f;
                    float current = _bikEff[i].FollowDistanceWeight;
                    _bikEff[i].FollowDistanceWeight = Mathf.MoveTowards(current, target, speed * dt);
                }
            }
        }

        // ── VR時 頭ボーン→HMD変換 ───────────────────────────────────────

        private void ApplyHeadBoneToHmdConversion()
        {
            if (_settings == null || !_settings.FollowHeadBoneToHmdEnabled)
                return;
            if (!VR.Active)
                return;

            Transform hmd = GetOrCreateHmdFollowTarget();
            if (hmd == null)
                return;

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (!_bikEff[i].Running || _bikEff[i].FollowBone == null)
                    continue;

                string name = _bikEff[i].FollowBone.name ?? string.Empty;
                if (name == "cm_j_head" || name == "cf_j_head")
                {
                    _bikEff[i].FollowBone = hmd;
                }
            }
        }
    }
}
