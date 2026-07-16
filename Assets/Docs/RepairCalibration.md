# Coconut bottle repair calibration

## Coordinate source

All `Assets/OrbModels/bottle_view_*.bytes` files use the same Meshroom SfM
world coordinate system. The old implementation assigned a different mouth
anchor to each view even though their 3D points shared one frame. The new
calibration uses one global mouth frame for every ORB database.

The current global mouth center is the median of the 13 legacy anchors:

`(0.419225, -4.514827, 0.314265)` model units.

The old anchors span approximately `(0.169876, 0.103417, 0.169569)` model
units, which explains visible jumps when the active ORB database changed.

## Physical scale status

The current `metersPerModelUnit` value is `0.18`. It is retained only as a
provisional compatibility value. No caliper measurement is stored in the
project, so `physicalScaleVerified` is false and the application must not claim
millimetre-accurate registration.

To verify the scale, measure at least two distances on the same physical
bottle, for example:

1. bottle-mouth outside diameter;
2. bottle height between two identifiable SfM points.

For each measurement calculate:

`metersPerModelUnit = physical distance in metres / model distance in units`

The two results should agree within the measurement and reconstruction error.
If they do not, the SfM reconstruction or selected model points must be
rechecked.

## Mouth frame

The frame is stored in
`Assets/Calibration/CoconutBottleRepairCalibration.asset`:

- `mouthCenterInModel`
- `mouthRightInModel`
- `mouthFrontInModel`
- `neckAxisPointInModel`

The axes are calculated as:

`up = normalize(mouthCenter - neckAxisPoint)`

`right = normalize(project(mouthRight - mouthCenter, plane normal up))`

`forward = normalize(cross(right, up))`

The front reference determines the forward sign.

## Repair mesh registration

`Tools/register_repair_mesh.py` implements Umeyama similarity registration and
requires at least four explicit source/target correspondences. It stops when
the requested RMS limit is exceeded. The provisional generated transform has
an RMS residual of `0.000023016` model units across four correspondences.
Those target correspondences were derived from the assumed cap dimensions,
not measured directly on the real bottle, so this is only a numerical fit to
the provisional inputs and is not a verified physical-registration error.

Until those points and the two physical dimensions are measured, the generated
registered cap remains a provisional visual alignment, not a metrology result.
