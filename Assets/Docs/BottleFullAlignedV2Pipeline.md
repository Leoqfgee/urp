# BottleFullAlignedV2 A→B→C contract

## Asset mapping and rigid registration

- A is the real bottle observed by the phone with its cap removed.
- B is `DamagedBottleB`, imported from
  `F:\Meshroom_work\bottle_full_clean_v2\split_models\bottle_no_cap`.
- C is the approved clean 39 mm × 10 mm `BottleCapC`, imported from
  `F:\Au\暑期任务\bottle0720\processed_20260721\clean_cap`.

The production Blender source is
`F:\Meshroom_work\bottle_full_clean_v2\split_models\bottle_no_cap_clean_cap_registered.blend`.
Its FBX sibling is the app source. The canonical frame is Y-up, the printed
label front faces +X, the bottle-mouth plane and centre are `(0, 0, 0)`, and B
extends toward negative Y. C's inner roof
is placed 8.65 mm above its bottom and aligned to the mouth plane. Its 34.4 mm
inner diameter leaves 0.2 mm radial clearance around the measured 34 mm neck.

B and C are baked into this common frame. Their local position and rotation are
zero and local scale is one. The exact source-to-canonical matrix, dimensions,
hash and B/C transforms are stored in
`bottle_full_aligned_v2_report.json`.

## Runtime pose chain

```text
TrackedBottleRoot                 coarse world pose, then accepted A-to-B pose
└── ModelCoordinateAlignment      fixed ORB-to-Blender calibration
    └── BottleRepairRoot          Blender-authored rigid asset
        ├── DamagedBottleB        textured before Start; depth-only after lock
        └── BottleCapC            clean material; never positioned independently
```

Entering the page places B+C once in world space, upright and centred. Recognition
starts immediately, before Start is pressed. The object is not parented to the
camera or Canvas. Phone motion therefore changes its perspective naturally.

The production ORB database contains 4,100 filtered records from real open/no-cap
bottle photographs. It excludes C and excludes rendered-mesh descriptors. Strict
photo matches and coarse-pose-guided matches are solved independently. SQPnP,
EPNP and iterative RANSAC candidates are refined and selected using inliers,
spatial coverage and reprojection error.

PnP is not a competing tracker. It is the camera-geometry step that converts
ORB's 2D image points and their known 3D B points into the six-degree-of-freedom
A-to-B pose. The UI deliberately reports this as “三维姿态” instead of exposing
the implementation name.

Pressing Start requests repair presentation. B remains visible until the pose
has passed consecutive-frame stability checks. Then only B's colour is removed:
its Renderer stays enabled with a depth-only material, while C remains a fixed
sibling and inherits the same root pose. During short or prolonged ORB
relocalisation the last accepted root remains in AR world space; C is never
snapped to a screen point.

## Thesis consistency steps

- Geometric consistency: B and C share a Blender coordinate frame and only the
  common `TrackedBottleRoot` receives pose updates.
- Occlusion consistency: depth-only B hides portions of C that should be behind
  the real bottle; AR Foundation environment depth remains enabled when the
  device supports it.
- Illumination consistency: verified B inliers sample low-saturation bright
  bottle pixels in HSV. The smoothed sample is combined with AR Foundation
  ambient, main-light and spherical-harmonic estimates and applied only to C.
- Tracking robustness: real-photo multi-view ORB performs initialisation and
  relocalisation; world-space holding avoids visible screen-space snapping while
  a new accepted pose is sought.

## Acceptance boundary

Offline replay, Unity validation, Play Mode checks and APK construction are
engineering evidence, not physical overlay proof. Physical acceptance still
requires:

1. A real-device recording showing B continuously covering A while the phone
   moves through front, oblique and top views.
2. A real-device recording after Start showing only C remaining at the bottle
   mouth while the phone moves.

Until those recordings exist, `device_overlay_verified` remains `false`.
