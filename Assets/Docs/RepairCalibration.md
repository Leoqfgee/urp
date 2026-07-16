# Coconut bottle repair calibration

## Canonical coordinate source

All `Assets/OrbModels/bottle_*.bytes` records are stored in one mouth-centred
canonical frame. The source SfM mouth centre was:

`(0.419225, -4.514827, 0.314265)` source model units.

It is now canonical `(0, 0, 0)`, with `+X` right, `+Y` up and `+Z` toward the
front reference. The registered cap uses the same frame, so the initial preview
and PnP pose no longer extrapolate from the distant SfM world origin.

## Physical scale status

The supplied measurements are:

1. bottle-mouth outer thread diameter: `34 mm`;
2. cap outer diameter: `39 mm`;
3. cap height: `10 mm`.

The canonical mouth diameter is `0.2` model units, therefore
`metersPerModelUnit = 0.034 / 0.2 = 0.17`. The cap mesh is dimension-normalised
to `39 x 10 mm` after registration. The estimated cap inner diameter is `35 mm`
and estimated effective depth is `8 mm`; these estimates affect only the visual
starting geometry and are not labelled as measurements.

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

The original four-point similarity fit remains a numerical source registration,
then the mesh is moved to the canonical mouth frame and dimension-normalised.
The physical dimensions are recorded, but the final camera overlay remains
unverified until it is tested on the Android device from front, side and oblique
views.
