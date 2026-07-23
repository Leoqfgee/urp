# BottleFullAlignedV2 AR restoration

Unity 2022.3.62f2 Android project for rigid A→B→C restoration:

- **A** — the real cap-missing bottle seen by the phone camera.
- **B** — `DamagedBottleB`, the no-cap mesh in the new photogrammetry asset.
- **C** — `BottleCapC`, the completion mesh fixed beside B under
  `BottleRepairRoot`.

The runtime tracker matches natural features from A only against B, solves B's
full six-degree-of-freedom PnP pose, and applies that pose to
`TrackedBottleRoot`. C is never positioned independently. The validation stage
shows translucent B over A. After the operator confirms that B remains aligned
from multiple views, the app disables only B's Renderers and shows C.

The production flow contains no cyan outline, manual box, screen-space anchor,
camera-front placement, bottleneck single-point projection, display-matrix pose
correction, or independent ARAnchor for C.

## Formal assets

- B+C FBX:
  `Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/bottle_full_aligned_v2.fbx`
- B-only ORB database: `Assets/OrbModels/bottle_reference_b.bytes`
- Calibration: `Assets/Calibration/CoconutBottleRepairCalibration.asset`
- Scene: `Assets/Scenes/UrpARPrototype.unity`

## Validation and build

- Static/runtime-contract validation:
  `Urp.ArDemo.Editor.UrpArValidation.RunFromCommandLine`
- Play Mode smoke validation:
  `Urp.ArDemo.Editor.UrpArValidation.RunPlayModeSmokeFromCommandLine`
- Android build:
  `Urp.ArDemo.Editor.UrpArProjectSetup.BuildAndroidFromCommandLine`
- APK: `Builds/BottleFullAlignedV2AR.apk`

Static validation and an APK do not prove physical A/B registration. Real-device
acceptance still requires an actual translucent-B-over-A recording followed by
an actual C-only recording from moving viewpoints.
