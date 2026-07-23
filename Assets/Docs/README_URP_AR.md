# URP AR production scope

The formal bottle restoration asset is BottleFullAlignedV2. Its Blender and FBX
hierarchy is:

```text
BottleRepairRoot
├── DamagedBottleB
└── BottleCapC
```

Runtime solves only A→B. `TrackedBottleRoot` receives every accepted PnP
translation and rotation; B and C remain under one rigid object-coordinate
hierarchy. Entering the page shows B+C at one temporary world-space coarse
pose. The user aligns B to A by moving the phone and presses Start. Guided
natural-feature matching plus multi-point PnP refine B. After the stable-frame
gate the runtime automatically disables only B Renderers while C remains
enabled.

The project generator must never recreate the removed cyan outline, manual box,
screen-space placement, display-matrix pose correction, old registered FBX, or
old preview scenes.

See `BottleFullAlignedV2Pipeline.md` for the asset contract and truthful
real-device validation boundary.
