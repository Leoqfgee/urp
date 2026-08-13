# Bottle v42 A-to-B-to-C coordinate contract

## Geometry and acquisition are independent

A is the real open bottle, B is `DamagedBottleB`, and C is `BottleCapC`.
v41 contributed the same-reconstruction B geometry and measured mouth, base,
surface, up, and printed-front evidence. Those measurements remain authoritative.

v41 also regenerated and surface-filtered the runtime observation database,
reducing 4100 records to 3240. Device testing showed that acquisition regressed.
v42 therefore restores the complete v40 database byte for byte. Descriptor
bytes, record ordering, and 3D points are unchanged; the 5 mm surface filter is
offline diagnostic evidence only.

`Assets/Calibration/bottle_v42_v41b_to_v40orb_frame_bridge.json` records the
audited v41-B-to-v40-ORB Sim(3). It is applied to B and inherited by its
ReferenceNeck child. C already occupies the target v40 ORB frame, so applying
the bridge to C again would be a double transform. The rigid physical B/C
relationship is validated in the common target frame.

The runtime hierarchy is:

```text
TrackedBottleRoot                 accepted six-DoF PnP world pose
└── ModelCoordinateAlignment      inverse of Unity FBX import axes
    └── BottleRepairRoot          identity
        ├── DamagedBottleB        v41 geometry + audited bridge to v40 ORB
        │   └── ReferenceNeckProxyB (empty child; inherits the same bridge)
        └── BottleCapC            already in v40 ORB; identity local
```

## PreAlignment and pose-prior contract

The calibration landmarks define model front as
`normalize(mouthFrontInModel - mouthCenterInModel)` and model up as
`normalize(mouthCenterInModel - neckAxisPointInModel)`. The complete imported
hierarchy transforms those directions into `TrackedBottleRoot`. PreAlignment
then faces the printed +Z direction toward the camera and aligns bottle up with
camera up. It is never a measurement.

During SEARCHING, every tracker clears its pose prior and only strict/global
PnP may establish the first registration. After a full reliable pose passes the
existing quality and stability gates, the last reliable PnP pose may seed
guided/local matching. Strict/global remains active while registered and is the
relocalization path after loss. No PnP or spatial-coverage threshold is lowered.

## Evidence boundary

`tracking_acquisition_regression_v42.json` compares the same front, left,
right, and side frames against the v40 database and v42 candidate. Because the
runtime database is byte-identical, the strict/global results are identical.
Offline geometry, EditMode, PlayMode, native tests, and an Android build do not
prove physical-phone overlay; device verification requires collected Android
diagnostics from a real run.
