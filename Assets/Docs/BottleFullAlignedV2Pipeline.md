# Bottle v40 A-to-B-to-C coordinate contract

## Provenance and fixed model registration

A is the real open bottle. B is `DamagedBottleB` plus
`ReferenceNeckProxyB`; C is `BottleCapC`. The 4,100 unchanged ORB records and
the current B mesh come from different Meshroom reconstructions, so v40 does
not assert an identity frame and does not use copied B landmarks.

`Assets/Calibration/bottle_orb_to_b_registration.json` records the measured
similarity transform `p_orb = T_ORB_FROM_B * p_B`, both source/target hashes,
independent mouth/right/up/front controls, and all-point triangle-surface
statistics. The yaw is locked by the red-logo/front texture: source B `+X`
maps to ORB `+Z`; the barcode side cannot satisfy that contract. The physical
mouth centre maps exactly to ORB `(0,0,0)`. ORB `+Y` is base-to-mouth and ORB
`+X` is bottle-right.

The same matrix is baked offline into the vertices of B, the B neck, and C.
No object is transformed separately. The runtime hierarchy is therefore:

```text
TrackedBottleRoot                 accepted six-DoF PnP world pose
└── ModelCoordinateAlignment      Rx(+90), inverse of imported FBX root Rx(-90)
    └── BottleRepairRoot          identity
        ├── DamagedBottleB        identity local transform
        │   └── ReferenceNeckProxyB
        └── BottleCapC            identity local transform
```

The source-to-ORB Sim(3) is provenance, not a runtime offset. Applying it again
at runtime would be a coordinate conversion bug.

## Pose and state contract

Native rotates CPU pixels and intrinsics before solvePnP. PnP R/t already use
that oriented tracking-camera frame; final pose conversion does not call
`UndoImageRotation`. `PoseRT` validates the CV→Unity→CV round trip in the same
native K. `HierarchyRT` proves only transform arithmetic. Real model
registration is a separate `ModelReg` gate backed by the JSON artifact.
Display-space RMS remains diagnostic only.

After stable PnP, B+C immediately receive the pose before Start. Accepted
updates use confidence-weighted SE(3) EMA based on inliers, ratio, RMS,
coverage, and continuity. High confidence follows rapidly, marginal confidence
smooths, and low confidence holds; the former 0.018 m/s and 6°/s freeze caps do
not exist.

Start is a pure presentation gate. It disables B renderers and retains C. It
does not change position, rotation, scale, parent, material, or registration.
`StartDoesNotChangeRigidPose` checks Root/B/C matrices around the call.

## Evidence boundary

EditMode, PlayMode, offline surface validation, and Editor rendering prove the
software/asset contract only. `device_verified` remains false until an actual
Android ARCamera run visibly shows B covering A in front, left/right oblique,
top, near, and far views and C remaining at the mouth after Start.
