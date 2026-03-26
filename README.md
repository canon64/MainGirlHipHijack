# canon_plugins (MainGirlHipHijack profile)

MainGirlHipHijack を中心にした、KKS向けプラグイン群のCS配布用READMEです。

## このプロファイルの主用途
- Hシーン中の女性BodyIK操作
- 追従ボーン編集・ギズモ編集
- 頭角度のスライダー/ギズモ操作
- 体位一致のポーズ自動呼び出し

## 含まれるプラグイン（CS/DLL対象）
- MainGirlHipHijack
- MainGameTransformGizmo
- MainGameUiInputCapture
- MainGameLogRelay

## 任意同梱（推奨）
- MainGirlShoulderIkStabilizer
  - 必須依存ではないが、HipHijack配布プロファイルでは同梱対象

## MainGameBlankMapAdd 連携
- MainGameBlankMapAdd が導入され、VideoAllposeRoom を使用している場合、
  再生バーから MainGirlHipHijack UI の表示/非表示を切り替え可能
- 再生バー2段目の `説明` の右側 `HipUI` チェックで切り替え

## 主要設定ファイル
- BepInEx/plugins/canon_plugins/MainGirlHipHijack/FullIkGizmoSettings.json

## ログ
- BepInEx/plugins/canon_plugins/MainGirlHipHijack/MainGirlHipHijack.log
