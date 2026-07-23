# BottleFullAlignedV2 A→B→C contract

## Asset mapping

- A is the physical bottle with its cap removed.
- B is `DamagedBottleB` in `bottle_full_aligned_v2.fbx`.
- C is `BottleCapC` in the same FBX.

The Blender source is
`F:\Meshroom_work\bottle_full_clean_v2\split_models\bottle_full_aligned_v2.blend`.
One shared uniform transform places the mouth seam at the origin, keeps Y up,
normalizes B to 1.2 model units in height, and is applied to both meshes. B and C
therefore keep their exact scan-space seam. Their object transforms are baked to
identity and their fixed relationship is stored in both the `.blend` and `.fbx`.

## Runtime pose chain

```text
TrackedBottleRoot                 accepted full PnP world pose
└── ModelCoordinateAlignment      fixed ORB-to-Blender calibration
    └── BottleRepairRoot          Blender-authored rigid asset
        ├── DamagedBottleB        stage-two translucent Renderer
        └── BottleCapC            stage-three visible Renderer
```

The B-only database is generated from 72 rendered views of `DamagedBottleB`.
`BottleCapC` is hidden during feature extraction and ray casting. Native ORB
matching and `solvePnPRansac` estimate B. Unity converts OpenCV R/t to one world
position and rotation and updates only `TrackedBottleRoot`.

Switching from B validation to C repair changes Renderer visibility only. It
does not delete B, disable the parent, rewrite C's local transform, attach C to
the camera/Canvas, or create an ARAnchor.

## Acceptance boundary

Editor validation proves the hierarchy, database format, source-code exclusions,
Renderer gate, missing-component scan, and Play Mode startup. Android build
proves packaging. Neither proves physical alignment.

Physical success requires:

1. A real-device image or recording showing translucent B continuously covering A
   while the phone moves.
2. A real-device image or recording showing B hidden and C remaining at the
   missing location while the phone moves.

Until those two recordings exist, `device_overlay_verified` remains `false`.
