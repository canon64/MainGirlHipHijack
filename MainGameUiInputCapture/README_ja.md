# MainGameUiInputCapture

KoikatsuSunshine 本編/VR 向けの共有 UI 入力キャプチャ調停プラグインです。

ドラッグ操作時の入力競合を避けるため、プラグイン間で一時的なカーソル/カメラ入力の所有を調停します。

## 対象プロセス

- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`

## ハード依存

- `MainGameLogRelay`

## 公開 API（UiInputCaptureApi）

- `Sync(ownerKey, sourceKey, active)`
- `Begin(ownerKey, sourceKey)`
- `Tick(ownerKey, sourceKey)`
- `End(ownerKey, sourceKey)`
- `EndOwner(ownerKey)`
- `SetIdleCursorUnlock(ownerKey, enabled)`
- `IsOwnerActive(ownerKey)`
- `SetOwnerDebug(ownerKey, enabled)`
- `IsAnyActive()`
- `GetStateSummary()`

## 実行時挙動

- キャプチャ中はカメラ/カーソルのロック制約を一時解除
- キャプチャ終了時に前状態を復元
- owner 単位のトークン管理（`owner::source`）をサポート
- owner 単位のアイドル時カーソル解除をサポート

## 設定ファイル

- `MainGameUiInputCaptureSettings.json`

現行ソースの設定項目:

- `DetailLogEnabled`
- `LogStateOnTransition`
- `VerboseLog`（legacy/unused）

## ログ

ConfigManager キー:

- `Logging/EnableLogs`

有効時は `MainGameLogRelay` 経由で以下 owner に出力します:

- `com.kks.maingame.uiinputcapture`

## このフォルダ内のファイル

- `MainGameUiInputCapture.dll`
- `MainGameUiInputCaptureSettings.json`

## ビルド（ソース）

- Target framework: `net472`
- Build command: `dotnet build MainGameUiInputCapture.csproj -c Release`
- Output DLL: `bin/Release/net472/MainGameUiInputCapture.dll`

## プラグイン情報

- GUID: `com.kks.maingame.uiinputcapture`
- Name: `MainGameUiInputCapture`
- Version: `1.0.0`
