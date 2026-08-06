# URP AR production scope v34

`BottleFullAlignedV2` is the only formal bottle asset. The app recognizes A
against B with grouped multi-view ORB and recovers B's complete PnP pose. C is
a rigid Blender sibling and is never tracked or positioned independently.

Before Start, opaque B+C is front-facing and centred while recognition runs.
After a stable lock, Start removes B and its neck proxy from the colour pass,
keeps the damaged B body as a depth-only occluder, and keeps C visible. The
accepted PnP pose is applied directly to the common root, with no upright
override or screen-space correction.

The scene generator must not recreate a cyan outline, manual box, screen-space
anchor, single-mouth-point placement, independent C anchor, or single-view ORB
database. See `BottleFullAlignedV2Pipeline.md` for the full contract.
