# MainGameTransformGizmo

KoikatsuSunshine 本編向けの実行時 Transform Gizmo サービスプラグインです。

他プラグイン（主に MainGirlHipHijack）が使うアタッチ/デタッチと操作補助を提供します。

## 対象プロセス

- `KoikatsuSunshine`

## ハード依存

- `MainGameLogRelay`

## 提供機能

- 実行時の移動/回転/拡縮ハンドルを持つ `TransformGizmo` MonoBehaviour
- 他プラグイン向け静的 API `TransformGizmoApi`:
  - `Attach(GameObject)`
  - `TryAttach(GameObject, out TransformGizmo)`
  - `Get(GameObject)`
  - `Detach(GameObject)`
  - `GetSizeMultiplier(...)`
  - `SetSizeMultiplier(...)`
- ソース実装上の操作モデル:
  - 中央球の左クリック: `Move -> Rotate -> Scale` 切替
  - 中央球の右クリック: 軸空間 `Local/World` 切替
  - 軸ドラッグ: 現在モードで Transform 編集

## ログ

ConfigManager キー:

- `Logging/EnableLogs`

有効時は `MainGameLogRelay` 経由で以下 owner に出力します:

- `com.kks.maingame.transformgizmo`

## このフォルダ内のファイル

- `MainGameTransformGizmo.dll`

## ビルド（ソース）

- Target framework: `net472`
- Build command: `dotnet build MainGameTransformGizmo.csproj -c Release`
- Output DLL: `bin/Release/net472/MainGameTransformGizmo.dll`

## プラグイン情報

- GUID: `com.kks.maingame.transformgizmo`
- Name: `MainGameTransformGizmo`
- Version: `0.1.0`
