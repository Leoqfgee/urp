# URP AR production scope v40

The production bottle uses the unchanged real-observation ORB descriptor/3D
database and a separately reconstructed textured B. Their non-identity Sim(3)
is recorded in `Assets/Calibration/bottle_orb_to_b_registration.json` and is
baked once into the complete B+neck+C asset. Runtime
`ModelCoordinateAlignment` contains only the measured FBX import-axis inverse
`Rx(+90)` for the imported root's `Rx(-90)`; C has no independent correction.

Stable PnP is applied to visible B+C before Start. PoseRT and HierarchyRT check
camera/hierarchy arithmetic, while ModelReg is backed by independent geometry
evidence and file hashes. DisplayDiag is warning-only. Adaptive SE(3) fusion
replaces the old fixed 1.8 cm/s and 6°/s caps so reliable object motion follows
without freezing camera-relative yaw/pitch/roll.

Start only hides B and retains C. Development builds emit `[URP_POSE_DIAG]`,
`[URP_POSE_FUSION_DIAG]`, and `[URP_CAP_DIAG]`. No Editor or synthetic GPU
test is described as physical-device overlay verification.

See `BottleFullAlignedV2Pipeline.md` for the complete contract.
