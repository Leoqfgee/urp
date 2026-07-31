# BottleCleanCap v31 production assets

The only production bottle geometry is the rigid Blender-authored pair from:

`F:\Meshroom_work\bottle_full_clean_v2\split_models`

Unity uses `bottle_no_cap_clean_cap_v31.fbx` under
`Assets/Models/CleanBottleReconstruction/BottleCleanCapV31`. The hierarchy is:

```text
BottleRepairRoot
  DamagedBottleB
    ReferenceNeckProxyB
  BottleCapC
```

`prepare_bottle_clean_cap.py` preserves the approved rigid B+C registration and
adds the clean B-only neck guide. The residual scan shoulder cut is Y=0 and the
physical mouth plane is 20 mm above that cut. The lower 10 mm is the narrow
stem/tamper-ring transition visible in the real photograph; the clean 10.12 mm
cap encloses the upper threaded half. C overlaps B's neck instead of floating
above it.
`render_bottle_clean_cap_qa.py`
renders the six required QA views without changing that relationship.

The production ORB database is not generated from Blender renders. It contains
filtered SfM observations and ORB descriptors from the real open/no-cap bottle
photo set at `F:\Meshroom_work\bottle_damaged`. C is excluded. The database is
stored at `Assets/OrbModels/bottle_reference_b.bytes`; its manifest records the
source, bounds, hash, and supplied failure-frame replay evidence.

The copied Meshroom atlas is
`Assets/Models/CleanBottleReconstruction/BottleCleanCapV31/Textures/bottle_full_clean_v2_albedo.png`.
Unity assigns it to B. C and the B-only neck guide use the separate clean white
material.
