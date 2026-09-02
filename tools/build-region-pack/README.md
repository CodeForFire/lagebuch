# Building a Wasserförderung region pack

This is a runbook, not an app component. It documents how a maintainer builds and publishes one
region pack (one Landkreis) for the Wasserförderung "Karte" map mode (#150 follow-up). It is
**deliberately not part of `LageBuch.sln`** — it doesn't ship with the app, doesn't run in the
app's CI, and isn't built/tested by `dotnet build`/`dotnet test`. It's a manual, occasional process
a maintainer runs on their own machine with real internet access, using off-the-shelf OSS tools.

The app itself only ever *downloads* a pack someone already built this way — see
`src/LageBuch.AppLogic/Services/IRegionPackCatalogService.cs` and `IRegionPackInstaller.cs`.

## What a pack contains

A zip with exactly two files at its root:

- `region.mbtiles` — raster map tiles, standard MBTiles SQLite schema, TMS row numbering, zoom
  11–15. Read by `src/LageBuch.AppLogic/Services/MbTilesFileSource.cs`.
- `region.dem` — elevation grid in this app's own binary format ("FWDM"): little-endian header
  (magic, version, origin lat/lon, cell-size-degrees, rows, cols) + row-major `Int16` body,
  `-32768` = NoData. See `src/LageBuch.AppLogic/Services/DemFileElevationSampler.cs`'s doc comment
  for the exact byte layout. SRTM's own Skadi `.hgt` format is nearly identical (same NoData
  sentinel, same 1-arcsecond grid) — the only real conversion is a byte order swap and writing our
  small header.

## Licensing (read this before publishing anything)

- **OpenStreetMap data (ODbL 1.0)**: rendered raster tiles are a "Produced Work", not a
  "Derivative Database" — see the OSM Foundation's Legal FAQ. Produced Works need **attribution
  only** ("© OpenStreetMap contributors", linking to the ODbL/copyright page); share-alike does
  **not** apply. If a future pack ever ships raw/vector OSM data instead of flattened raster
  images, revisit this — vector tiles with queryable attributes are much closer to a Derivative
  Database and ODbL's share-alike would likely apply.
- **SRTM elevation (NASA/USGS)**: US federal government work, public domain, freely
  redistributable. No legal attribution requirement (a courtesy credit is still included below).
- **Copernicus DEM**: higher resolution than SRTM and tempting as a future upgrade, but its exact
  redistribution terms for derived/cropped public extracts have **not been confirmed**. Don't use
  it for a published pack until someone has actually read the license and confirmed this is
  permitted.
- Every published pack's `regions.json` entry carries an `attribution` string (see below) that the
  app surfaces directly under the installed region in Stammdaten → Einsatzgebiet. Keep that string
  accurate for what the pack actually contains.

## Prerequisites

- [`osmium-tool`](https://osmcode.org/osmium-tool/) — extracts a bounded region from a larger
  `.osm.pbf` without needing a database.
- A raster tile renderer that reads `.osm`/`.pbf` directly and needs no database setup — e.g.
  [Maperitive](http://maperitive.net/) (single executable, built-in default style) or
  [Mapnik](https://mapnik.org/) + the classic `generate_tiles.py` script. Pin whichever is actually
  available and maintained at the time you run this — don't assume either is still the best choice.
- `sqlite3` CLI (to assemble/inspect the `.mbtiles` file — most tile renderers write this format
  natively; if yours doesn't, `mbutil` converts a tile directory into one).
- `curl` and `gzip` (SRTM `.hgt.gz` download).
- A `.NET` SDK is **not** required for any of this — none of it touches `LageBuch.sln`.

## 1. Find the region's exact boundary

Look up the Landkreis's OSM relation and precise administrative boundary via Nominatim:

```sh
curl -s "https://nominatim.openstreetmap.org/lookup?osm_ids=R<relation-id>&format=json&polygon_geojson=1" \
  -o boundary.json
```

For Landkreis Fürstenfeldbruck this is relation **62595**, bounding box
**48.0877067,10.9930275 – 48.2967233,11.4128816**.

## 2. Extract the region from a Geofabrik regional extract

Download the containing regional `.osm.pbf` from [Geofabrik](https://download.geofabrik.de/)
(e.g. `bayern-latest.osm.pbf` for a Bavarian Landkreis), then clip to the precise boundary:

```sh
osmium extract --polygon boundary.geojson bayern-latest.osm.pbf -o ffb.osm.pbf
```

(`boundary.geojson` is `boundary.json`'s `geojson` field extracted into its own file — osmium wants
a bare GeoJSON polygon, not the full Nominatim response.)

## 3. Render raster tiles, zoom 11–15

Render directly from `ffb.osm.pbf` with whichever tool you picked above, output as a directory of
`{z}/{x}/{y}.png` tiles or directly as MBTiles if the tool supports it. Zoom 11–15 is enough detail
for route planning at Landkreis scale without an unreasonably large pack.

## 4. Pack into `region.mbtiles`

If your renderer didn't write MBTiles directly, pack the tile directory with `mbutil`:

```sh
mb-util --image_format=png tiles/ region.mbtiles
```

`MbTilesFileSource.cs` expects the standard schema (`tiles(zoom_level, tile_column, tile_row,
tile_data)`) with **TMS row numbering** (row 0 = south) — this is what `mb-util` and most tile
tools produce natively; no manual row-flipping should be needed.

## 5. Build `region.dem` from SRTM

Download the SRTM `.hgt.gz` tiles covering the bbox (+2 km padding) from the public
`elevation-tiles-prod` S3 bucket:

```sh
curl -sO https://s3.amazonaws.com/elevation-tiles-prod/skadi/N48/N48E010.hgt.gz
curl -sO https://s3.amazonaws.com/elevation-tiles-prod/skadi/N48/N48E011.hgt.gz
gunzip N48E010.hgt.gz N48E011.hgt.gz
```

Mosaic adjacent tiles (de-duplicating the shared edge column/row between neighboring 1°×1° tiles),
then convert to `region.dem`:

- SRTM `.hgt` is big-endian `Int16`, 1 arcsecond/cell (3601×3601 per tile), `-32768` = void. Our
  format is little-endian with the same cell size and the same NoData sentinel — no resampling
  needed, just a byte-swap and prepending our 40-byte header (magic `FWDM`, version, origin
  lat/lon, cell-size-degrees, row count, column count — see `DemFileElevationSampler.cs`).
- `build-dem.py` (or equivalent) is intentionally left as a short script you write against the
  exact header layout in that file at the time you run this — the format is simple enough that
  hand-rolling a ~30-line converter beats depending on a library for a once-per-pack step.

## 6. Zip and publish

```sh
zip ffb.zip region.mbtiles region.dem
```

Publish as a GitHub Release asset in the region-pack repo (recommended: a separate repo, e.g.
`CodeForFire/lagebuch-regions`, kept apart from this app's own release history so a pack update is
never confused with an app version) — one tag per pack, e.g. `ffb-v1`.

Add (or update) the pack's entry in that repo's `regions.json`, served raw at
`https://raw.githubusercontent.com/CodeForFire/lagebuch-regions/main/regions.json` (this exact URL
is `CompositionRoot.RegionPackManifestUrl` in this repo — update both if the manifest ever moves):

```json
{
  "name": "Landkreis Fürstenfeldbruck",
  "slug": "ffb",
  "downloadUrl": "https://github.com/CodeForFire/lagebuch-regions/releases/download/ffb-v1/ffb.zip",
  "sizeBytes": 12345678,
  "minLat": 48.0877067,
  "minLon": 10.9930275,
  "maxLat": 48.2967233,
  "maxLon": 11.4128816,
  "builtAt": "2026-09-01",
  "attribution": "© OpenStreetMap contributors (ODbL). Höhendaten: SRTM (NASA/USGS, gemeinfrei)."
}
```

The app fetches this manifest, lists every entry in the Stammdaten → Einsatzgebiet dropdown, and
downloads/extracts whichever one the operator picks. No app code change is needed to publish a new
region — only a `regions.json` update.

## What this runbook does not cover

Actually running these steps for Landkreis Fürstenfeldbruck (~244 MB regional extract, real tile
rendering, real SRTM download) needs a machine with real internet access and takes a while — that's
a manual follow-up outside this repo/session, not something covered by `dotnet test`.
