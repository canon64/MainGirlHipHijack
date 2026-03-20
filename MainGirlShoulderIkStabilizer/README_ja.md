# MainGirlShoulderIkStabilizer

女性肩の安定化プラグインです。H シーン中の FinalIK 全身解に対して後段補正を行います。

女性 `FullBodyBipedIK` に肩ローテータをアタッチし、腕状態に応じて肩回転を補正します。安全クランプと左右別チューニングに対応しています。

## 対象プロセス

- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`

## ハード依存

- `MainGameLogRelay`

## コア挙動

- H シーンのメイン女性キャラ参照を解決
- 女性 `animBody` と `FullBodyBipedIK` を取得
- ソルバーホストに `ShoulderRotator` をアタッチ
- ソルバー post-update にフックして左右腕の肩補正を適用
- 対応機能:
  - 左右独立チューニング
  - 腕下げ時の反転補正
  - 腕上げ/腕下げ状態に応じた応答スケーリング
  - 角度差上限と solver blend 上限による安全制御

## 設定ファイル

- `ShoulderIkStabilizerSettings.json`

設定値は正規化/クランプされ、約2秒間隔でホットリロード監視されます。

## ログ

ConfigManager キー（例）:

- `General/VerboseLog`
- `Logging/EnableLogs`

relay ログ有効時は `MainGameLogRelay` 経由で以下 owner に出力します:

- `com.kks.main.girlshoulderikstabilizer`

## このフォルダ内のファイル

- `MainGirlShoulderIkStabilizer.dll`
- `ShoulderIkStabilizerSettings.json`

## ビルド（ソース）

- Target framework: `net472`
- Build command: `dotnet build MainGirlShoulderIkStabilizer.csproj -c Release`
- Output DLL: `bin/Release/net472/MainGirlShoulderIkStabilizer.dll`

## プラグイン情報

- GUID: `com.kks.main.girlshoulderikstabilizer`
- Name: `MainGirlShoulderIkStabilizer`
- Version: `1.0.0`
