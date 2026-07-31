# UrpOrbNative

Native Android ARM64 plugin for the URP AR prototype. It performs the ORB target
matching used by `OrbImageTrackingController` and returns the target center,
relative width, match diagnostics and full solvePnPRansac pose to Unity.

The current `r12-prior-constrained-multiview` matcher supports both the legacy flat
`URP3DM1` database and the grouped `URP3DM2` database. Each V2 group contains
real no-cap-bottle ORB observations from one calibrated viewpoint. Groups are
solved independently, which prevents descriptors from incompatible viewpoints
being combined into a false pose. The best group is ranked by inliers, inlier
ratio and reprojection error. With a user-aligned B pose, candidates more than
100 degrees from the prior are rejected and candidates beyond 20 degrees
receive an additional score penalty. This prevents the approximately
180-degree front/back ambiguity observed on the near-cylindrical bottle.

During continuous tracking, the last accepted view group is tested first with
the user-aligned world-space B pose as a geometric prior. A strong solution
avoids rescanning every view; quality loss falls back to all groups for
relocalization. Strict descriptor matches and prior-guided matches are still
solved independently. Each candidate is tested with SQPnP, EPNP and iterative
RANSAC, refined with LM, and must pass spatial coverage, positive-depth and
bounded reprojection-error gates.

For a large multi-view database, relocalization first compares 40 evenly
sampled descriptors per view and shortlists the best 24 groups. Full matching
and PnP then run only on those groups. This preserves the dense viewpoint
coverage without scanning every full keyframe on the phone.

The v29 build retains the 10-level ORB pyramid (1.15 scale factor) and low FAST
threshold so label features remain repeatable under scale and oblique-view
changes. After registration, guided matching is constrained to a tighter
projected neighbourhood; global strict matches remain available for
relocalization.

The plugin also samples low-saturation bright pixels around accepted inliers
and returns normalized HSV statistics to Unity. These statistics are used only
for the repair cap material's appearance consistency; they never alter pose.
The formal V2 database is generated from exact ORB keypoints in the real
open/no-cap bottle photo set. Keypoints are associated across calibrated
neighbouring images by forward/backward optical flow and triangulated into the
canonical Blender B coordinate frame. C and Blender-rendered descriptors are
excluded from registration.

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
