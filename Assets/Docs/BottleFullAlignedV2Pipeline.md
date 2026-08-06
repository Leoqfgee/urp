# BottleFullAlignedV2 v38 A-to-B-to-C contract

## Rigid asset contract

- A is the real open bottle seen by the phone.
- B is `DamagedBottleB` plus its 10 mm `ReferenceNeckProxyB` child.
- C is the approved clean 39 x 10 mm `BottleCapC`.

The Blender-authored hierarchy is fixed:

```text
BottleRepairRoot
├── DamagedBottleB
│   └── ReferenceNeckProxyB
└── BottleCapC
```

The scan cut is model `Y=0`. Blender restores the measured 10 mm neck above
that datum and bakes C around the same mouth plane. B, neck, and C keep identity
local transforms. Runtime code never positions, rotates, or scales C by itself.

## Tracking contract

```text
TrackedBottleRoot                 complete accepted PnP world pose
└── ModelCoordinateAlignment      fixed profile calibration only
    └── BottleRepairRoot          immutable Blender B+C relationship
```

Before registration, opaque B+C is placed once in world space, front-facing
and centred while ORB recognition runs. The production database is the `URP3DM1`
4,100-record real-photo baseline used by v33 and previously shown to recognize
this physical bottle. Its multi-point correspondences are solved with PnP and
gated by inlier consensus, spatial coverage, positive depth, and reprojection
error. The experimental grouped database is not used at runtime because it
regressed real-device pose acceptance.

After consecutive-frame stability validation, the full six-degree-of-freedom
PnP pose is applied immediately to `TrackedBottleRoot`, before Start. Every
subsequent accepted pose continues to move the common root while B and C stay
visible. The state does not become `ReadyForRepair` until this has happened.
There is no session upright/yaw correction. This preserves the measured pitch,
roll, yaw, translation, and perspective in front, oblique, and top views. C is
excluded from recognition and inherits B exactly.

The imported FBX `BottleRepairRoot` is measured with rotation near
`Rx(-90 degrees)` and hierarchy scale 100, while imported mesh vertices carry
the reciprocal file-unit scale and an X handedness reflection. Runtime passes
five actual B landmark coordinates through that complete hierarchy and solves
the proper similarity transform to the reflected ORB target landmarks. The
current asset derives zero translation, unit scale and `Rx(+90 degrees)` with
landmark RMS below 1e-5. The value is an output of the fit, not a profile Euler.

Native rotates raw CPU pixels and intrinsics by `frameRotationClockwise` before
PnP. The resulting R/t is already in the display-oriented tracking camera.
Unity applies only `CvCameraToUnityCamera = diag(1,-1,1)` on the camera side;
the imported mesh X reflection supplies the model-side handedness conversion.
The old final-pose `UndoImageRotation` left a portrait 90-degree roll and was
removed. Raw/oriented rotation and inverse rotation remain unit-tested for
0/90/180/270 degrees and are still used to supply the native raw-frame prior.

Before registration, `UnityPoseConsistencyGate` retrieves the exact native PnP
inlier model/image pairs. Each observed pixel is converted through the oriented
K and ARCamera projection, while the same model point is passed through the
prospective TrackedBottleRoot, derived alignment, actual FBX hierarchy, and
`WorldToScreenPoint`. RMS above 5 px blocks registration and ReadyForRepair.

Start changes rendering only. It does not apply registration, create, move,
rotate, scale, reparent, or rematerial C:

- every B renderer is disabled for colour and depth, including
  `ReferenceNeckProxyB`;
- C remains in the colour pass and its visibility is reasserted every frame.

`StartDoesNotChangeRigidPose` records the world matrices of
`TrackedBottleRoot`, `BottleRepairRoot`, `DamagedBottleB`, and `BottleCapC`
around the actual `StartRecognition()` call. Position tolerance is 0.01 mm,
rotation tolerance is 0.01 degree, and scale tolerance is 1e-6.

Development Android builds can log `[URP_CAP_DIAG]` snapshots containing all
four rigid transforms, B/C bounds, all eight C bounds corners in ARCamera space,
near/far checks, frustum intersection, layer/culling state, renderer flags,
shader/material/property-block fields, projection data, ARCameraBackground,
and AROcclusionManager depth modes. The marker/axes and magenta override are
development diagnostics only; they never modify C's transform.

## Paper-aligned consistency

- Geometry follows thesis section 3.3: the recovered B pose drives the repaired
  model in the same rigid object frame.
- Occlusion is deliberately limited to environment/AR depth. The scanned B
  mesh is not used as a depth proxy because its reconstructed shoulder and
  neck overlap C and erase the required cap pixels on the real device.
- Illumination follows chapter 4: low-saturation B pixels around verified ORB
  inliers provide HSV correction, combined with AR Foundation ambient colour,
  intensity, spherical harmonics, and main-light estimates. Smoothing affects
  C's material only, never its pose.

The related Tjaden et al. region tracker motivates temporal pose continuity,
but the production tracking algorithm remains ORB as required. The Gruber et
al. photometric-registration reference motivates geometry-aware light matching;
the mobile implementation uses verified B samples plus AR light estimates.

## Evidence boundary

The graphics-enabled RenderTexture check is only an Editor synthetic rendering
smoke test. Offline replay, Unity validation, EditMode/PlayMode tests, and APK
construction verify the software and asset contract. They do not prove physical overlay. Final device
acceptance still requires recordings in which B covers A and, after Start, C
remains seated through front, oblique, and top motion.
