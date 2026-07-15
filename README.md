# URP AR

Unity Android prototype for the cultural heritage digital restoration and AR presentation project.

## Implemented application flow

- Three-dimensional resource viewer for the reconstructed damaged bottle and complete bottle.
- ORB three-dimensional tracking mode with 13 local feature/point databases, PnP pose estimation, reprojection validation, display-coordinate correction, temporal smoothing, bottle-neck occlusion, and local luminance adaptation.
- Planar marker and SLAM mode using an AR Foundation reference-image library, ARCore plane mapping, and a persistent world anchor.
- Chinese navigation and working controls for start, reset, before/after comparison, rotate, zoom, marker detection, and project information.

## Tracking data

- Actual object: cap-missing coconut drink bottle.
- Repair part: processed Meshroom bottle-cap reconstruction.
- ORB 3D databases: `Assets/OrbModels/`.
- Planar marker: `Assets/Textures/Targets/orb_target.jpg`.

The Android ORB/PnP implementation is the project-owned native plugin under `Native/UrpOrbNative`. It is built for `arm64-v8a` with OpenCV 4.10 and does not depend on the Asset Store demo plugin at runtime.

## Build

- Unity version: 2022.3.62f2.
- Unity menu: `URP AR/Setup Prototype Scene`.
- Command-line method: `Urp.ArDemo.Editor.UrpArProjectSetup.BuildAndroidFromCommandLine`.
- APK output: `Builds/urp-ar-demo.apk`.

For the planar-marker mode, display or print the supplied marker image at the physical size configured in the reference-image library before selecting `检测平面标志`.
