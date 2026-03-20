# MainGameHipHijack

KoikatsuSunshine (BepInEx 5 / net472) plugins source bundle.

This repository contains the source code for these 5 plugins:

- MainGirlHipHijack
- MainGameTransformGizmo
- MainGameUiInputCapture
- MainGirlShoulderIkStabilizer
- MainGameLogRelay

## Build

```powershell
dotnet build .\MainGameLogRelay\MainGameLogRelay.csproj -c Release
dotnet build .\MainGameTransformGizmo\MainGameTransformGizmo.csproj -c Release
dotnet build .\MainGameUiInputCapture\MainGameUiInputCapture.csproj -c Release
dotnet build .\MainGirlShoulderIkStabilizer\MainGirlShoulderIkStabilizer.csproj -c Release
dotnet build .\MainGirlHipHijack\MainGirlHipHijack.csproj -c Release
```

## DLL Release

Built DLLs are attached in GitHub Releases as `canon_plugins_bundle.zip`.
Place DLLs under:

`BepInEx/plugins/canon_plugins/`
