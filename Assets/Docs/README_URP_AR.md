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
hierarchy. Stage two enables B Renderers with a translucent amber validation
material. Stage three disables only B Renderers and enables C Renderers.

The project generator must never recreate the removed cyan outline, manual box,
screen-space placement, display-matrix pose correction, old registered FBX, or
old preview scenes.

See `BottleFullAlignedV2Pipeline.md` for the asset contract and truthful
real-device validation boundary.
