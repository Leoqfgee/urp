# URP AR production scope v39

`BottleFullAlignedV2` is the only formal bottle asset. The app recognizes A
against B with the v33 device-proven ORB baseline and recovers B's complete PnP pose. C is
a rigid Blender sibling and is never tracked or positioned independently.

Before a stable pose is available, opaque B+C is front-facing and centred.
As soon as consecutive-frame validation accepts a pose, that pose is applied
to the common root immediately: B+C remain visible and track A before Start.
Start is enabled only in that state and then disables every B renderer while
keeping C visible. It does not apply a pose, change a transform, reparent an
object, or replace a material.

The imported FBX root carries an axis/unit hierarchy, but the profile no longer
contains a guessed Euler correction. Five corresponding landmarks are passed
through the actual imported B hierarchy and fitted to the ORB canonical frame.
For the current FBX this derives `Rx(+90 degrees)`, unit scale, and zero
translation with negligible landmark RMS. There is no independent C calibration.

Native rotates the CPU image and intrinsics to a display-oriented tracking
frame. PnP R/t stay in that frame for Unity conversion. `UndoImageRotation` is
used only for the exact native-image round trip and raw-frame pose prior; using
it on the final pose was the v37 portrait-roll defect.

The same PnP inlier correspondences are evaluated in the native oriented CPU
camera and K. PoseRT validates PnP -> Unity root -> Unity camera -> oriented CV;
BHierarchy separately validates the imported B hierarchy. Both must pass for
three consecutive reliable frames before `ReadyForRepair`. The old display
cross-projection is retained only as `DisplayDiag WARN`; crop/aspect differences
can no longer discard a stable PnP pose or trigger tracking loss.

Development Android builds emit `[URP_CAP_DIAG]` snapshots for the real
ARCamera, rigid matrices, renderer bounds, camera-space cap corners, frustum,
culling, material state, projection, camera background, and environment-depth
state. The optional marker, RGB axes, and unlit magenta override are diagnostic
only and are inert in release builds.

Development builds also emit `[URP_POSE_DIAG]`: CPU/native/screen dimensions,
rotationClockwise, camera facing, intrinsics, ORB and rendered-B projected axes,
NativePnP/PoseRT/BHierarchy/DisplayDiag RMS, the derived alignment matrix, every hierarchy transform,
and B mesh/renderer bounds.

The scene generator must not recreate a cyan outline, manual box, screen-space
anchor, single-mouth-point placement, or independent C anchor. See
`BottleFullAlignedV2Pipeline.md` for the full contract.
