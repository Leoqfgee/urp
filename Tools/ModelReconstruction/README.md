# BottleCleanCap v28 production assets

The only production bottle geometry is the rigid Blender-authored pair from:

`F:\Meshroom_work\bottle_full_clean_v2\split_models`

Unity uses `bottle_no_cap_clean_cap_v28.fbx` under
`Assets/Models/CleanBottleReconstruction/BottleCleanCapV28`. The hierarchy is:

```text
BottleRepairRoot
  DamagedBottleB
    ReferenceNeckProxyB
  BottleCapC
```

`prepare_bottle_clean_cap.py` preserves the approved rigid B+C registration,
adds the clean B-only neck guide, keeps the ORB/B reconstruction datum at Y=0,
and stores the physical neck and cap at Y=0.190 model units (32.3 mm).
`render_bottle_clean_cap_qa.py`
renders the six required QA views without changing that relationship.

The production ORB database is not generated from Blender renders. It contains
filtered SfM observations and ORB descriptors from the real open/no-cap bottle
photo set at `F:\Meshroom_work\bottle_damaged`. C is excluded. The database is
stored at `Assets/OrbModels/bottle_reference_b.bytes`; its manifest records the
source, bounds, hash, and supplied failure-frame replay evidence.

The copied Meshroom atlas is
`Assets/Models/CleanBottleReconstruction/BottleCleanCapV28/Textures/bottle_full_clean_v2_albedo.png`.
Unity assigns it to B. C and the B-only neck guide use the separate clean white
material.
