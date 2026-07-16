# Tissue source inventory

Import source inspected recursively:
`F:\Au\暑期任务\Tissue`.

The absolute source path is import-only and is not referenced by runtime assets.

## Files found

- Main Meshroom mesh: `tissue/texturedMesh.obj` (15,462,837 bytes)
- Material: `tissue/texturedMesh.mtl`
- Base-color texture: `tissue/texture_1001.png`
- Original capture frames: 299 PNG images under `tissue_img`
- Capture video: `tissue_img.mp4`
- Small Meshroom status/log/statistics text files

SHA-256:

- OBJ: `2EF2FEC0D98F1B1F5C081D530400E52C8B7E44C5932814770D5152BFB7952E31`
- MTL: `BA51E2165498E806C3877D67E504B94C110BCC4E1BFB833DAF624EFA6899EC19`
- texture: `EEA31B68C688C3298F44205C5FD3C408346B5AD16A855E1389CCA2ABFEE7FEB5`

## Assessment

The photographed object is a Vinda tissue box placed on a green suitcase. The
directory contains one textured reconstruction only. No FBX, PLY, ABC, EXR,
depth map, normal map, `cameras.sfm`, views/poses/structure file, MeshroomCache,
independent complete model, independent damaged model, repair-part mesh,
occluder mesh, physical dimensions or landmark calibration file was found.

Therefore the available data supports a cleaned textured viewer asset. It does
not support a real SfM descriptor-to-3D database or a defensible repair-part
registration without additional source data.
