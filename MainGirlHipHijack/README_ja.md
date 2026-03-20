# MainGirlHipHijack

KoikatsuSunshine の H シーン向け BepInEx プラグインです。女性の全身 IK 制御、実行時ギズモ編集、追従ターゲット運用、ポーズプリセット自動化を主軸にしています。

## 状態

- Beta
- 主サポート対象は女性ワークフロー
- 男性制御 UI は現在封印中（`MaleFeaturesTemporarilySealed = true`）
- ただし、女性側の追従ターゲット候補には条件付きで男性ボーンと HMD を含められます

## 主な機能

- 女性 BodyIK の 13 エフェクタ制御:
  - 左右の手
  - 左右の足
  - 左右の肩
  - 左右の腰
  - 左右の肘
  - 左右の膝
  - 胴体（腰中央）
- 各エフェクタごとの設定:
  - ON/OFF
  - Weight（0..1）
  - ギズモ表示
  - 現在アニメ姿勢へのリセット
- ボーン追従ワークフロー:
  - `Nearest Follow` で最近傍候補へスナップ
  - 女性ボーン・男性ボーン・HMD をフィルタ済み候補として利用
- VR 操作:
  - IK プロキシの VR つかみ操作
  - 女性頭部の加算回転つかみ挙動
- 女性ポーズプリセット:
  - スクリーンショット付き保存/読込
  - 体位マッチによる自動適用
  - 遷移イージング（`Linear` / `SmoothStep` / `EaseOut`）
- H シーン連携:
  - 体のコントローラ連動
  - 速度ゲージ hijack
  - 女性アニメ速度カット（任意）

## ハード依存

- `MainGameTransformGizmo`
- `MainGameUiInputCapture`
- `MainGameLogRelay`

## 任意の同梱候補プラグイン

- `MainGirlShoulderIkStabilizer`（別プラグイン。ハード依存ではありません）

## 対象プロセス

- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`

## このフォルダ内のファイル

- `MainGirlHipHijack.dll`
- `FullIkGizmoSettings.json`
- `pose_presets/`（女性プリセット）
- `pose_presets_male/`（現行ビルドで使う男性プリセット格納）

## 設定 / ログ

ConfigManager キー（GUID: `com.kks.main.girlbodyikgizmo`）:

- `General/Enabled`
- `UI/Visible`
- `Logging/EnableLogs`

`Logging/EnableLogs` が ON のとき、`MainGameLogRelay` 経由で以下 owner キーへログ出力します:

- `com.kks.main.girlbodyikgizmo`
- `com.kks.main.girlbodyikgizmo.input`

## ビルド（ソース）

- Target framework: `net472`
- Build command: `dotnet build MainGirlHipHijack.csproj -c Release`
- Output DLL: `bin/Release/net472/MainGirlHipHijack.dll`

## プラグイン情報

- GUID: `com.kks.main.girlbodyikgizmo`
- Name: `MainGirlHipHijack`
- Version: `1.0.0`
