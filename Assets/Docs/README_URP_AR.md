# URP AR production scope v36

`BottleFullAlignedV2` is the only formal bottle asset. The app recognizes A
against B with the v33 device-proven ORB baseline and recovers B's complete PnP pose. C is
a rigid Blender sibling and is never tracked or positioned independently.

Before Start, opaque B+C is front-facing and centred while recognition runs.
After a stable lock, Start disables every B renderer in both the colour and
depth passes and keeps C visible. The
accepted PnP pose is applied directly to the common root, with no upright
override or screen-space correction.

The scene generator must not recreate a cyan outline, manual box, screen-space
anchor, single-mouth-point placement, or independent C anchor. See
`BottleFullAlignedV2Pipeline.md` for the full contract.
