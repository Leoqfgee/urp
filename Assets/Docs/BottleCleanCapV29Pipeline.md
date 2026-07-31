# BottleCleanCap v29 A→B→C contract

## Asset mapping and rigid registration

- A is the real bottle observed by the phone with its cap removed.
- B is `DamagedBottleB`, imported from
  `F:\Meshroom_work\bottle_full_clean_v2\split_models\bottle_no_cap`.
- C is the approved clean 39 mm × 10 mm `BottleCapC`.

The production Blender source is
`F:\Meshroom_work\bottle_full_clean_v2\split_models\bottle_no_cap_clean_cap_v29.blend`.
Its FBX sibling is the app source. The canonical tracking frame is Y-up, the
printed label front faces +X, and the physical mouth centre is `(0, 0, 0)`.

The B-only reference neck is 10.00 mm high. C is 10.12 mm high from its mesh
bounds. Both use the mouth-centre origin with identity rotation and unit scale,
so C axially overlaps the neck instead of being stacked above it. C extends
8.77 mm below the mouth plane and only 1.35 mm above it. The exact transforms
and dimensions are stored in `bottle_no_cap_clean_cap_v29_report.json`.

## Runtime pose chain

```text
TrackedBottleRoot                 accepted A-to-B world pose
└── ModelCoordinateAlignment      fixed ORB-to-Blender calibration
    └── BottleRepairRoot          Blender-authored rigid asset
        ├── DamagedBottleB        opaque before Start; Renderer off after lock
        │   └── ReferenceNeckProxyB
        └── BottleCapC            clean material; never positioned independently
```

Entering the tracking page places opaque B+C once in world space, upright,
front-facing, and centred. Recognition starts before Start is pressed. The
object is not parented to the camera or Canvas.

ORB matches real open/no-cap bottle photographs to B-only 3D points. PnP is
the camera-geometry step that converts those 2D/3D correspondences to the
complete six-degree-of-freedom A-to-B pose; it is not a competing tracking
algorithm. The v29 native tracker retains the user-aligned world-pose prior,
penalizes candidates beyond 20 degrees, and rejects candidates beyond
100 degrees to avoid the cylindrical front/back ambiguity.

Pressing Start requests the repair presentation. B remains visible until the
pose passes consecutive-frame stability checks. Then only B's Renderers are
disabled; B, C, and their parent transforms remain active. C therefore retains
the fixed Blender relation while the AR camera supplies perspective changes.

## Consistency implementation

- Geometric consistency: B and C share one Blender coordinate frame and only
  the common tracked root receives pose updates.
- Occlusion consistency: B's inaccurate photogrammetry Renderer is disabled
  after lock. Environment-depth occlusion is disabled for this glossy,
  thin-walled bottle because its noisy depth previously erased C.
- Illumination consistency: verified B inliers and AR light estimates provide a
  smoothed appearance correction applied only to C.
- Tracking robustness: grouped real-photo multi-view ORB performs
  initialization and relocalization; AR world-pose holding plus bounded pose
  correction reduces raw frame-to-frame jitter.

## Acceptance boundary

Offline replay, Blender six-view QA, Unity validation, Play Mode checks and APK
construction are engineering evidence, not physical overlay proof. Physical
acceptance requires:

1. A real-device recording showing B continuously covering A through front,
   oblique, and top views.
2. A real-device recording after Start showing only C remaining at the physical
   bottle mouth while the phone moves.

Until those recordings exist, `device_overlay_verified` remains `false`.
