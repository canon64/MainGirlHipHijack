# MainGameLogRelay

canon_plugins 向けの共有ログ中継プラグインです。

owner 単位のログルーティング、レベルフィルタ、出力先制御を一元管理します。

## 対象プロセス

- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`
- `CharaStudio`

## 公開 API（LogRelayApi）

- `Log(owner, level, message)`
- `LogLazy(owner, level, factory)`
- `Debug/Info/Warn/Error(owner, message)`
- `SetOwnerEnabled(owner, enabled)`
- `SetOwnerOutputMode(owner, mode)`
- `SetOwnerMinimumLevel(owner, level)`
- `SetOwnerLogKey(owner, logKey)`
- `ClearOwnerRuntimeOverrides(owner)`
- `GetOwnerSummary(owner)`
- `GetRelaySummary()`

## 設定ファイル

- `MainGameLogRelaySettings.json`

主な設定項目:

- `Enabled`
- `ResetOwnerLogsOnStartup`
- `DefaultOwnerEnabled`
- `DefaultOutputMode`（`FileOnly` / `BepInExOnly` / `Both`）
- `DefaultMinimumLevel`（`Debug` .. `Error`）
- `FileLayout`（既定 `PerPlugin`、または `Shared`）
- `LogInternalState`
- `OwnerRules[]`（owner 単位の上書き）

owner 上書きで扱える内容:

- enabled 上書き
- output mode 上書き
- minimum level 上書き
- file layout 上書き
- log key 上書き

## ログファイル配置

既定（`PerPlugin`）:

- 各プラグインフォルダ配下へ出力:
  - `canon_plugins/<PluginFolder>/log/*.log`

代替（`Shared`）:

- relay フォルダ配下へ集約出力:
  - `canon_plugins/MainGameLogRelay/log/*.log`

起動時ログリセット有効時は、設定に従って既存 `*.log` を削除します。

## このフォルダ内のファイル

- `MainGameLogRelay.dll`
- `MainGameLogRelaySettings.json`
- `log/`（実行時に作成/使用）

## ビルド（ソース）

- Target framework: `net472`
- Build command: `dotnet build MainGameLogRelay.csproj -c Release`
- Output DLL: `bin/Release/net472/MainGameLogRelay.dll`

## プラグイン情報

- GUID: `com.kks.maingame.logrelay`
- Name: `MainGameLogRelay`
- Version: `1.0.0`
