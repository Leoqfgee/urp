# Tissue repair asset assessment

Tissue currently matches case C/ambiguous single-model input:

- one reconstructed mesh is present;
- the real missing region is not specified;
- no separate complete model or repair part is present;
- the mesh itself is not treated as a repair part.

The App therefore exposes the cleaned model for resource viewing and creates a
Tissue profile with its own empty tracking/repair slots. Entering tracking never
falls back to bottle ORB data, bottle calibration, bottle repair geometry or
bottle text. It reports that Tissue tracking and repair calibration are pending.

Canonical-frame placeholder for viewer organization:

- origin: cleaned geometry bounds center;
- axes: source Meshroom axes, with viewer-only orientation stored in the profile;
- physical scale: unverified;
- repair connection frame: undefined;
- ORB database: absent;
- repair registration RMS: not computable;
- occluder: absent.

Required next inputs are the confirmed missing region, a complete counterpart or
independent repair mesh, at least six corresponding landmarks, two independent
real measurements, and Meshroom cameras/poses/structure (or the original
MeshroomCache) for a real photo-derived ORB 2D-3D database.
