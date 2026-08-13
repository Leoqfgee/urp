# URP AR production scope v42

v42 separates two concerns that v41 coupled accidentally:

- B geometry keeps the v41 same-reconstruction measurements and production
  mesh.
- Runtime acquisition restores the byte-identical v40 observation database:
  4100 ordered records, SHA-256
  `A046CD3386245B4A255A45088ECD9087366FF32A1352B2E20C3AC713253AC1EF`.

The audited Sim(3) bridge maps v41 B and its ReferenceNeck child into the v40
ORB frame. BottleCapC was already authored in that target frame and remains a
rigid sibling with no independent offset, PnP, anchor, or runtime motion.

PreAlignment is presentation only. Its printed front comes from the calibrated
mouth-front landmark (+Z), not Unity +X. Before a reliable stable PnP pose has
been established, native acquisition receives no pose prior and accepts only a
strict/global solution. Guided/local matching is enabled only with the last
reliable tracked pose; strict/global relocalization remains available.

Development diagnostics report database identity, acquisition mode, detected,
ratio/guided/unique matches, PnP inliers/RMS/rejection code, prior source,
prealignment front angle, registration metrics, and camera timestamp sync.

Offline, EditMode, PlayMode, native, and APK-build evidence are separate from
physical-phone evidence. `device_verified` remains false until a real Android
run confirms acquisition and overlay.

See `BottleFullAlignedV2Pipeline.md` for the complete coordinate contract.
