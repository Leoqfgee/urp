# Paper 3.4.1 Occlusion Implementation

This implementation maps section 3.4.1 of *Research and Implementation of an
Augmented-Reality Display System for Damaged Cultural Relics* directly onto
the bottle repair mode.

| Paper term | This project |
| --- | --- |
| RBOT object pose | ORB descriptor matching plus PnP pose |
| Damaged model P2 and depth2 | Complete `DamagedBottleB` rendered to `BDepthRT` |
| Completion model P3 and depth3 | Original `BottleCapC` rendered to `CColorRT` and `CDepthRT` |
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
Start, neither is drawn directly into the Main Camera colour target. The full
B renderers produce `BDepthRT`; the byte-for-byte original C renderers produce
`CDepthRT`, while their original material and lighting produce `CColorRT`.
The composite pass outputs C only where the depth rule says it is visible.
When C is absent or B is in front, the existing AR camera colour is retained.

ARCore Environment Depth is not used by this algorithm. ORB replaces only the
paper's pose estimator; the model-depth comparison remains the paper method.
