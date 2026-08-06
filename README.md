# Bottle repair AR v37

Unity 2022.3.62f2 Android project for rigid A-to-B-to-C restoration:

- A: the real cap-missing bottle;
- B: `DamagedBottleB` and its alignment-only `ReferenceNeckProxyB`;
- C: `BottleCapC`, fixed beside B under `BottleRepairRoot`.

The app deliberately restores the v33 device-proven, 4,100-record ORB database
made only from real open-bottle photographs. Robust multi-point PnP recovers B's full 6DoF pose and
applies it directly to `TrackedBottleRoot`; C only inherits that pose. Before
registration, B+C is shown in the centre. Once stable, B+C immediately moves
to and continuously follows the accepted A-to-B pose while still visible.
Start is then a transform-invariant presentation gate: every B renderer is disabled
for both colour and depth, while C remains visible with HSV plus AR-light
appearance correction. B is never allowed to self-occlude C.

Development Android builds provide searchable `[URP_CAP_DIAG]` snapshots of
the real ARCamera projection/frustum, rigid matrices, cap camera-space bounds,
culling, renderer/material state, and AR environment-depth state. The Editor
pixel-difference check is a synthetic rendering smoke test, not device proof.

Validation entry points:

- `Urp.ArDemo.Editor.UrpArValidation.RunFromCommandLine`
- `Urp.ArDemo.Editor.UrpArValidation.RunPlayModeSmokeFromCommandLine`
- `Urp.ArDemo.Editor.UrpArProjectSetup.BuildAndroidFromCommandLine`

The Android artifact is `Builds/BottleRepairAR_v37.apk`. Offline and editor
checks do not replace physical-device front/oblique/top acceptance testing.
