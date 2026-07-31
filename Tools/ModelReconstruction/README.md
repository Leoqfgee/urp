# BottleFullAlignedV2 production assets

The only production bottle geometry is the rigid Blender-authored pair from:

`F:\Meshroom_work\bottle_full_clean_v2\split_models`

Unity uses the byte-identical `bottle_full_aligned_v2.fbx` under
`Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2`. The hierarchy is:

```text
BottleRepairRoot
  DamagedBottleB
    ReferenceNeckProxyB
  BottleCapC
```

`prepare_bottle_full_aligned_v2.py` keeps the approved scan cut at model Y=0,
adds the photographed 10 mm neck as part of B, and bakes C's matching 10 mm
lift into its vertices. B and C transforms remain identity.
`render_bottle_full_aligned_v2_qa.py` renders the six required QA views without
changing that relationship.

The production ORB database is not generated from Blender renders. It contains
filtered SfM observations and ORB descriptors from the real open/no-cap bottle
photo set at `F:\Meshroom_work\bottle_damaged`. C is excluded. The database is
stored at `Assets/OrbModels/bottle_reference_b.bytes`; its manifest records the
source, bounds, hash, and supplied failure-frame replay evidence.

The copied Meshroom atlas is
`Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/Textures/bottle_full_clean_v2_albedo.png`.
Unity explicitly assigns it to B instead of relying on FBX material path
discovery. C uses the clean white cap material.
