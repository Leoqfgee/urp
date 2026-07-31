# BottleFullAlignedV2 v32 A-to-B-to-C contract

## Formal assets

- A is the real open bottle seen by the phone.
- B is `DamagedBottleB`, reconstructed from the open/no-cap bottle photographs.
- C is the approved clean 39 x 10 mm `BottleCapC`.

The production Blender source is
`F:\Meshroom_work\bottle_full_clean_v2\split_models\bottle_no_cap_clean_cap_registered.blend`.
The Unity FBX is
`Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/bottle_full_aligned_v2.fbx`.

The Blender hierarchy is fixed:

```text
BottleRepairRoot
├── DamagedBottleB
└── BottleCapC
```

B and C use one mouth-centred coordinate frame. Both child transforms are
baked to identity. The measured cap encloses the mouth plane by 8.77 mm and
extends 1.35 mm above it. Unity never moves, rotates, or scales C separately.
The exact source transforms, dimensions, source hashes, and cap seating are in
`bottle_full_aligned_v2_report.json`.

## Runtime tracking

```text
TrackedBottleRoot                 accepted A-to-B world pose
└── ModelCoordinateAlignment      fixed ORB-to-Blender calibration
    └── BottleRepairRoot          Blender-authored rigid pair
        ├── DamagedBottleB        opaque before Start; Renderer off after lock
        └── BottleCapC            always inherits the complete B pose
```

Recognition starts when the tracking page opens. Before Start, the opaque B+C
pair is shown upright, front-facing, and centred so the user can move the phone
until B roughly overlaps A. Start does not create or reposition C. It requests
the repair presentation; once A-to-B tracking is stable, only the B Renderers
are disabled and C remains enabled under the same tracked hierarchy.

The v32 baseline restores the complete coordinate contract from commit
`bcb344b`, the last revision with supplied physical-device evidence of both
registration and cap visibility. This includes:

- the 4,100-record URP3DM1 database built from real open-bottle observations;
- the matching native ORB and multi-point pose solver;
- the managed OpenCV-to-Unity pose conversion;
- the mouth-centred Blender B+C asset and calibration axes.

These components must not be mixed with the later grouped database or
shoulder-cut/full-neck coordinate experiments.

ORB supplies natural-feature correspondences. Multi-point camera geometry then
uses the known 3D B coordinates to recover the six-degree-of-freedom A-to-B
pose. C is excluded from recognition.

## Consistency

- Geometric consistency: B and C remain rigid siblings and only their common
  tracked root receives pose updates.
- Illumination consistency: accepted B inliers and AR Foundation light
  estimates drive the C material gradually; they do not alter pose.
- Occlusion consistency: B's visible surface is disabled after lock while AR
  Foundation environment depth remains enabled on supported devices.
- Stability: the accepted pose uses consecutive-frame confirmation,
  reprojection checks, spatial coverage, deadbands, and bounded drift
  correction. There is no screen-space anchor or independent C tracking.

## Verification boundary

Static validation, offline replay, Play Mode, and a successful APK build prove
the asset and code contract only. They do not prove physical overlay. v32 is
accepted on-device only after:

1. B remains over A while the phone moves through front, oblique, and top views.
2. After Start, B disappears and C remains seated on the real mouth through the
   same movements.

Until those recordings exist, `device_overlay_verified` remains `false`.
