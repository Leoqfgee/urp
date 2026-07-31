# BottleCleanCap v29 production assets

The only production bottle geometry is the rigid Blender-authored pair from:

`F:\Meshroom_work\bottle_full_clean_v2\split_models`

Unity uses `bottle_no_cap_clean_cap_v29.fbx` under
`Assets/Models/CleanBottleReconstruction/BottleCleanCapV29`. The hierarchy is:

```text
BottleRepairRoot
  DamagedBottleB
    ReferenceNeckProxyB
  BottleCapC
```

`prepare_bottle_clean_cap.py` preserves the approved rigid B+C registration and
adds the clean B-only neck guide. The physical mouth centre is Y=0. Both the
10 mm neck and 10.12 mm cap use that same origin and overlap axially; they are
not stacked into a long 32.3 mm neck.
`render_bottle_clean_cap_qa.py`
renders the six required QA views without changing that relationship.

The production ORB database is not generated from Blender renders. It contains
filtered SfM observations and ORB descriptors from the real open/no-cap bottle
photo set at `F:\Meshroom_work\bottle_damaged`. C is excluded. The database is
stored at `Assets/OrbModels/bottle_reference_b.bytes`; its manifest records the
source, bounds, hash, and supplied failure-frame replay evidence.

The copied Meshroom atlas is
`Assets/Models/CleanBottleReconstruction/BottleCleanCapV29/Textures/bottle_full_clean_v2_albedo.png`.
Unity assigns it to B. C and the B-only neck guide use the separate clean white
material.
