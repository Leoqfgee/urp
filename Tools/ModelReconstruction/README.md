# BottleCleanCap v26 production assets

The only production bottle geometry is the rigid Blender-authored pair from:

`F:\Meshroom_work\bottle_full_clean_v2\split_models`

Unity uses `bottle_no_cap_clean_cap_v26.fbx` under
`Assets/Models/CleanBottleReconstruction/BottleCleanCapV26`. The hierarchy is:

```text
BottleRepairRoot
  DamagedBottleB
    ReferenceNeckProxyB
  BottleCapC
```

`prepare_bottle_clean_cap_v26.py` preserves the approved rigid B+C registration,
adds the clean B-only neck guide, places the mouth seam at the origin, and
keeps B/C transforms at identity. `render_bottle_clean_cap_v26_qa.py`
renders the six required QA views without changing that relationship.

The production ORB database is not generated from Blender renders. It contains
filtered SfM observations and ORB descriptors from the real open/no-cap bottle
photo set at `F:\Meshroom_work\bottle_damaged`. C is excluded. The database is
stored at `Assets/OrbModels/bottle_reference_b.bytes`; its manifest records the
source, bounds, hash, and supplied failure-frame replay evidence.

The copied Meshroom atlas is
`Assets/Models/CleanBottleReconstruction/BottleCleanCapV26/Textures/bottle_full_clean_v2_albedo.png`.
Unity assigns it to B. C and the B-only neck guide use the separate clean white
material.
