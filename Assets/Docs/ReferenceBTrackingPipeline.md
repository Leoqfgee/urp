# Real bottle a -> reference model b -> repair cap c

The tracking path is explicit and contains no direct `a -> c` registration.

1. `b_c_registered_canonical.blend` registers the no-cap photogrammetry model
   **b** and measured repair cap **c** to one mouth-centred canonical frame.
2. `bottle_reference_b.bytes` contains natural-feature observations of the real
   no-cap bottle **a**, expressed in that same **b** canonical frame.
3. Runtime ORB matching and solvePnP estimate the pose of hidden reference **b**.
4. `TrackedObjectPoseRoot/ModelCoordinateAlignment/TrackingReferenceBRoot`
   contains **b**, but every renderer under it is forced off.
5. `RepairPartRoot` is a sibling in the same registered frame. It contains only
   **c**, so the solved **b** pose moves **c** without drawing **b**.

The legacy `RestorationObjectProfile.orbModelDatabase` field is deliberately
left null. Runtime loads `trackingReferenceDatabase` and instantiates
`trackingReferencePrefab`; this prevents a future scene rebuild from silently
restoring the former direct-repair wiring.

The rough photogrammetry surface of b supplies correspondences for one robust
global similarity transform. That transform maps the higher-quality SfM 3D
observations into b coordinates without independently deforming points. Offline
comparison showed that per-point snapping to rough triangles reduced solvePnP
acceptance, so that diagnostic path is not used by the runtime database.
