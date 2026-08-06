# URP AR production scope v37

`BottleFullAlignedV2` is the only formal bottle asset. The app recognizes A
against B with the v33 device-proven ORB baseline and recovers B's complete PnP pose. C is
a rigid Blender sibling and is never tracked or positioned independently.

Before a stable pose is available, opaque B+C is front-facing and centred.
As soon as consecutive-frame validation accepts a pose, that pose is applied
to the common root immediately: B+C remain visible and track A before Start.
Start is enabled only in that state and then disables every B renderer while
keeping C visible. It does not apply a pose, change a transform, reparent an
object, or replace a material.

The imported FBX root carries Unity's measured `-90 degrees X` axis conversion.
The profile has one fixed `+90 degrees X` model-coordinate alignment on the
common B+C parent to cancel it. There is no independent C calibration.

Development Android builds emit `[URP_CAP_DIAG]` snapshots for the real
ARCamera, rigid matrices, renderer bounds, camera-space cap corners, frustum,
culling, material state, projection, camera background, and environment-depth
state. The optional marker, RGB axes, and unlit magenta override are diagnostic
only and are inert in release builds.

The scene generator must not recreate a cyan outline, manual box, screen-space
anchor, single-mouth-point placement, or independent C anchor. See
`BottleFullAlignedV2Pipeline.md` for the full contract.
