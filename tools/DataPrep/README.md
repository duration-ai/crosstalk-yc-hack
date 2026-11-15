# DataPrep

.NET 8 console tool that builds the offline world data the Unity game loads.

```
dotnet run --project tools/DataPrep -c Release -- [--refresh]
```

Every raw HTTP response is cached in `data/cache/` (gitignored), so reruns are offline; `--refresh` refetches.

Sources: OpenStreetMap (Overpass), DataSF Building Footprints (ynuv-fyni), Polyfork models (`models.json`: role -> ids).

Outputs:
- `unity/Assets/Resources/World/dogpatch.json` (WorldData: roads, rails, buildings, parks, land, trees; local metres, x east, z north)
- `unity/Assets/Resources/Polyfork/{id}.bytes` (GLB files) and `catalog.json` (ModelCatalog)
- `data/preview.png` top-down render for eyeballing

Bounds are constants at the top of `Program.cs`.
