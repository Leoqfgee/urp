# BottleFullAlignedV2 v33 A-to-B-to-C contract

## Formal assets

- A is the real open bottle seen by the phone.
- B is `DamagedBottleB` plus its clean 10 mm `ReferenceNeckProxyB`.
- C is the approved clean 39 x 10 mm `BottleCapC`.

The production Blender source is
`F:\Meshroom_work\bottle_full_clean_v2\split_models\bottle_full_aligned_v2_v33.blend`.
Unity keeps the formal FBX at
`Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/bottle_full_aligned_v2.fbx`
so its Unity GUID remains stable.

```text
BottleRepairRoot
├── DamagedBottleB
│   └── ReferenceNeckProxyB
└── BottleCapC
```

The Meshroom scan ends at the damaged shoulder cut, which remains model `Y=0`
for the existing ORB feature database. Blender restores the photographed
10 mm neck above that datum. C's corresponding 10 mm lift is baked into its
vertices; B and C object transforms remain identity and Unity never positions,
rotates, or scales C independently.

## Runtime tracking

```text
TrackedBottleRoot                 accepted A-to-B world pose
└── ModelCoordinateAlignment      fixed ORB-to-Blender calibration
    └── BottleRepairRoot          Blender-authored rigid pair
        ├── DamagedBottleB        opaque before Start; Renderer off after lock
        │   └── ReferenceNeckProxyB
        └── BottleCapC            always inherits the complete B pose
```

Recognition starts when the tracking page opens. Before Start, the opaque B+C
pair is upright, front-facing, and centred for coarse alignment. Start does not
create or reposition C. Once A-to-B tracking is stable, only the B renderers
(including its neck) are disabled. C remains in the same rigid hierarchy.

ORB supplies natural-feature correspondences from real open-bottle photos.
The multi-point pose solver converts those 2D-to-3D correspondences into the
six-degree-of-freedom A-to-B pose; C is excluded from recognition.

## Consistency

- Geometry: only `TrackedBottleRoot` receives pose updates.
- Illumination: accepted B samples and AR Foundation light estimates adjust
  C's material gradually without changing pose.
- Occlusion: B can participate in validation/depth, but after lock its colour
  renderer is disabled while supported AR environment depth remains available.
- Stability: temporal confirmation, reprojection error, spatial coverage,
  deadbands, and bounded corrections gate accepted pose updates.

The editor regression now renders the repair stage into a `RenderTexture` and
fails when C is merely enabled in the hierarchy but produces no colour pixels.

## Verification boundary

Static validation, six-view Blender QA, Play Mode, and APK build prove the asset
and code contract. They do not prove physical overlay. Device success requires:

1. B stays over A through front, oblique, and top views.
2. After Start, B disappears and C remains seated through the same motion.

Until device recordings prove both, `device_overlay_verified` remains `false`.
