# UrpOrbNative

Native Android ARM64 plugin for the URP AR prototype. It performs the ORB target
matching used by `OrbImageTrackingController` and returns the target center,
relative width, match diagnostics and full solvePnPRansac pose to Unity.

The current `r14-rigid-cap-direct-pose` matcher supports grouped `URP3DM2`
data. Its 188 calibrated real-photo view groups are shortlisted coarsely and
solved independently. Strict and pose-guided candidates cannot suppress one
another. SQPnP, EPNP and iterative RANSAC candidates are refined with LM and
ranked by inliers, ratio, reprojection error and temporal orientation
continuity. A 10-level low-threshold ORB pyramid improves repeatability under
scale and oblique-view changes.

The plugin also samples low-saturation bright pixels around accepted inliers
and returns normalized HSV statistics to Unity. These statistics are used only
for the repair cap material's appearance consistency; they never alter pose.
The formal database contains 73,047 observations from real open/no-cap bottle
photos, grouped into 188 viewpoints. C and Blender-rendered descriptors are
excluded. Held-out offline replay accepts 106/106 sampled views; this is
coverage evidence, not physical-device overlay proof.

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
