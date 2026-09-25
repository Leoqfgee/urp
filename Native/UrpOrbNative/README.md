# UrpOrbNative

Native Android ARM64 plugin for the URP AR prototype. It performs the ORB target
matching used by `OrbImageTrackingController` and returns the target center,
relative width, match diagnostics and full solvePnPRansac pose to Unity.

The current `r10-global-first-acquisition` matcher accepts a pose prior only
after managed code has established a quality/stability-approved PnP pose. Strict
real-photo descriptor matches and guided matches are solved independently, so
SEARCHING is global-only, while REGISTERED tracking evaluates strict global and
reliable-last-pose guided candidates together. A visual PreAlignment pose is
never passed to native matching and cannot replace a valid global match set.
Each candidate is tested with SQPnP, EPNP and iterative RANSAC (with the prior
used only as one iterative seed), refined with LM, and ranked by inliers,
inlier ratio and reprojection error. The accepted pose still requires spatial
coverage, positive depth and bounded RMS/maximum reprojection error.

The plugin also samples low-saturation bright pixels around accepted inliers
and returns normalized HSV statistics to Unity. These statistics are used only
for the repair cap material's appearance consistency; they never alter pose.
The v42 formal database restores all 4,100 aeb5a36/v40 real-photo observations
byte-for-byte. The v41 5 mm surface filter is retained only as an offline
registration diagnostic; it no longer deletes runtime acquisition descriptors.
Native matching/PnP thresholds are unchanged and Blender-render descriptors are
not mixed into the database.

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
