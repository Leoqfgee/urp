# v71 release snapshot

- App version: `4.25.0` (`versionCode 630`)
- Unity: `2022.3.62f2c1`
- Android package: `com.qfgeeee.paper52objecttrackingar`
- Local APK name: `BottleRepairAR_v71.apk`

This Git commit is the complete reproducible Unity project snapshot for v71. It includes source code, project settings, scenes, required models, textures, fonts, audio, and Unity `.meta` files.

Generated folders and machine-local output are intentionally excluded: `Library/`, `Temp/`, `Logs/`, `Builds/`, `UserSettings/`, IDE files, and validation logs. The APK should be distributed as a GitHub Release asset instead of being committed to normal Git history.

Build from the project root with:

```powershell
& 'E:\Unity2022\2022.3.62f2\Editor\Unity.exe' `
  -batchmode -quit `
  -projectPath 'E:\app\urp-v64-source\urp-main' `
  -executeMethod Urp.ArDemo.Editor.UrpArProjectSetup.BuildAndroidFromCommandLine `
  -logFile 'Logs\v71-build.log'
```

The generated APK is written to `Builds/BottleRepairAR_v71.apk`.
