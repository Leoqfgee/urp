# Latest failure diagnosis

Audit baseline: `main` at `81f271c4a703518476633133bed934d29d8f0a65`, equal to
`origin/main` on 2026-07-16 before this change. The worktree was clean.

## Evidence from the five supplied screenshots

1. Home page: the strip above the safe area showed the live AR camera. The page
   background was a child of `Safe Area`, so it never covered the notch/status
   area. The AR camera and `ARCameraBackground` also remained enabled.
2. Resource page: reconstructed fragments appeared above the header while the
   intended viewport was empty. `Resource Viewer Camera.targetTexture` was null,
   so that camera rendered directly to the phone screen. Models were manually
   scaled and positioned in front of it without a UI viewport or bounds fit.
3. Resource gestures: `ModelViewerController` rejected touches whenever
   `EventSystem.current.IsPointerOverGameObject()` was true. The resource page is
   itself UI, so valid viewport touches were classified as blocked UI.
4. Tracking search: the status exposed raw match count (`23/24`) as the main
   progress signal even though match count alone does not establish a valid,
   visible repair pose.
5. False tracking success: the screenshot text claimed that the virtual cap was
   overlaid, but no cap was visible. Visibility checks were not part of a strict
   calibrated success state, and unverified physical scale was still described
   as stable overlay.

## Structural corrections

- Canvas now owns a full-screen, non-raycast `FullScreenBackground`, a
  safe-area-constrained content root, and a separate modal layer.
- AR camera, camera manager, camera background, viewer camera, viewer target and
  full-screen background are explicitly switched per page.
- The resource camera renders only to a temporary RenderTexture displayed by the
  `ModelViewport` RawImage.
- `ModelViewportInputHandler` is the only model input surface.
- Viewer models are centered from combined renderer bounds and fit using both
  horizontal and vertical FOV.
- Object text, viewer models, ORB database, calibration and repair assets come
  from `RestorationObjectProfile`; the catalog contains exactly bottle and Tissue.
- An unverified profile cannot emit the final "aligned" success state.

## Native Android build

- Native source version: `urp-orb-native-2026.07.16-r3-16k`
- Source modified: 2026-07-16
- ARM64 `.so` built: 2026-07-16 22:12:13
- `.so` SHA-256:
  `E32EE97F72163D36C79773540BB1AA5455B43874BA50941D10190AF1CB444D2D`
- All packaged plugin ELF `LOAD` segments report `p_align = 0x4000`.
- The plugin extracted from the final APK has the same SHA-256 as the project
  plugin.

## Remaining evidence boundary

No Android device was connected during this diagnosis. Physical bottle scale,
bottle-mouth landmark accuracy, Tissue physical scale, Tissue missing region,
Tissue repair part and Tissue occluder remain unverified until real measurements
and a defined missing-part dataset are supplied.
