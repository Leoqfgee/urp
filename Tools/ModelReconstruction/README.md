# Bottle registration tools (v41 measurement, v42 runtime)

v42 deliberately separates geometry measurement from runtime observations.
Runtime acquisition uses the byte-identical, device-proven v40 database:
4100 ordered records, SHA-256
`A046CD3386245B4A255A45088ECD9087366FF32A1352B2E20C3AC713253AC1EF`.
The measured v41 B geometry is mapped into that proven ORB frame by
`Assets/Calibration/bottle_v42_v41b_to_v40orb_frame_bridge.json`.
`BottleCapC` was already authored in the target v40 ORB frame and therefore is
not transformed a second time.

v41 replaces the v40 two-reconstruction fit with the actual Meshroom
reconstruction that owns the ORB observations.

1. AliceVision meshing/filtering/texturing produces B from
   `F:\Meshroom_work\bottle_damaged`.
2. `compute_same_reconstruction_registration_v41.py` independently measures
   raw and production mouth/base rings, scale, long axis, red-logo front, and
   barcode side. It writes the strict registration and frame artifacts.
3. `build_orb_database_by_optical_flow.py --canonical-transform-json ...`
   triangulates real-photo ORB features directly into that measured frame.
4. `package_same_reconstruction_pair_v41.py` is retained as historical v41
   packaging evidence. Its byte-preservation check for C does not establish
   cross-frame correctness and it is not used to produce v42 runtime assets.

`filter_orb_database_by_surface_v41.py` remains an offline diagnostic only. Its
5 mm mesh-distance filter must not replace the runtime acquisition database
without device regression evidence.

The v40 scripts remain as historical evidence of the rejected forced-mouth
approach. They are not part of the v41 build.
