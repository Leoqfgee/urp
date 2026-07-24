# BottleFullAlignedV2 production assets

The only production bottle geometry is the rigid Blender-authored pair from:

`F:\Meshroom_work\bottle_full_clean_v2\split_models`

Unity uses the byte-identical `bottle_full_aligned_v2.fbx` under
`Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2`. The hierarchy is:

```text
BottleRepairRoot
  DamagedBottleB
  BottleCapC
```

`prepare_bottle_full_aligned_v2.py` applies one shared canonical transform to B
and C, places the mouth seam at the origin, and bakes both child transforms to
identity. `render_bottle_full_aligned_v2_qa.py` renders the six required QA
views without changing that relationship.

The production ORB database is not generated from Blender renders. It contains
filtered SfM observations and ORB descriptors from the real open/no-cap bottle
photo set at `F:\Meshroom_work\bottle_damaged`. C is excluded. The database is
stored at `Assets/OrbModels/bottle_reference_b.bytes`; its manifest records the
source, bounds, hash, and supplied failure-frame replay evidence.

The copied Meshroom atlas is
`Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/Textures/bottle_full_clean_v2_albedo.png`.
Unity explicitly assigns it to both B and C instead of relying on FBX material
path discovery.
