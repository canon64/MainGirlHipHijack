using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MainGameLogRelay;
using RootMotion.FinalIK;
using UnityEngine;

namespace MainGirlShoulderIkStabilizer;

[BepInPlugin("com.kks.main.girlshoulderikstabilizer", "MainGirlShoulderIkStabilizer", "1.0.0")]
[BepInProcess("KoikatsuSunshine")]
[BepInProcess("KoikatsuSunshine_VR")]
[BepInDependency(MainGameLogRelay.Plugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
public sealed class ShoulderIkStabilizerPlugin : BaseUnityPlugin
{
	public const string Guid = "com.kks.main.girlshoulderikstabilizer";

	public const string PluginName = "MainGirlShoulderIkStabilizer";

	public const string Version = "1.0.0";

	private const string RelayOwner = Guid;

	private const string RelayLogKey = "main/" + PluginName;

	private static readonly FieldInfo FiHSceneLstFemale = AccessTools.Field(typeof(HSceneProc), "lstFemale");

	private Harmony _harmony;

	private PluginSettings _settings;

	private string _pluginDir;

	private string _lastResolveMissing;

	private float _nextResolveMissingLogTime;

	private DateTime _settingsFileLastWrite;

	private float _nextSettingsPollTime;

	private ChaControl _targetFemale;

	private Animator _animBody;

	private FullBodyBipedIK _fbbik;

	private ShoulderRotator _rotator;

	private ConfigEntry<bool> _cfgEnabled;

	private ConfigEntry<bool> _cfgVerboseLog;

	private ConfigEntry<bool> _cfgRelayLogEnabled;

	private ConfigEntry<bool> _cfgShoulderRotationEnabled;

	private ConfigEntry<bool> _cfgIndependentShoulders;

	private ConfigEntry<bool> _cfgReverseShoulderL;

	private ConfigEntry<bool> _cfgReverseShoulderR;

	private ConfigEntry<float> _cfgShoulderWeight;

	private ConfigEntry<float> _cfgShoulderOffset;

	private ConfigEntry<float> _cfgShoulderRightWeight;

	private ConfigEntry<float> _cfgShoulderRightOffset;

	private ConfigEntry<float> _cfgLoweredArmScale;

	private ConfigEntry<float> _cfgRaisedArmStartY;

	private ConfigEntry<float> _cfgRaisedArmFullY;

	private ConfigEntry<float> _cfgRaisedArmScaleMin;

	private ConfigEntry<float> _cfgMaxShoulderDeltaAngleDeg;

	private ConfigEntry<float> _cfgMaxSolverBlend;

	internal static ShoulderIkStabilizerPlugin Instance { get; private set; }

	private void Awake()
	{
		Instance = this;
		_pluginDir = Path.GetDirectoryName(base.Info.Location) ?? string.Empty;
		PreconfigureRelayLogRoutingEarly();
		_settings = SettingsStore.LoadOrCreate(_pluginDir, LogInfo, LogWarn, LogError);
		try { _settingsFileLastWrite = File.GetLastWriteTimeUtc(Path.Combine(_pluginDir, SettingsStore.FileName)); } catch { }
		BindConfigEntries();
		ApplyRelayLoggingState();
		ApplyConfigOverrides(logChanges: false);
		_harmony = new Harmony("com.kks.main.girlshoulderikstabilizer");
		_harmony.PatchAll(typeof(ShoulderIkStabilizerPatches));
		LogInfo("loaded");
		LogInfo("settings=" + Path.Combine(_pluginDir, "ShoulderIkStabilizerSettings.json"));
		LogInfo("enabled=" + _settings.Enabled + " shoulderRotation=" + _settings.ShoulderRotationEnabled);
	}

	private void PreconfigureRelayLogRoutingEarly()
	{
		if (!LogRelayApi.IsAvailable)
		{
			return;
		}

		LogRelayApi.SetOwnerLogKey(RelayOwner, RelayLogKey);
	}

	private void OnDestroy()
	{
		try
		{
			DestroyAllRotators();
		}
		catch
		{
		}
		if (_harmony != null)
		{
			_harmony.UnpatchSelf();
			_harmony = null;
		}
		UnbindConfigEntries();
		LogInfo("destroyed");
		Instance = null;
	}

	internal void OnAfterHSceneLateUpdate(HSceneProc proc)
	{
		PollSettingsFileReload();
		if (!_settings.Enabled || !_settings.ShoulderRotationEnabled)
		{
			DisableCurrentRotator("settings disabled");
		}
		else if (TryResolveRuntimeRefs(proc))
		{
			EnsureRotator();
		}
	}

	private void PollSettingsFileReload()
	{
		if (Time.unscaledTime < _nextSettingsPollTime)
			return;
		_nextSettingsPollTime = Time.unscaledTime + 2f;

		string path = Path.Combine(_pluginDir, SettingsStore.FileName);
		try
		{
			DateTime lastWrite = File.GetLastWriteTimeUtc(path);
			if (lastWrite == _settingsFileLastWrite)
				return;
			_settings = SettingsStore.LoadOrCreate(_pluginDir, LogInfo, LogWarn, LogError);
			ApplyConfigOverrides(logChanges: false);
			_settingsFileLastWrite = File.GetLastWriteTimeUtc(path);
			LogInfo("settings reloaded from file");
		}
		catch (Exception ex)
		{
			LogWarn("settings poll failed: " + ex.Message);
		}
	}

	private bool TryResolveRuntimeRefs(HSceneProc proc)
	{
		if (proc == null)
		{
			LogResolveMissing("HSceneProc");
			return false;
		}
		ChaControl female = ResolveMainFemale(proc);
		if (female == null)
		{
			LogResolveMissing("FemaleChaControl");
			return false;
		}
		if ((object)_targetFemale != female)
		{
			_targetFemale = female;
			_animBody = null;
			_fbbik = null;
			DisableCurrentRotator("female changed");
			LogInfo("target female=" + GetFemaleName(_targetFemale));
		}
		Animator animBody = _targetFemale.animBody;
		if (animBody == null)
		{
			LogResolveMissing("animBody");
			return false;
		}
		if ((object)_animBody != animBody)
		{
			_animBody = animBody;
			_fbbik = null;
			DisableCurrentRotator("animBody changed");
			LogInfo("animBody=" + _animBody.name);
		}
		if (_fbbik == null)
		{
			_fbbik = ResolveFbbik(_targetFemale);
		}
		if (_fbbik == null)
		{
			LogResolveMissing("FullBodyBipedIK");
			return false;
		}
		return true;
	}

	private void EnsureRotator()
	{
		if (_fbbik == null)
		{
			return;
		}
		if (_rotator == null || (object)_rotator.gameObject != _fbbik.gameObject)
		{
			_rotator = _fbbik.GetComponent<ShoulderRotator>();
			if (_rotator == null)
			{
				_rotator = _fbbik.gameObject.AddComponent<ShoulderRotator>();
				LogInfo("rotator attached: " + _fbbik.gameObject.name);
			}
		}
		_rotator.Configure(_fbbik, (_targetFemale != null) ? _targetFemale.transform : null, _settings);
		if (!_rotator.enabled)
		{
			_rotator.enabled = true;
		}
	}

	private void DisableCurrentRotator(string reason)
	{
		if (!(_rotator == null))
		{
			try
			{
				UnityEngine.Object.Destroy(_rotator);
			}
			catch
			{
			}
			LogInfo("rotator removed: " + reason);
			_rotator = null;
		}
	}

	private void DestroyAllRotators()
	{
		ShoulderRotator[] all = UnityEngine.Object.FindObjectsOfType<ShoulderRotator>();
		foreach (ShoulderRotator rot in all)
		{
			if (rot != null)
			{
				UnityEngine.Object.Destroy(rot);
			}
		}
		if (all.Length != 0)
		{
			LogInfo("rotator cleanup count=" + all.Length);
		}
	}

	private ChaControl ResolveMainFemale(HSceneProc proc)
	{
		if (proc == null)
		{
			return null;
		}
		if (FiHSceneLstFemale != null && FiHSceneLstFemale.GetValue(proc) is IList listObj)
		{
			for (int i = 0; i < listObj.Count; i++)
			{
				ChaControl cha = listObj[i] as ChaControl;
				if (cha != null)
				{
					return cha;
				}
			}
		}
		ChaControl[] all = UnityEngine.Object.FindObjectsOfType<ChaControl>();
		foreach (ChaControl cha2 in all)
		{
			if (cha2 != null && cha2.sex == 1)
			{
				return cha2;
			}
		}
		return null;
	}

	private static FullBodyBipedIK ResolveFbbik(ChaControl cha)
	{
		if (cha == null || cha.animBody == null)
		{
			return null;
		}
		FullBodyBipedIK direct = cha.animBody.GetComponent<FullBodyBipedIK>();
		if (direct != null)
		{
			return direct;
		}
		return cha.animBody.GetComponentInChildren<FullBodyBipedIK>(includeInactive: true);
	}

	private void BindConfigEntries()
	{
		_cfgEnabled = Config.Bind("General", "Enabled", _settings.Enabled, "プラグイン全体のON/OFF。");
		_cfgVerboseLog = Config.Bind("General", "VerboseLog", _settings.VerboseLog, "詳細なデバッグログを出力する。");
		_cfgRelayLogEnabled = Config.Bind("Logging", "EnableLogs", false, "MainGameLogRelay経由ログのON/OFF");
		_cfgRelayLogEnabled.SettingChanged += OnRelayLogSettingChanged;
		_cfgShoulderRotationEnabled = Config.Bind("General", "ShoulderRotationEnabled", _settings.ShoulderRotationEnabled, "肩の回転補正をON/OFFする。これがOFFだと肩補正は一切動かない。");

		_cfgIndependentShoulders = Config.Bind("Shoulder", "IndependentShoulders", _settings.IndependentShoulders, "左右の肩で別々のウェイト・オフセット値を使う。OFFにすると左の設定が両肩に適用される。");
_cfgReverseShoulderL = Config.Bind("Shoulder", "ReverseShoulderL", _settings.ReverseShoulderL, "腕が下がっているとき、左肩の補正方向を逆にする。");
		_cfgReverseShoulderR = Config.Bind("Shoulder", "ReverseShoulderR", _settings.ReverseShoulderR, "腕が下がっているとき、右肩の補正方向を逆にする。");
		_cfgShoulderWeight = Config.Bind("Shoulder", "ShoulderWeight", _settings.ShoulderWeight, new ConfigDescription("左肩補正の反応強度。上げると肩が大きく動く。", new AcceptableValueRange<float>(0f, 5f)));
		_cfgShoulderOffset = Config.Bind("Shoulder", "ShoulderOffset", _settings.ShoulderOffset, new ConfigDescription("左肩補正が効き始める閾値。上げると補正が早めに働く。", new AcceptableValueRange<float>(-1f, 1f)));
		_cfgShoulderRightWeight = Config.Bind("Shoulder", "ShoulderRightWeight", _settings.ShoulderRightWeight, new ConfigDescription("右肩補正の反応強度（IndependentShouldersがONのとき有効）。", new AcceptableValueRange<float>(0f, 5f)));
		_cfgShoulderRightOffset = Config.Bind("Shoulder", "ShoulderRightOffset", _settings.ShoulderRightOffset, new ConfigDescription("右肩補正の閾値（IndependentShouldersがONのとき有効）。", new AcceptableValueRange<float>(-1f, 1f)));

		_cfgLoweredArmScale = Config.Bind("ArmState", "LoweredArmScale", _settings.LoweredArmScale, new ConfigDescription("腕が上腕より下がっているときの補正強度の倍率。0で無効、1でそのまま。", new AcceptableValueRange<float>(0f, 1f)));
		_cfgRaisedArmStartY = Config.Bind("ArmState", "RaisedArmStartY", _settings.RaisedArmStartY, new ConfigDescription("腕がこの高さを超えたら補正を弱め始める（キャラローカルY差分）。", new AcceptableValueRange<float>(-0.1f, 0.5f)));
		_cfgRaisedArmFullY = Config.Bind("ArmState", "RaisedArmFullY", _settings.RaisedArmFullY, new ConfigDescription("この高さで補正が最小値になる（キャラローカルY差分）。", new AcceptableValueRange<float>(-0.05f, 0.8f)));
		_cfgRaisedArmScaleMin = Config.Bind("ArmState", "RaisedArmScaleMin", _settings.RaisedArmScaleMin, new ConfigDescription("腕が最大まで上がったときの補正の最小倍率。1.0にすると腕を上げても補正が弱まらない。", new AcceptableValueRange<float>(0f, 1f)));

		_cfgMaxShoulderDeltaAngleDeg = Config.Bind("Safety", "MaxShoulderDeltaAngleDeg", _settings.MaxShoulderDeltaAngleDeg, new ConfigDescription("1フレームに肩が動ける最大角度（度）。大きくするほど強い補正が可能。", new AcceptableValueRange<float>(0f, 180f)));
		_cfgMaxSolverBlend = Config.Bind("Safety", "MaxSolverBlend", _settings.MaxSolverBlend, new ConfigDescription("ソルバーブレンドの上限。1.0が最大。", new AcceptableValueRange<float>(0f, 1f)));

		HookSettingChanged(_cfgEnabled);
		HookSettingChanged(_cfgVerboseLog);
		HookSettingChanged(_cfgShoulderRotationEnabled);
		HookSettingChanged(_cfgIndependentShoulders);
HookSettingChanged(_cfgReverseShoulderL);
		HookSettingChanged(_cfgReverseShoulderR);
		HookSettingChanged(_cfgShoulderWeight);
		HookSettingChanged(_cfgShoulderOffset);
		HookSettingChanged(_cfgShoulderRightWeight);
		HookSettingChanged(_cfgShoulderRightOffset);
		HookSettingChanged(_cfgLoweredArmScale);
		HookSettingChanged(_cfgRaisedArmStartY);
		HookSettingChanged(_cfgRaisedArmFullY);
		HookSettingChanged(_cfgRaisedArmScaleMin);
		HookSettingChanged(_cfgMaxShoulderDeltaAngleDeg);
		HookSettingChanged(_cfgMaxSolverBlend);
	}

	private void ApplyConfigOverrides(bool logChanges)
	{
		if (_settings == null)
		{
			return;
		}

		_settings.Enabled = _cfgEnabled?.Value ?? _settings.Enabled;
		_settings.VerboseLog = _cfgVerboseLog?.Value ?? _settings.VerboseLog;
		_settings.ShoulderRotationEnabled = _cfgShoulderRotationEnabled?.Value ?? _settings.ShoulderRotationEnabled;
		_settings.IndependentShoulders = _cfgIndependentShoulders?.Value ?? _settings.IndependentShoulders;
_settings.ReverseShoulderL = _cfgReverseShoulderL?.Value ?? _settings.ReverseShoulderL;
		_settings.ReverseShoulderR = _cfgReverseShoulderR?.Value ?? _settings.ReverseShoulderR;
		_settings.ShoulderWeight = Mathf.Clamp(_cfgShoulderWeight?.Value ?? _settings.ShoulderWeight, 0f, 5f);
		_settings.ShoulderOffset = Mathf.Clamp(_cfgShoulderOffset?.Value ?? _settings.ShoulderOffset, -1f, 1f);
		_settings.ShoulderRightWeight = Mathf.Clamp(_cfgShoulderRightWeight?.Value ?? _settings.ShoulderRightWeight, 0f, 5f);
		_settings.ShoulderRightOffset = Mathf.Clamp(_cfgShoulderRightOffset?.Value ?? _settings.ShoulderRightOffset, -1f, 1f);
		_settings.LoweredArmScale = Mathf.Clamp01(_cfgLoweredArmScale?.Value ?? _settings.LoweredArmScale);
		_settings.RaisedArmStartY = Mathf.Clamp(_cfgRaisedArmStartY?.Value ?? _settings.RaisedArmStartY, -0.1f, 0.5f);
		_settings.RaisedArmFullY = Mathf.Clamp(_cfgRaisedArmFullY?.Value ?? _settings.RaisedArmFullY, -0.05f, 0.8f);
		_settings.RaisedArmScaleMin = Mathf.Clamp01(_cfgRaisedArmScaleMin?.Value ?? _settings.RaisedArmScaleMin);
		_settings.MaxShoulderDeltaAngleDeg = Mathf.Clamp(_cfgMaxShoulderDeltaAngleDeg?.Value ?? _settings.MaxShoulderDeltaAngleDeg, 0f, 180f);
		_settings.MaxSolverBlend = Mathf.Clamp01(_cfgMaxSolverBlend?.Value ?? _settings.MaxSolverBlend);

		if (_settings.RaisedArmFullY < _settings.RaisedArmStartY + 0.001f)
		{
			_settings.RaisedArmFullY = _settings.RaisedArmStartY + 0.001f;
		}

		if (logChanges && _settings.VerboseLog)
		{
			LogInfo("config updated from BepInEx cfg");
		}
	}

	private void HookSettingChanged<T>(ConfigEntry<T> entry)
	{
		if (entry != null)
		{
			entry.SettingChanged += OnAnyConfigSettingChanged;
		}
	}

	private void UnhookSettingChanged<T>(ConfigEntry<T> entry)
	{
		if (entry != null)
		{
			entry.SettingChanged -= OnAnyConfigSettingChanged;
		}
	}

	private void UnbindConfigEntries()
	{
		if (_cfgRelayLogEnabled != null)
		{
			_cfgRelayLogEnabled.SettingChanged -= OnRelayLogSettingChanged;
		}

		UnhookSettingChanged(_cfgEnabled);
		UnhookSettingChanged(_cfgVerboseLog);
		UnhookSettingChanged(_cfgShoulderRotationEnabled);
		UnhookSettingChanged(_cfgIndependentShoulders);
UnhookSettingChanged(_cfgReverseShoulderL);
		UnhookSettingChanged(_cfgReverseShoulderR);
		UnhookSettingChanged(_cfgShoulderWeight);
		UnhookSettingChanged(_cfgShoulderOffset);
		UnhookSettingChanged(_cfgShoulderRightWeight);
		UnhookSettingChanged(_cfgShoulderRightOffset);
		UnhookSettingChanged(_cfgLoweredArmScale);
		UnhookSettingChanged(_cfgRaisedArmStartY);
		UnhookSettingChanged(_cfgRaisedArmFullY);
		UnhookSettingChanged(_cfgRaisedArmScaleMin);
		UnhookSettingChanged(_cfgMaxShoulderDeltaAngleDeg);
		UnhookSettingChanged(_cfgMaxSolverBlend);
	}

	private void OnAnyConfigSettingChanged(object sender, EventArgs e)
	{
		ApplyConfigOverrides(logChanges: true);
	}

	private void OnRelayLogSettingChanged(object sender, EventArgs e)
	{
		ApplyRelayLoggingState();
	}

	private void ApplyRelayLoggingState()
	{
		if (!LogRelayApi.IsAvailable)
		{
			return;
		}

		bool enabled = _cfgRelayLogEnabled != null && _cfgRelayLogEnabled.Value;
		LogRelayApi.SetOwnerLogKey(RelayOwner, RelayLogKey);
		LogRelayApi.SetOwnerEnabled(RelayOwner, enabled);
	}

	private void LogResolveMissing(string what)
	{
		float now = Time.unscaledTime;
		if (!string.Equals(_lastResolveMissing, what, StringComparison.Ordinal) || !(now < _nextResolveMissingLogTime))
		{
			_lastResolveMissing = what;
			_nextResolveMissingLogTime = now + 1f;
			LogWarn("resolve missing: " + what);
		}
	}

	private static string GetFemaleName(ChaControl cha)
	{
		if (cha == null)
		{
			return "(null)";
		}
		try
		{
			if (cha.fileParam != null && !string.IsNullOrEmpty(cha.fileParam.fullname))
			{
				return cha.fileParam.fullname;
			}
		}
		catch
		{
		}
		return cha.name ?? "(unnamed)";
	}

	private void LogInfo(string message)
	{
		if (LogRelayApi.IsAvailable)
		{
			LogRelayApi.Info(RelayOwner, message);
			return;
		}

		base.Logger.LogInfo("[MainGirlShoulderIkStabilizer] " + message);
	}

	private void LogWarn(string message)
	{
		if (LogRelayApi.IsAvailable)
		{
			LogRelayApi.Warn(RelayOwner, message);
			return;
		}

		base.Logger.LogWarning("[MainGirlShoulderIkStabilizer] " + message);
	}

	private void LogError(string message)
	{
		if (LogRelayApi.IsAvailable)
		{
			LogRelayApi.Error(RelayOwner, message);
			return;
		}

		base.Logger.LogError("[MainGirlShoulderIkStabilizer] " + message);
	}
}
