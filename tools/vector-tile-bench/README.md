# Vector tile bench (POC for lagebuch-geodata#5)

These are measuring tools and are not part of `LageBuch.sln`. They compare three ways of getting a 256 px map tile on screen, using the same tiles for each:

| Candidate | What is timed |
|---|---|
| raster PNG decode | `map.mbtiles` tile → Avalonia `Bitmap`. This is today's path. |
| vector, ours | PMTiles read + gunzip, `MvtTile.Decode`, then `VectorTileRenderer.Render`, Avalonia on Skia. A second row times the render alone, as on a warm decode cache. |
| VexTile | `VexTile.Renderer.Mvt.AliFlux` 1.0.2, the renderer Mapsui uses, with the style `protomaps-carto-lite.json`. |

VexTile runs in a process of its own because it pins SkiaSharp 2.88 and Avalonia 12 loads 3.119.

## Data

```bash
pmtiles extract https://build.protomaps.com/<yyyymmdd>.pmtiles ffb.pmtiles \
  --region=boundaries/ffb.geojson --maxzoom=15     # boundary from lagebuch-geodata
gh release download ffb-v2 -R CodeForFire/lagebuch-geodata && unzip ffb-v2.zip -d ffb-v2
```

`pmtiles` is [go-pmtiles](https://github.com/protomaps/go-pmtiles).

## Run

```bash
tools/vector-tile-bench/run.sh ffb-v2/map.mbtiles ffb.pmtiles out/ 1500
```

The script:
- picks at most 1500 tiles per zoom (13, 15, 17) from the raster pack's own coverage;
- writes the tile lists, `out/results.md`, and 3×3-tile PNGs around Fürstenfeldbruck for each renderer.

The style is the app's `VectorTileRenderer` style, rewritten for VexTile by `VexTileBench/make_style.py`; rerun the script after changing either. VexTile evaluates a 256 px tile's style two zoom levels low, and the generated style allows for that.

The app draws a pack's `map.pmtiles` whenever the installed pack has one; there is nothing to switch on.
