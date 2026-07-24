# UrpOrbNative

Native Android ARM64 plugin for the URP AR prototype. It performs the ORB target
matching used by `OrbImageTrackingController` and returns the target center,
relative width, match diagnostics and full solvePnPRansac pose to Unity.

The current `r7-real-photo-guided-b-pnp` matcher uses the user-aligned world-space B pose
only to restrict descriptor correspondences to plausible projected locations.
It does not return that coarse pose as tracking output. The accepted pose still
requires B natural-feature correspondences, RANSAC inliers, spatial coverage,
positive depth and reprojection-error checks. If the guided set is insufficient,
the tracker falls back to one-way model-to-frame ratio matches; the former
double-sided mutual test was removed because thousands of legitimate
multi-view B descriptors made its reverse ratio test reject real matches.
The formal database contains 4,100 filtered observations from the real
open/no-cap bottle photo set. Blender-render descriptors are not mixed into
that database because supplied failure-frame replay showed that the mixed
geometry reduced PnP consistency.

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

The CMake target explicitly links ARM64 builds with a 16 KB maximum/common
page size. Verify every ELF `LOAD` segment reports alignment `0x4000` before
packaging for Android 15+.
