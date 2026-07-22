# Real bottle a -> reference model b -> repair cap c

The tracking path is explicit and contains no direct `a -> c` registration.

1. `b_c_registered_canonical.blend` registers the no-cap photogrammetry model
   **b** and measured repair cap **c** to one mouth-centred canonical frame.
2. On entering Object Tracking, a clean cyan physical-envelope outline registered
   to **b** is shown. The user frames real bottle **a** inside that outline,
   matching the initialization described in thesis section 5.2. The incomplete
   raw photogrammetry triangles are available only in Diagnostics.
3. Start captures that coarse **b** pose, hides both **b** and **c**, and begins
   matching natural features from **a** against `bottle_reference_b.bytes`.
4. ORB 2D-3D matching and solvePnP estimate the pose of **b**. The first accepted
   pose must remain consistent with the guide's mouth projection and vertical
   axis; complete Quaternion/yaw equality is deliberately not required.
5. After a valid pose, `TrackingReferenceBRoot` stays hidden and
   `RepairPartRoot` renders only **c**. **c** contributes no descriptors.

The legacy `RestorationObjectProfile.orbModelDatabase` field is deliberately
left null. Runtime loads `trackingReferenceDatabase` and instantiates
`trackingReferencePrefab`; this prevents a future scene rebuild from silently
restoring direct `a -> c` placement.

The rough photogrammetry surface of b supplies correspondences for one robust
global similarity transform. That transform maps the higher-quality SfM 3D
observations into b coordinates without independently deforming points. Offline
comparison showed that per-point snapping to rough triangles reduced solvePnP
acceptance, so that diagnostic path is not used by the runtime database.
