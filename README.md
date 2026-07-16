# URP AR

Unity Android prototype for the cultural heritage digital restoration and AR presentation project.

## Implemented application flow

- Three-dimensional resource viewer for the reconstructed damaged bottle and complete bottle.
- ORB three-dimensional tracking mode with a merged 5,000-point feature database, PnP pose estimation, reprojection validation, display-coordinate correction, temporal smoothing, bottle-neck occlusion, and local luminance adaptation.
- Chinese navigation and working controls for start, reset, before/after comparison, rotate, zoom, and project information.

## Tracking data

- Actual object: cap-missing coconut drink bottle.
- Repair part: processed Meshroom bottle-cap reconstruction.
- ORB 3D databases: `Assets/OrbModels/`.
- Bottle calibration: mouth-centred canonical frame, 34 mm mouth diameter,
  39 mm cap diameter and 10 mm cap height.

The Android ORB/PnP implementation is the project-owned native plugin under `Native/UrpOrbNative`. It is built for `arm64-v8a` with OpenCV 4.10 and does not depend on the Asset Store demo plugin at runtime.

## Build

- Unity version: 2022.3.62f2.
- Unity menu: `URP AR/Setup Prototype Scene`.
- Command-line method: `Urp.ArDemo.Editor.UrpArProjectSetup.BuildAndroidFromCommandLine`.
- APK output: `Builds/urp-ar-rebuilt.apk`.
