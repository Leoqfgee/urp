# Bottle v41 A-to-B-to-C coordinate contract

## Provenance and measured model frame

A is the real open bottle, B is `DamagedBottleB`, and C is `BottleCapC`.
v40 used an independent `bottle_full_clean_v2` reconstruction and forced a
hard-coded B mouth point onto an older provisional ORB origin. That could make
an internal landmark error equal zero while the visible bottle remained high.

v41 reconstructs B from the same `F:\Meshroom_work\bottle_damaged` Meshroom
project used to generate the real-photo ORB features. The raw AliceVision mesh
and the filtered production surface are measured in separate passes. Robust
mouth and base rings define the bottle endpoints; their line defines +Y. Red
logo detections in source photographs define printed-front +Z, while separately
recorded barcode-side views reject the cylindrical yaw ambiguity. The supplied
34 mm neck diameter establishes scale. The historical
`[0.419225,-4.514827,0.314265]` mouth origin is explicitly rejected because its
source correspondence file is marked provisional and physically unverified.

`Assets/Calibration/bottle_orb_to_b_registration.json` records the actual
raw-B-to-canonical matrix, both endpoint measurements, directional residuals,
file hashes, and ORB-point-to-production-B triangle distances. ModelReg uses
the strict 1/2/3/2.5/5 mm and 1.5 degree gates; no copied landmark pair can
create a PASS.

The runtime hierarchy is:

```text
TrackedBottleRoot                 accepted six-DoF PnP world pose
└── ModelCoordinateAlignment      exact inverse of Unity FBX import axes
    └── BottleRepairRoot          identity
        ├── DamagedBottleB        same-reconstruction mesh, identity local
        │   └── ReferenceNeckProxyB (empty compatibility node; neck is in B)
        └── BottleCapC            unchanged geometry and identity local
```

## Pose, timing, and Start contract

The v38 portrait-oriented PnP chain remains unchanged. Stable PnP is applied
to visible B+C before Start. v40 confidence-weighted SE(3) fusion remains the
only temporal filter; position/rotation smoothing are active and no fixed
0.018 m/s or 6 degree/s correction cap exists.

Start is a presentation-only gate: B renderers are hidden and C is retained.
Root, pair, B, and C matrices are asserted unchanged. Development builds draw
separate ORB/B mouth, base, and front landmarks and emit
`[URP_CAMERA_SYNC_DIAG]` with CPU-image timestamp, closest AR frame timestamp,
time delta, and camera-pose delta. This separates static registration bias from
motion-dependent capture latency.

## Evidence boundary

Offline geometry, EditMode, PlayMode, native tests, and synthetic rendering do
not prove physical-phone overlay. `device_verified` remains false until a real
Android run confirms B covers A in front, oblique, and top views and C remains
at the mouth after Start.
