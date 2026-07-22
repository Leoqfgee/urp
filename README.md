# URP AR

Unity Android prototype for the cultural heritage digital restoration and AR presentation project.

## Implemented application flow

- Three-dimensional resource viewer for the reconstructed damaged bottle and complete bottle.
- Thesis section 5.2 Object Tracking flow: show a clean cyan outline registered
  to no-cap reference B, let the user frame real bottle A inside it, capture that
  pose on Start, estimate B with adaptive ORB/PnP, then hide B and render only C.
- Chinese navigation, compact tracking controls, safe-area title coverage and a
  collapsed Development diagnostics drawer for inspecting B, C and B+C registration.

## Tracking data

- Actual object: cap-missing coconut drink bottle.
- Tracking reference: no-cap photogrammetry model B.
- Repair part: Blender-registered bottle-cap model C; C is excluded from feature matching.
- ORB 3D database: `Assets/OrbModels/bottle_reference_b.bytes`.
- Bottle calibration: mouth-centred canonical frame, 34 mm mouth diameter,
  39 mm cap diameter and 10 mm cap height.

The Android ORB/PnP implementation is the project-owned native plugin under `Native/UrpOrbNative`. It is built for `arm64-v8a` with OpenCV 4.10 and does not depend on the Asset Store demo plugin at runtime.

## Build

- Unity version: 2022.3.62f2.
- Unity menu: `URP AR/Setup Prototype Scene`.
- Command-line method: `Urp.ArDemo.Editor.UrpArProjectSetup.BuildAndroidFromCommandLine`.
- APK output: `Builds/Paper52ObjectTrackingAR.apk`.
