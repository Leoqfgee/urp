# URP AR production scope v33

The only formal bottle asset is `BottleFullAlignedV2`. Runtime solves A-to-B
only. B contains the scan and its 10 mm clean neck; C is a rigid sibling under
the same Blender-authored root.

Before Start, the app shows opaque, front-facing B+C and recognition is already
running. After stable A-to-B registration, Start hides only B renderers. C stays
enabled and inherits the full B pose.

The scene generator must not recreate the removed cyan outline, manual box,
screen-space placement, single-mouth-point projection, independent C anchor,
old grouped database, or old bottle-model copies.

See `BottleFullAlignedV2Pipeline.md` for the asset and validation contract.
