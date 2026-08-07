# Bottle v41 registration tools

v41 replaces the v40 two-reconstruction fit with the actual Meshroom
reconstruction that owns the ORB observations.

1. AliceVision meshing/filtering/texturing produces B from
   `F:\Meshroom_work\bottle_damaged`.
2. `compute_same_reconstruction_registration_v41.py` independently measures
   raw and production mouth/base rings, scale, long axis, red-logo front, and
   barcode side. It writes the strict registration and frame artifacts.
3. `build_orb_database_by_optical_flow.py --canonical-transform-json ...`
   triangulates real-photo ORB features directly into that measured frame.
4. `package_same_reconstruction_pair_v41.py` replaces B, keeps C geometry and
   local matrix unchanged, and exports the identity-local rigid hierarchy.

The v40 scripts remain as historical evidence of the rejected forced-mouth
approach. They are not part of the v41 build.
