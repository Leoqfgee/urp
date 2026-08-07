# URP AR production scope v41

The bottle reference mesh and ORB 3D points now originate from the same
`bottle_damaged` Meshroom reconstruction. Actual raw and production geometry
independently determine mouth, base, scale, up, and front; the old forced mouth
origin and cross-reconstruction self-consistency claim are not used.

The v40 confidence-weighted full-6DoF fusion and v39 Start presentation gate
are preserved. C has no offset, separate PnP, anchor, or runtime transform.
Development diagnostics include PoseRT, strict ModelReg endpoint/surface/axis
metrics, projected ORB/B landmarks, pose-fusion lag, and camera timestamp sync.

All offline evidence remains explicitly `device_verified=false`. A successful
Android build is not a claim that B visibly covers A on a physical phone.

See `BottleFullAlignedV2Pipeline.md` for the complete contract.
