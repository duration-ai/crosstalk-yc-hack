# DataPrep

.NET 8 console tool that builds the offline world data the Unity game loads: roads, rails, buildings (with type), trees, props, landmarks.

```
dotnet run --project tools/DataPrep -c Release -- [--refresh] [--models] [--no-validate]
```

One command, rerunnable. Every raw HTTP response is cached in `data/cache/` (gitignored), so reruns are offline; `--refresh` refetches. All requests send the User-Agent
`crosstalk-dogpatch-dataprep/1.0 (+https://github.com/duration-ai/crosstalk-yc-hack)` and retry with exponential backoff (6 attempts; 429, 408, 5xx, timeouts, honours Retry-After; Overpass falls
back across three mirrors). DataSF queries are always bounded to the bbox (`within_box` or a lat/lon range). `--models` additionally downloads the Polyfork GLBs listed in `models.json`
into `unity/Assets/Resources/Polyfork` (off by default so a data run never touches that folder).

## Bounds

South 37.7495, North 37.7645, West -122.3965, East -122.3800 (constants at the top of `Program.cs`, local origin = the box centre). Kept from the first extract: the box already contains the whole Pier 70
site to the bay edge and the YC campus (580 20th St, OSM node 11657169276, 37.76078 -122.38664) with >= 413 m to the nearest edge (north 413 m, east 584 m, west 870 m, south 1254 m). It is deliberately
not clipped to the official Dogpatch outline. The bbox and this reason are also in `data/extract-stats.json`.

## Sources and licences

| Data | Source | Dataset id | Licence |
|---|---|---|---|
| Roads, rails, parks, coastline, building tags, props, landmarks, platforms, bridges, viaducts, water | OpenStreetMap via Overpass | n/a (two queries in `Program.cs`) | ODbL, (c) OpenStreetMap contributors |
| Building footprints + height (geometry and height source) | DataSF Building Footprints | ynuv-fyni | PDDL |
| Parcel polygons, street, zoning | DataSF Parcels - Active and Retired | acdm-wktn | PDDL |
| Land use (residential units, retail / PDR / CIE / MED / MIPS / visitor sqft, parking, open space) | DataSF San Francisco Land Use | c5ge-t6pj | CC0 |
| Use code, year built, stories, units (roll year 2025) | DataSF Assessor Historical Secured Property Tax Rolls | wv5m-vpq2 | PDDL |
| Street trees (species, DBH, plot size) | DataSF San Francisco Street Tree Inventory | tkzw-k3nq | PDDL |
| Traffic signals | DataSF Traffic Signals | ybh5-27n2 | PDDL |
| Muni stops (name, shelter) | DataSF Muni Stops | i28k-bkz6 | PDDL |
| Parking meters | DataSF Parking Meters | 8vzz-qzz9 | PDDL |
| Bike racks | DataSF Bicycle Parking Racks | hn4j-6fx5 | PDDL |
| Trash cans | DataSF Trash Can Locations | 5buk-jp7b | PDDL |
| Bike share docks (legacy 2024 snapshot) | DataSF Bay Area Bike Share Stations | 7jbp-yzp3 | PDDL |
| Stop signs (corner position, facing) | DataSF Stop Signs | 4542-gpa3 | PDDL |
| Park benches, lamp posts, picnic tables, fountains, racks, cans | DataSF Assets Maintained by the Recreation and Parks Department | e3jj-mbb3 | PDDL |
| Models | Polyfork | polyfork.dev | see Polyfork |

Datasets looked for and not found on data.sf.gov (discovery API, `search_context=data.sf.gov`): SFPUC/DPW fire hydrants, SFPUC streetlights, bollards, drinking fountains outside parks,
public art inside the bbox (cf6e-9e4j and wg8w-68vc have 0 rows here). Hydrants and street lamps therefore come from OSM only (3 hydrants, 30 lamps incl. 11 park lamp posts from e3jj-mbb3).
Street Signs (m48z-6ji4, 2,827 rows) were left out as too dense and not requested. Esri World Imagery is used only as a validation backdrop (below); nothing is derived from it.

Attribution string (also in dogpatch.json): see `Attribution` in `Program.cs`.

## Outputs

- `unity/Assets/Resources/World/dogpatch.json`: WorldData, camelCase, local metres, x east, z north, heading = degrees clockwise from north (+z). Mirror: `unity/Assets/Scripts/Runtime/Data/WorldData.cs` (names and types must match; JsonUtility drops mismatches).
- `data/extract-stats.json`: bounds + reason, counts per category / type / source, top species, landmarks, warnings.
- `data/preview.png`: top-down render (trees green dots, props small dots by kind, landmarks ringed).
- `data/validation/*.png`: footprints, roads, trees, props over Esri imagery (validation only).
- `unity/Assets/Resources/Polyfork/*` with `--models`.

## Building type rules (Buildings.cs, BuildingTypes.Classify)

`type` + `typeRule` are derived; the raw inputs (`use`, `useCode`, `yearBuilt`, `stories`, `units`, `areaM2`, `height`, `osmTags`, `landUseRaw`) are kept so rules can be revisited. First match wins: garage, utility, shed (OSM),
industrial_brick (OSM historic / Pier 70 names), civic (OSM), shed (<= 60 m2 and <= 4.5 m), industrial_brick (PDR/IND built before 1945), civic (GOVT / CIE / MED), warehouse (PDR/IND),
residential (>= 4 storeys or > 13 m -> apartment_midrise; ground retail -> mixed_use; built < 1925, <= 4 storeys, < 400 m2 -> victorian; <= 450 m2 -> rowhouse_modern; else apartment_midrise / rowhouse_modern),
commercial (> 3 storeys or > 13 m -> office, else commercial_lowrise), vacant -> shed, size-based fallback. `cottage` = victorian, <= 2 storeys, <= 2 units. Parcel join is by footprint centroid inside the parcel / land-use polygon;
assessor rows join by parcel number, then by the row's own point inside the parcel. Port-owned Pier 70 parcels have no assessor rows (yearBuilt 0).

## Props, trees, landmarks

Props are deduped across sources (same kind within 1.5 m keeps the DataSF record and puts the other id in `altId`; bicycle_rental 15 m, bus_stop 12 m). Heading: OSM `direction` / `camera:direction`; stop / give_way / signals face the
approaching driver (way geometry and `direction=forward|backward`); crossings are perpendicular to the road; DataSF stop signs use `st_facing`; ways use the long axis of their outline; everything else faces the nearest
non-path road centreline (`distToRoad`, `nearestRoad`). `headingSource` says which. Cranes, smokestacks and similar nodes have `headingKnown=false`. OSM signals and stop signs sit on the road centreline (`tags onRoad=yes`);
`dsNearby=m` marks ones that also have a curbside DataSF record. Trees: DataSF inventory (empty planting sites and stumps dropped) plus OSM trees not within 2 m of a DataSF tree; the flat `trees` array holds the xz of every tree record.
