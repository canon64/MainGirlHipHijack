# HipHijack

KoikatsuSunshine (BepInEx 5 / net472) 向けプラグインのソース一式です。

英語版: [README.md](README.md)

このリポジトリには次の 5 プラグインのソースを含みます。

- MainGirlHipHijack
- MainGameTransformGizmo
- MainGameUiInputCapture
- MainGirlShoulderIkStabilizer
- MainGameLogRelay

## ビルド

```powershell
dotnet build .\MainGameLogRelay\MainGameLogRelay.csproj -c Release
dotnet build .\MainGameTransformGizmo\MainGameTransformGizmo.csproj -c Release
dotnet build .\MainGameUiInputCapture\MainGameUiInputCapture.csproj -c Release
dotnet build .\MainGirlShoulderIkStabilizer\MainGirlShoulderIkStabilizer.csproj -c Release
dotnet build .\MainGirlHipHijack\MainGirlHipHijack.csproj -c Release
```

## DLL リリース

ビルド済み DLL は GitHub Releases の `canon_plugins_bundle.zip` に添付します。
配置先:

`BepInEx/plugins/canon_plugins/`
