# BottleFullAlignedV2 v36 A-to-B-to-C contract

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

Before Start, opaque B+C is placed once in world space, upright and centred.
ORB recognition already runs. The production database is the `URP3DM1`
4,100-record real-photo baseline used by v33 and previously shown to recognize
this physical bottle. Its multi-point correspondences are solved with PnP and
gated by inlier consensus, spatial coverage, positive depth, and reprojection
error. The experimental grouped database is not used at runtime because it
regressed real-device pose acceptance.

The full six-degree-of-freedom PnP pose is applied directly to
`TrackedBottleRoot`. There is no session upright/yaw correction. This preserves
the measured pitch, roll, yaw, translation, and perspective in front, oblique,
and top views. C is excluded from recognition and inherits B exactly.

Start changes rendering only. It does not create, move, or reparent C:

- every B renderer is disabled for colour and depth, including
  `ReferenceNeckProxyB`;
- C remains in the colour pass and its visibility is reasserted every frame.

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

Offline replay, Unity validation, Play Mode, and APK construction verify the
software and asset contract. They do not prove physical overlay. Final device
acceptance still requires recordings in which B covers A and, after Start, C
remains seated through front, oblique, and top motion.
