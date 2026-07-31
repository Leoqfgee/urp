# URP AR production scope v32

The only formal bottle restoration asset is `BottleFullAlignedV2`. Runtime
solves A-to-B only. B and C remain rigid children of the same tracked root.

Before Start, the app shows the opaque, front-facing B+C pair and begins bottle
recognition. After a stable A-to-B pose is accepted, Start hides only B
Renderers. C stays enabled and inherits every translation and rotation of B.

The scene generator must not recreate the removed cyan outline, manual box,
screen-space placement, single-mouth-point projection, independent C anchor,
old grouped database, or old bottle-model copies.

See `BottleFullAlignedV2Pipeline.md` for the full asset and validation contract.
