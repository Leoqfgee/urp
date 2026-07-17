# Bottle tracking architecture audit

## Build and source identity

- Audit baseline was clean `main` at `ef694a03216f08e6c8a21f6ef71fbb4cae64be3b`.
- The former APK contained the same Native plugin as the project
  (`E32EE97F...444D2D`) but contained no Git identity, so its C# HEAD could not
  be proven. Timestamp was the only available clue.
- Every new command-line build generates both a Resources identity and
  `StreamingAssets/build_identity.json`, verifies that payload from the APK,
  compares packaged/local Native SHA256, and prints APK SHA256.
- `SetupPrototypeScene()` recreates the scene and Inspector references on every
  build. All hierarchy, layer, shader, threshold and debug-button changes are
  therefore implemented in `UrpArProjectSetup.cs`, not only in the `.unity` file.

## Screenshot status strings

All three supplied strings were in `Assets/Scripts/OrbImageTrackingController.cs`:

- generic feature-distribution/pose failure: former `PassesPoseQuality()`;
- `有效匹配 18/24`: former fixed `minimumGoodMatches=24` profile gate;
- bottle-mouth projection mismatch: former `PassesAnchorProjection()` hard gate.

The first is now split into exact match, coverage, grid, PnP, inlier-ratio,
RMS, depth and continuity reasons. The second now uses the replay-backed
14/10 layered gate. The third no longer controls world position.

## Pose and hierarchy

Runtime position has one path only:

`canonical 3D point -> PnP R,t -> OpenCV camera -> Unity camera -> Unity world`.

The Native projected bottle-mouth point and AR display matrix are retained only
for Development diagnostics. No `ViewportPointToRay` path exists.

Scene hierarchy is:

```text
TrackedObjectPoseRoot
|- ModelCoordinateAlignment
|- RepairPartRoot
|  `- RegisteredBottleCap (runtime)
|- OcclusionRoot
|  `- RegisteredNeckOccluder (runtime, disabled by default)
`- DebugRoot
```

`SetRepairHierarchyVisible()` explicitly restores the root, repair root, cap
child and each renderer. A parent reactivation can no longer leave a cap child
with `activeSelf=false`.

## Canonical model and registration

- Origin: bottle-mouth contact-plane centre.
- +X: bottle right; +Y: bottle axis upward; +Z: front label direction.
- Scale: 0.17 metres per model unit.
- ORB points, clean damaged bottle and cap use this frame.
- The cap registration is baked into `bottle_cap_clean.obj`, so runtime local
  position, rotation and scale are identity.
- Occlusion geometry remains disabled until a real-device visible cap is
  confirmed. The current cylinder is diagnostic/provisional, not final.

## Evidence boundary

Editor validation and offline replay do not prove phone pixels. The Development
build provides a forced magenta cap at 0.50 m, green world-bounds lines, renderer
diagnostics, Native match/inlier/reprojection overlays, and failure-frame export.
A connected phone and user-captured screenshots are still required before any
claim that the white virtual cap is visibly aligned and stable.
