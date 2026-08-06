# Bottle v40 registration tools

The 4,100-record ORB database and `bottle_full_clean_v2` B are different
Meshroom reconstructions. Do not claim same-reconstruction identity.

1. `export_b_registration_surface.py` exports actual legacy/runtime rendered B
   triangles in explicit coordinate frames.
2. `compute_cross_reconstruction_registration_v40.py` validates the measured
   cross-reconstruction Sim(3), real triangle distances, semantic axes, hashes,
   and writes the registration/frame-contract JSON files.
3. `package_bottle_orb_pair_v40.py` applies that one matrix to B, neck and C
   vertices together and exports the identity-local runtime hierarchy.

`prepare_bottle_full_aligned_v2.py` documents the pre-v40 source authoring
asset; it is not the final ORB↔B registration step. C is never moved by itself.
