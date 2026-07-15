# UrpOrbNative

Native Android ARM64 plugin for the URP AR prototype. It performs the ORB target
matching used by `OrbImageTrackingController` and returns the target center,
relative width, and match count to Unity.

Build inputs used for the current binary:

- Unity NDK: `F:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Data\PlaybackEngines\AndroidPlayer\NDK`
- OpenCV Android SDK: `F:\Au\native-build\opencv-4.10.0-android-sdk\OpenCV-android-sdk`

Example build command:

```powershell
$ndk='F:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Data\PlaybackEngines\AndroidPlayer\NDK'
$opencv='F:\Au\native-build\opencv-4.10.0-android-sdk\OpenCV-android-sdk\sdk\native\jni\abi-arm64-v8a'
$build='F:\Au\native-build\urp-orb-native\build-arm64'
cmake -S 'F:\Au\urp-unity-ar\Native\UrpOrbNative' -B $build -G 'Unix Makefiles' `
  -D CMAKE_TOOLCHAIN_FILE="$ndk\build\cmake\android.toolchain.cmake" `
  -D ANDROID_ABI=arm64-v8a `
  -D ANDROID_PLATFORM=android-24 `
  -D CMAKE_BUILD_TYPE=Release `
  -D OpenCV_DIR="$opencv"
cmake --build $build --config Release -j 8
```

Copy the resulting `libUrpOrbNative.so` into:

`Assets/Plugins/Android/arm64-v8a/libUrpOrbNative.so`
