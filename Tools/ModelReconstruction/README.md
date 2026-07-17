# Clean bottle reconstruction

This generator rebuilds the drink bottle as clean parametric geometry using the
three supplied photo sets for silhouette and label appearance. It intentionally
does not retain any photogrammetry background geometry.

Physical constraints:

- bottle-neck thread outer diameter: 34 mm
- cap outer diameter: 39 mm
- cap height: 10 mm
- canonical scale: 1 model unit = 170 mm
- canonical origin: bottle-mouth centre

Run from the repository root:

```powershell
python Tools/ModelReconstruction/generate_clean_bottle_models.py `
  --source-root "F:\Au\暑期任务\抽帧照片" `
  --output "Assets\Models\CleanBottleReconstruction"
```

Outputs are a damaged/open bottle, a complete/capped bottle, an independent cap,
a shared MTL, and a photo-derived atlas. All generated geometry is deliberate;
there are no point-cloud or room-background fragments.
