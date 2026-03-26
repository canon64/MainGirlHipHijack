using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MainGameLogRelay;
using MainGameTransformGizmo;
using MainGameUiInputCapture;
using RootMotion.FinalIK;
using UnityEngine;
using VRGIN.Controls;
using VRGIN.Core;
using Valve.VR;

namespace MainGirlHipHijack
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    [BepInDependency(MainGameLogRelay.Plugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(MainGameTransformGizmo.Plugin.GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(MainGameUiInputCapture.Plugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.kks.main.girlbodyikgizmo";
        public const string PluginName = "MainGirlHipHijack";
        public const string Version = "1.0.0";
        private const string RelayOwner = Guid;
        private const string RelayLogKey = "main/" + PluginName;
        private const string InputCaptureRelayLogKey = "main/" + PluginName + ".input";
        private const string InputCaptureOwnerKey = Guid + ".input";
        private const string InputCaptureSourceGizmo = "gizmo-drag";
        private const string InputCaptureSourceWindow = "window-drag";
        private const string InputCaptureSourceSlider = "slider-drag";
        private const string InputCaptureSourceScroll = "scroll-drag";

        internal static Plugin Instance { get; private set; }

        internal const int BIK_LH = 0;
        internal const int BIK_RH = 1;
        internal const int BIK_LF = 2;
        internal const int BIK_RF = 3;
        internal const int BIK_LS = 4;
        internal const int BIK_RS = 5;
        internal const int BIK_LT = 6;
        internal const int BIK_RT = 7;
        internal const int BIK_LE = 8;
        internal const int BIK_RE = 9;
        internal const int BIK_LK = 10;
        internal const int BIK_RK = 11;
        internal const int BIK_BODY = 12;
        internal const int BIK_TOTAL = 13;
        private const int BIK_BEND_START = 8;
        private const bool MaleFeaturesTemporarilySealed = true;
        private const int WindowId = 0x4D47424A;

        private static readonly string[] BIK_Labels =
        {
            "左手 (L.Hand)",    "右手 (R.Hand)",
            "左足 (L.Foot)",    "右足 (R.Foot)",
            "左肩 (L.Shoulder)","右肩 (R.Shoulder)",
            "左腰 (L.Thigh)",   "右腰 (R.Thigh)",
            "左肘 (L.Elbow)",   "右肘 (R.Elbow)",
            "左膝 (L.Knee)",    "右膝 (R.Knee)",
            "腰中央 (Body)",
        };

        private readonly RuntimeState _runtime = new RuntimeState();
        private readonly SimpleFileLogger _fileLogger = new SimpleFileLogger();
        private readonly BIKEffectorState[] _bikEff = new BIKEffectorState[BIK_TOTAL];
        private readonly bool[] _bikWant = new bool[BIK_TOTAL];
        private readonly float[] _bikWeight = new float[BIK_TOTAL];

        private Harmony _harmony;
        private BodyIkGizmoSettings _settings;
        private ConfigEntry<bool> _cfgPluginEnabled;
        private ConfigEntry<bool> _cfgUiVisible;
        private ConfigEntry<bool> _cfgRelayLogEnabled;
        private ConfigEntry<BepInEx.Configuration.KeyboardShortcut> _cfgHipQuickSetupKey;
        private bool _syncingDetailLogSwitch;
        private string _pluginDir;

        private Rect _windowRect = new Rect(20f, 20f, 620f, 620f);
        private Vector2 _scroll;
        private string _lastResolveMissing;
        private float _nextResolveMissingLogTime;
        private bool _windowDragging;
        private bool _sliderDragging;
        private int _sliderDraggingIndex = -1;
        private bool _scrollDragging;
        private bool _gizmoDragging;
        private readonly float[] _nextOffLeakWarnTime = new float[BIK_TOTAL];
        private readonly float[] _nextOnRebindWarnTime = new float[BIK_TOTAL];
        private float _nextFbbikReenableWarnTime;
        private bool _abandonedByPostureChange;
        private bool _pendingAbandonByPostureChange;
        private string _pendingAbandonTrigger;
        private float _pendingAbandonRequestTime;
        private const float PendingAbandonFallbackDelaySeconds = 0.2f;
        private const int BodyIkPostDragHoldFrames = 2;
        private bool _vrGrabMode;
        private Controller.Lock _vrGrabLockLeft;
        private Controller.Lock _vrGrabLockRight;
        private int _vrLeftGrabIdx  = -1;
        private int _vrRightGrabIdx = -1;
        private bool _bodyCtrlLinkEnabled;
        private Controller.Lock _vrBodyCtrlLock;

        // HipQuickSetup トグル状態
        private bool _hipQuickSetupActive;
        private bool[] _hipQuickSetupSavedWant;
        private bool _hipQuickSetupTogglePending;
        // 左コントローラー非表示
        private Renderer[] _leftCtrlRenderers = Array.Empty<Renderer>();
        private bool[] _leftCtrlRendererEnabled = Array.Empty<bool>();
        private Collider[] _leftCtrlColliders = Array.Empty<Collider>();
        private bool[] _leftCtrlColliderEnabled = Array.Empty<bool>();
        private bool _leftCtrlHidden;
        // GUI通知
        private string _guiNotifyText;
        private float _guiNotifyEndTime;
        private const float GuiNotifyDurationSeconds = 2.5f;

        private bool _settingsSavePending;
        private float _settingsSaveDueTime;
        private const float SettingsSaveDelaySeconds = 0.25f;
        private float _nextStateHeartbeatTime;
        private float _nextBodyIkDiagLogTime;
        private int _lastBodyIkDiagFrame = -1;
        private int _lastBodyIkDiagIndex = -1;
        private Vector3 _lastBodyIkDiagPostBonePos;
        private Vector3 _lastBodyIkDiagPostProxyPos;
        private bool _lastBodyIkDiagHasPost;
        private readonly List<PosePresetRuntime> _posePresets = new List<PosePresetRuntime>();
        private readonly Dictionary<string, Texture2D> _posePresetThumbCache = new Dictionary<string, Texture2D>();
        private string _posePresetRootDir;
        private string _posePresetShotsDir;
        private string _posePresetIndexPath;
        private string _posePresetNameDraft = "pose";
        private bool _posePresetsLoaded;
        private bool _posePresetThumbDirty;
        private string _lastThumbClickedPresetId;
        private float _lastThumbClickTime;
        private bool _autoPosePendingApply;
        private int _autoPoseLastAnimatorStateHash;
        private int _autoPoseLastAnimatorLoop;
        private bool _autoPoseLoopReady;
        private int _autoPoseLoopCountSinceSwitch;
        private string _autoPoseLastAppliedPresetId;
        private string _poseTransitionPresetId;
        private Coroutine _poseTransitionCoroutine;
        private List<PoseTransitionPoint> _activeTransitionPoints;
        private const float PoseThumbnailDoubleClickWindow = 0.35f;
        private readonly List<MalePosePresetItem> _malePosePresets = new List<MalePosePresetItem>();
        private readonly Dictionary<string, Texture2D> _malePosePresetThumbCache = new Dictionary<string, Texture2D>();
        private string _malePosePresetRootDir;
        private string _malePosePresetShotsDir;
        private string _malePosePresetIndexPath;
        private string _malePosePresetNameDraft = "male_pose";
        private bool _malePosePresetsLoaded;
        private bool _malePosePresetThumbDirty;
        private string _lastMaleThumbClickedPresetId;
        private float _lastMaleThumbClickTime;

        private void Awake()
        {
            Instance = this;
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? string.Empty;
            PreconfigureRelayLogRoutingEarly();

            _fileLogger.Initialize(Path.Combine(_pluginDir, "MainGirlHipHijack.log"), truncateOnInitialize: true);
            _settings = SettingsStore.LoadOrCreate(_pluginDir, LogInfo, LogWarn, LogError);
            BindConfig();
            EnsureUiHiddenOnStartup();
            InitPosePresetStorage();
            InitMalePosePresetStorage();

            InitBodyIK();
            ApplySettingsToRuntime();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(PluginPatches));

            ApplyPluginEnabledState(IsPluginEnabled(), "awake");
            LogInfo("loaded");
            LogInfo("settings=" + Path.Combine(_pluginDir, SettingsStore.FileName));
            LogInfo("uiVisible=" + _settings.UiVisible);
            LogStateSnapshot("awake-init", force: true);
        }

        private void PreconfigureRelayLogRoutingEarly()
        {
            if (!LogRelayApi.IsAvailable)
                return;

            LogRelayApi.SetOwnerLogKey(RelayOwner, RelayLogKey);
            LogRelayApi.SetOwnerLogKey(InputCaptureOwnerKey, InputCaptureRelayLogKey);
        }

        private void OnDestroy()
        {
            DisableBodyCtrlLink();
            if (_vrGrabMode) ExitVRGrabMode();
            StopPoseTransitionIfRunning();
            DisableAllBodyIK(silent: true);
            ClearMaleRefs();
            ResetCachedBodyIkBindings();
            ReleaseAllInputCapture();
            _harmony?.UnpatchSelf();
            _harmony = null;
            DisposePosePresetThumbCache();
            DisposeMalePosePresetThumbCache();
            SaveSettings(immediate: true);
            LogInfo("destroyed");
            Instance = null;
        }

        private bool IsPluginEnabled()
        {
            return _cfgPluginEnabled == null || _cfgPluginEnabled.Value;
        }

        private void ApplyPluginEnabledState(bool enabled, string reason)
        {
            if (enabled)
            {
                LogInfo("plugin enabled via ConfigManager reason=" + reason);
                LogStateSnapshot("plugin-enabled", force: true);
                return;
            }

            StopPoseTransitionIfRunning();
            DisableAllBodyIK(silent: true);
            ReleaseAllInputCapture();

            _abandonedByPostureChange = false;
            _pendingAbandonByPostureChange = false;
            _pendingAbandonTrigger = null;
            _pendingAbandonRequestTime = 0f;
            ResetBodyIkDiagnosticsState();

            LogInfo("plugin disabled via ConfigManager reason=" + reason);
            LogStateSnapshot("plugin-disabled", force: true);
        }

        private void Update()
        {
            FlushPendingSettingsSave();

            if (!IsPluginEnabled())
                return;

            if (_settings != null && _settings.VerboseLog && Time.unscaledTime >= _nextStateHeartbeatTime)
            {
                _nextStateHeartbeatTime = Time.unscaledTime + 1f;
                LogStateSnapshot("heartbeat");
            }

            bool keyboardToggleDown = _cfgHipQuickSetupKey != null && _cfgHipQuickSetupKey.Value.IsDown();
            bool vrToggleDown = CheckVRHipQuickSetupInput();
            if (keyboardToggleDown || vrToggleDown)
                _hipQuickSetupTogglePending = true;

            UpdateUiDraggingStateByMouseRelease();
            UpdateInputCaptureApiState();
            TickVRDeviceScan();
            FetchVRPoses();
            UpdateFemaleHeadVRInput();
            UpdateFemaleHeadAngleGizmo();

            if (!TryResolveRuntimeRefs())
                return;

            if (_pendingAbandonByPostureChange)
            {
                TryFlushPendingAbandonByPostureChange("update-timeout", force: false);
                if (_pendingAbandonByPostureChange)
                    return;
            }

            ProcessAutoPoseRuntime();

            if (_hipQuickSetupTogglePending)
            {
                _hipQuickSetupTogglePending = false;
                ExecuteHipQuickSetup();
            }

            if (_vrGrabMode)
                HandleVRGrab();

            bool clearedStaleWant = false;
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                // Posture change直後は、UIで明示ONするまで暗黙の再有効化を許可しない。
                if (_abandonedByPostureChange && _bikWant[i])
                {
                    if (_settings != null && _settings.VerboseLog)
                        LogWarn("drop stale want after posture change idx=" + i);

                    _bikWant[i] = false;
                    if (_settings != null && _settings.Enabled != null && i < _settings.Enabled.Length)
                        _settings.Enabled[i] = false;
                    clearedStaleWant = true;
                    continue;
                }

                if (_bikWant[i] && !_bikEff[i].Running)
                    DoEnableBodyIK(i);
                else if (!_bikWant[i] && _bikEff[i].Running)
                    DoDisableBodyIK(i, preserveProxy: true);
            }

            if (clearedStaleWant)
            {
                SaveSettings();
                LogStateSnapshot("drop-stale-want", force: true);
            }
        }

        private bool CheckVRHipQuickSetupInput()
        {
            if (!VR.Active || VR.Mode == null)
                return false;

            // どちらかのコントローラーでトリガー+Bボタン(Axis0)同時押し
            return CheckVRComboDown(VR.Mode.Left) || CheckVRComboDown(VR.Mode.Right);
        }

        private static bool CheckVRComboDown(Controller ctrl)
        {
            if (ctrl == null || ctrl.Input == null)
                return false;

            var input = ctrl.Input;
            return input.GetPress(EVRButtonId.k_EButton_SteamVR_Trigger)
                && input.GetPressDown(EVRButtonId.k_EButton_Axis0);
        }

        private void ExecuteHipQuickSetup()
        {
            if (_settings == null)
                return;

            bool hipBodyActive = _bikWant[BIK_BODY] || (_bikEff[BIK_BODY] != null && _bikEff[BIK_BODY].Running);
            if (!_hipQuickSetupActive && hipBodyActive)
                _hipQuickSetupActive = true;

            if (!_hipQuickSetupActive || !hipBodyActive)
            {
                // ── 初回 or 再有効化: フルセットアップ ──
                _settings.SpeedHijackEnabled = true;
                _settings.CutFemaleAnimSpeedEnabled = true;

                // 保存済み want があれば復元（腰IKは常に明示ONする）
                if (_hipQuickSetupSavedWant != null)
                {
                    for (int i = 0; i < BIK_TOTAL; i++)
                    {
                        if (i == BIK_BODY)
                            continue;
                        if (_hipQuickSetupSavedWant[i])
                            SetBodyIK(i, on: true, saveSettings: false, reason: "quick-setup-restore");
                    }
                    _hipQuickSetupSavedWant = null;
                }

                // 腰IKはON処理のたびに必ず有効化する
                ResetBodyIKPartToAnimationPose(BIK_BODY, stopPoseTransition: true, logSnapshot: false, applyImmediate: true);
                SetBodyIK(BIK_BODY, on: true, saveSettings: false, reason: "quick-setup");
                SetGizmoVisible(BIK_BODY, on: false, saveSettings: false);
                SetBodyIKWeight(BIK_BODY, weight: 1f, saveSettings: false);

                if (VR.Active && !_bodyCtrlLinkEnabled)
                    ToggleBodyCtrlLink();

                _hipQuickSetupActive = true;
                SaveSettings();
                ShowGuiNotify("HipSetup ON");
                LogInfo("hip quick setup ON");
            }
            else
            {
                // ── 2回目: 全IK OFF + 腰リンクOFF + 左コン復帰 ──
                // 現在のwant状態を保存
                _hipQuickSetupSavedWant = new bool[BIK_TOTAL];
                for (int i = 0; i < BIK_TOTAL; i++)
                    _hipQuickSetupSavedWant[i] = i != BIK_BODY && _bikWant[i];

                // 全エフェクタOFF
                for (int i = 0; i < BIK_TOTAL; i++)
                {
                    ResetBodyIKPartToAnimationPose(i, stopPoseTransition: false, logSnapshot: false, applyImmediate: true);
                    SetBodyIK(i, on: false, saveSettings: false, reason: "quick-setup-off");
                }

                // SpeedHijack / CutFemaleAnimSpeed OFF
                _settings.SpeedHijackEnabled = false;
                _settings.CutFemaleAnimSpeedEnabled = false;

                // 男女アニメ同期リセット（先頭に巻き戻し）
                SyncResetAnimators();

                // 腰リンクOFF
                DisableBodyCtrlLink();
                // 左コン復帰
                RestoreLeftControllerVisuals();

                _hipQuickSetupActive = false;
                SaveSettings();
                ShowGuiNotify("HipSetup OFF");
                LogInfo("hip quick setup OFF");
            }
        }

        private void ShowGuiNotify(string text)
        {
            _guiNotifyText = text;
            _guiNotifyEndTime = Time.unscaledTime + GuiNotifyDurationSeconds;
        }

        internal void OnAfterHSceneLateUpdate(HSceneProc proc)
        {
            if (!IsPluginEnabled())
                return;
            if (proc == null || !_runtime.HSceneProc)
                return;
            if (!ReferenceEquals(proc, _runtime.HSceneProc))
                return;

            OnAfterHSceneLateUpdateBodyIK();
            UpdateBodyCtrlLink(Time.unscaledDeltaTime);
            TickSpeedHijack();
            UpdateMaleHMD();
            ApplyFemaleHeadAdditiveRot();
        }

        internal void OnAfterHSceneChangeAnimator(HSceneProc proc)
        {
            if (!IsPluginEnabled())
                return;
            if (proc == null)
                return;

            if (_runtime.HSceneProc == null || !_runtime.HSceneProc)
                _runtime.HSceneProc = proc;

            if (_settings != null && _settings.VerboseLog)
            {
                int currentId = _runtime.HSceneProc != null ? _runtime.HSceneProc.GetInstanceID() : 0;
                LogInfo("hook ChangeAnimator pending=" + _pendingAbandonByPostureChange + " proc=" + proc.GetInstanceID() + " current=" + currentId);
            }

            TryFlushPendingAbandonByPostureChange("changeAnimator", force: true);
        }

        private void SaveSettings(bool immediate = false)
        {
            if (immediate)
            {
                _settingsSavePending = false;
                SettingsStore.Save(_pluginDir, _settings, LogWarn);
                return;
            }

            _settingsSavePending = true;
            _settingsSaveDueTime = Time.unscaledTime + SettingsSaveDelaySeconds;
        }

        private void EnsureUiHiddenOnStartup()
        {
            if (_settings == null)
                return;

            if (_cfgUiVisible != null)
            {
                if (_cfgUiVisible.Value)
                {
                    _cfgUiVisible.Value = false;
                    LogInfo("uiVisible forced OFF on startup (config)");
                    return;
                }
            }

            if (_settings.UiVisible)
            {
                _settings.UiVisible = false;
                SaveSettings();
                LogInfo("uiVisible forced OFF on startup (settings)");
            }
        }

        private void FlushPendingSettingsSave()
        {
            if (!_settingsSavePending)
                return;
            if (Time.unscaledTime < _settingsSaveDueTime)
                return;

            _settingsSavePending = false;
            SettingsStore.Save(_pluginDir, _settings, LogWarn);
            if (_settings != null && _settings.VerboseLog)
                LogStateSnapshot("settings-flush");
        }

        private void BindConfig()
        {
            _cfgPluginEnabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Enable this plugin runtime. false disables IK handling and UI.");

            _cfgPluginEnabled.SettingChanged += (_, __) =>
            {
                ApplyPluginEnabledState(_cfgPluginEnabled.Value, "config-changed");
            };

            _cfgUiVisible = Config.Bind(
                "UI",
                "Visible",
                _settings != null && _settings.UiVisible,
                "UI表示のON/OFF（ConfigManagerから変更可）");

            _cfgUiVisible.SettingChanged += (_, __) =>
            {
                _settings.UiVisible = _cfgUiVisible.Value;
                SaveSettings();
                LogInfo("uiVisible changed via ConfigManager: " + _settings.UiVisible);
                LogStateSnapshot("ui-visible-config", force: true);
            };

            _cfgRelayLogEnabled = Config.Bind(
                "Logging",
                "EnableLogs",
                _settings != null && _settings.DetailLogEnabled,
                "MainGameLogRelay経由ログのON/OFF（HipHijack本体＋input owner）");

            _cfgRelayLogEnabled.SettingChanged += (_, __) =>
            {
                SetDetailLoggingEnabled(_cfgRelayLogEnabled != null && _cfgRelayLogEnabled.Value, "config-changed");
            };

            _cfgHipQuickSetupKey = Config.Bind(
                "Shortcut",
                "HipQuickSetupKey",
                new BepInEx.Configuration.KeyboardShortcut(KeyCode.Return),
                "腰IK即時セットアップのショートカットキー（SpeedHijack ON + 腰IK有効化）");

            _settings.UiVisible = _cfgUiVisible.Value;
            SetDetailLoggingEnabled(_cfgRelayLogEnabled != null && _cfgRelayLogEnabled.Value, "bind-init");
        }

        private void ApplyRelayLoggingState()
        {
            if (!LogRelayApi.IsAvailable)
                return;

            bool enabled = _cfgRelayLogEnabled != null && _cfgRelayLogEnabled.Value;
            LogRelayApi.SetOwnerLogKey(RelayOwner, RelayLogKey);
            LogRelayApi.SetOwnerEnabled(RelayOwner, enabled);
            LogRelayApi.SetOwnerLogKey(InputCaptureOwnerKey, InputCaptureRelayLogKey);
            LogRelayApi.SetOwnerEnabled(InputCaptureOwnerKey, enabled);
        }

        private void SetDetailLoggingEnabled(bool enabled, string reason)
        {
            if (_syncingDetailLogSwitch)
                return;

            _syncingDetailLogSwitch = true;
            try
            {
                bool settingsChanged = _settings != null && _settings.DetailLogEnabled != enabled;
                if (_settings != null)
                    _settings.DetailLogEnabled = enabled;

                bool cfgChanged = _cfgRelayLogEnabled != null && _cfgRelayLogEnabled.Value != enabled;
                if (cfgChanged)
                    _cfgRelayLogEnabled.Value = enabled;

                ApplyRelayLoggingState();
                if (UiInputCaptureApi.IsAvailable)
                    UiInputCaptureApi.SetOwnerDebug(InputCaptureOwnerKey, enabled);

                if (settingsChanged)
                    SaveSettings();

                if (_settings != null && _settings.VerboseLog)
                    LogInfo("detail/relay logs " + (enabled ? "ON" : "OFF")
                        + " reason=" + reason
                        + " cfgChanged=" + cfgChanged
                        + " settingsChanged=" + settingsChanged);
            }
            finally
            {
                _syncingDetailLogSwitch = false;
            }
        }

        private void ApplySettingsToRuntime()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                _bikWant[i] = _settings.Enabled[i];
                _bikWeight[i] = Mathf.Clamp01(_settings.Weights[i]);
            }
        }

        private void InitBodyIK()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                _bikEff[i] = new BIKEffectorState();
                _bikWeight[i] = 1f;
                if (i >= BIK_BEND_START && i != BIK_BODY)
                {
                    _bikEff[i].IsBendGoal = true;
                    _bikEff[i].BendChain = BIK_IndexToBendChain(i);
                }
            }
        }

        private bool IsFileLogEnabled() => _cfgRelayLogEnabled != null && _cfgRelayLogEnabled.Value;

        private void LogInfo(string message)
        {
            if (IsFileLogEnabled()) _fileLogger.Write("INFO", message);
            if (LogRelayApi.IsAvailable)
            {
                LogRelayApi.Info(RelayOwner, message);
                return;
            }

            Logger.LogInfo("[" + PluginName + "] " + message);
        }

        private void LogWarn(string message)
        {
            if (IsFileLogEnabled()) _fileLogger.Write("WARN", message);
            if (LogRelayApi.IsAvailable)
            {
                LogRelayApi.Warn(RelayOwner, message);
                return;
            }

            Logger.LogWarning("[" + PluginName + "] " + message);
        }

        private void LogError(string message)
        {
            if (IsFileLogEnabled()) _fileLogger.Write("ERROR", message);
            if (LogRelayApi.IsAvailable)
            {
                LogRelayApi.Error(RelayOwner, message);
                return;
            }

            Logger.LogError("[" + PluginName + "] " + message);
        }

        private void LogDebug(string message)
        {
            if (!_settings.VerboseLog)
                return;

            if (LogRelayApi.IsAvailable)
            {
                LogRelayApi.Debug(RelayOwner, message);
                return;
            }

            Logger.LogInfo("[" + PluginName + "] " + message);
        }

        private void LogMaleHmdDiag(string message)
        {
            // MaleHmdDiag 専用ログは封印: 生成・書き込みしない
        }

        private void LogStateSnapshot(string reason, bool force = false)
        {
            if (!force && (_settings == null || !_settings.VerboseLog))
                return;

            string want = BuildOnIndexList(_bikWant);
            string running = BuildRunningIndexList();
            string enabled = _settings != null ? BuildOnIndexList(_settings.Enabled) : "-";
            string text = "state[" + reason + "] want=" + want
                + " running=" + running
                + " enabled=" + enabled
                + " abandoned=" + _abandonedByPostureChange
                + " pendingAbandon=" + _pendingAbandonByPostureChange
                + " autoEnabledInH=" + _runtime.AutoEnabledInCurrentH;
            LogInfo(text);
        }

        private string BuildRunningIndexList()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (_bikEff[i] == null || !_bikEff[i].Running)
                    continue;
                if (sb.Length > 0)
                    sb.Append(",");
                sb.Append(i);
            }
            return sb.Length > 0 ? sb.ToString() : "-";
        }

        private static string BuildOnIndexList(bool[] arr)
        {
            if (arr == null)
                return "-";

            var sb = new StringBuilder();
            for (int i = 0; i < arr.Length; i++)
            {
                if (!arr[i])
                    continue;
                if (sb.Length > 0)
                    sb.Append(",");
                sb.Append(i);
            }
            return sb.Length > 0 ? sb.ToString() : "-";
        }
    }
}
