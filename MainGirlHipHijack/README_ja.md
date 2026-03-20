# MainGirlHipHijack

KoikatsuSunshine の H シーン向け BepInEx プラグインです。
女キャラの全身IK操作、ギズモ編集、ポーズ保存、VR補助操作を提供します。

英語版: [README.md](README.md)

## ステータス

- Beta
- 現在の主対象は女キャラ操作フロー
- 男性操作UIは一時封印中（ただし女側の追従先として男ボーン/HMD候補は利用可能）

## 主な機能

- 女BodyIK（13エフェクタ）
  - 左手 / 右手
  - 左足 / 右足
  - 左肩 / 右肩
  - 左腰 / 右腰
  - 左肘 / 右肘
  - 左膝 / 右膝
  - 腰中央（Body）
- 各エフェクタごとの設定
  - ON/OFF
  - Weight（0..1）
  - Gizmo表示/非表示
  - 現在アニメ姿勢へのリセット
- 骨追従
  - `Nearest Follow` で最寄り候補へスナップ
  - 追従候補:
    - 女ボーン（最小構成フィルタ）
    - 男ボーン（最小構成フィルタ）
    - HMD（VR姿勢取得時）
- VR操作
  - IKプロキシのVR掴み
  - 女頭部掴み（加算回転）
- 女ポーズ保存
  - スクリーンショット付き保存/読込
  - 体位一致による自動適用
  - 補間イージング（Linear / SmoothStep / EaseOut）
  - 女頭部加算回転状態も保存対象
- Hシーン補助
  - Bodyとコントローラ連動
  - 速度ゲージ乗っ取り
  - 女アニメ速度カット

## 動作要件

- KoikatsuSunshine
- BepInEx 5.x

## 依存関係

本プラグインは以下をハード依存として宣言しています。

- `MainGameTransformGizmo`
- `MainGameUiInputCapture`
- `MainGameLogRelay`

## 導入

以下にDLLを配置:

`BepInEx/plugins/canon_plugins/`

最小構成:

- `MainGirlHipHijack.dll`
- `MainGameTransformGizmo.dll`
- `MainGameUiInputCapture.dll`
- `MainGameLogRelay.dll`

## 設定ファイル

実行時設定:

`BepInEx/plugins/canon_plugins/FullIkGizmoSettings.json`

補足:

- 初回起動時に自動生成
- 読込/保存時に正規化・クランプ
- セッション依存のIK ON/OFF状態は起動時にリセット

## 既知課題

詳細:

- [KNOWN_ISSUES.md](KNOWN_ISSUES.md)

## ソースビルド

ターゲット: `net472`

ビルド:

`dotnet build MainGirlHipHijack.csproj -c Release`

出力:

`bin/Release/net472/MainGirlHipHijack.dll`

## プラグイン情報

- GUID: `com.kks.main.girlbodyikgizmo`
- Name: `MainGirlHipHijack`
- Version: `1.0.0`
- Process: `KoikatsuSunshine`, `KoikatsuSunshine_VR`