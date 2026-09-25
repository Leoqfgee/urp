# Paper 3.4.1 Occlusion Implementation

This implementation maps section 3.4.1 of *Research and Implementation of an
Augmented-Reality Display System for Damaged Cultural Relics* directly onto
the bottle repair mode.

| Paper term | This project |
| --- | --- |
| RBOT object pose | ORB descriptor matching plus PnP pose |
| Damaged model P2 and depth2 | Complete `DamagedBottleB` rendered to `BDepthRT` |
| Completion model P3 and depth3 | Original `BottleCapC`; normal URP colour pixels extracted to `CColorRT`, and the same mesh rendered to `CDepthRT` |
| `if depth2 > depth3: P = P3` | If `depthB > depthC`, composite the cap; otherwise preserve the AR camera background |

All three off-screen buffers use the same AR Camera, view matrix, projection
matrix, near/far planes, viewport, orientation, and tracked-root pose in the
same render frame. `BDepthRT` and `CDepthRT` are `R32_SFloat` textures that
store `-viewPosition.z` in metres. Their clear value is 1000 metres.

For a cap pixel, the runtime comparison is:

```text
visibleC = noB || depthC < depthB - epsilon
```

`epsilon` is 0.0005 metres. It is a numerical tolerance for floating-point
depth and contact-surface ambiguity only. It never scales, translates, clips,
or otherwise modifies either model.

Before Start, B and C render normally for registration inspection. After
Start, B is removed from the Main Camera colour target but still produces
`BDepthRT`. The immutable original C remains in the ordinary URP forward
colour pass, preserving its mesh, material property block, shader tags and
lighting. The feature renders that same mesh to `CDepthRT`, then uses the C
depth mask to extract the already-lit C pixels from the camera colour into
`CColorRT`. This avoids v47's incorrect manual assumption that material pass
index zero reproduces the normal URP Lit path. The composite starts from a
copy of the AR background captured before opaque C rendering and outputs C
only where the depth rule says it is visible. When C is absent or B is in
front, the captured AR camera colour is retained.

In Development builds, one requested Android frame writes
`DirectOriginalC.png`, `CameraBackground.png`, `BDepth.exr`, `CDepth.exr`,
`CColor.png`, `OcclusionMask.png`, and `FinalComposite.png` under
`Application.persistentDataPath/V48PaperOcclusion`. The capture also records
projection-flip and graphics-UV-origin metadata; no unconditional portrait
Y-flip is hard-coded.

ARCore Environment Depth is not used by this algorithm. ORB replaces only the
paper's pose estimator; the model-depth comparison remains the paper method.
