# Thesis section 5.2 Object Tracking implementation

Source: `残缺文物增强现实展示系统的研究与实现_朱林海.pdf`, PDF pages 43-45
(printed pages 37-39), especially figures 5-5, 5-6 and section 5.2.1.

## Required interaction

The thesis describes this sequence:

1. The user selects Object Tracking and then selects an artefact.
2. The rear camera starts and the selected artefact model is rendered at an
   initial pose.
3. The user moves the phone until the rendered model and the real damaged
   artefact approximately overlap.
4. Pressing Start begins tracking.
5. During tracking, only the completed/repaired portion is rendered after
   geometric and lighting consistency processing.
6. Reset restores the initial model pose so the user can align and start again.

## Bottle mapping

- **A**: the real no-cap bottle seen by the phone camera.
- **B**: the no-cap photogrammetry reference model used as the ORB/PnP object
  frame. Because its raw mesh is incomplete, the pre-Start UI displays a clean
  cyan physical-envelope outline registered to B instead of covering the real
  bottle with broken dark triangles. Raw B remains available in Diagnostics.
- **C**: the Blender-registered repair cap. It is hidden before Start and is the
  only model rendered after a valid B pose is obtained.

Runtime state flow:

`Aligning(show B outline) -> Start(hide B and C) -> Search A features -> solve B pose -> Tracking(show only C)`

The initial prior compares bottle-mouth viewport position and vertical-axis
direction. It deliberately does not compare complete Quaternions: a nearly
180-degree yaw difference between the front-facing guide convention and PnP
must not reject an otherwise upright, correctly projected bottle pose.

If tracking is lost, the user is prompted to Reset. Reset returns to
`Aligning(show B)`; it does not directly place C over A.

## UI rules

- The tracking header and unsafe status-bar area use an opaque light cover, so
  no camera strip appears above the title.
- The primary controls remain compact on the left, matching the thesis flow.
- Development diagnostics are collapsed by default behind one `诊断` button.
- Diagnostics inspect B, C and their fixed registration. The former occluder
  visibility buttons are not part of the user-facing test flow.
