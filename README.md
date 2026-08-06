# Bottle repair AR v35

Unity 2022.3.62f2 Android project for rigid A-to-B-to-C restoration:

- A: the real cap-missing bottle;
- B: `DamagedBottleB` and its alignment-only `ReferenceNeckProxyB`;
- C: `BottleCapC`, fixed beside B under `BottleRepairRoot`.

The app deliberately restores the v33 device-proven, 4,100-record ORB database
made only from real open-bottle photographs. Robust multi-point PnP recovers B's full 6DoF pose and
applies it directly to `TrackedBottleRoot`; C only inherits that pose. Before
Start, B+C is shown in the centre. After Start, B colour and the neck proxy are
hidden, the damaged B body supplies paper-style depth occlusion, and C remains
visible with HSV plus AR-light appearance correction.

Validation entry points:

- `Urp.ArDemo.Editor.UrpArValidation.RunFromCommandLine`
- `Urp.ArDemo.Editor.UrpArValidation.RunPlayModeSmokeFromCommandLine`
- `Urp.ArDemo.Editor.UrpArProjectSetup.BuildAndroidFromCommandLine`

The Android artifact is `Builds/BottleRepairAR_v35.apk`. Offline and editor
checks do not replace physical-device front/oblique/top acceptance testing.
