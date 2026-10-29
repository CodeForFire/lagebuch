"""Writes protomaps-carto-lite.json: the app's VectorTileRenderer style as a MapLibre style,
in the legacy filter syntax VexTile parses. VexTile evaluates a 256 px tile two zoom levels
below its own zoom, so every zoom here is the app's minus two."""
import json

SHIFT = -2


def z(app_zoom):
    return app_zoom + SHIFT


def width(at15):
    # The app: at15 * 1.6^(zoom - 15).
    return {"base": 1.6, "stops": [[z(11), at15 * 1.6 ** -4], [z(15), at15], [z(19), at15 * 1.6 ** 4]]}


LANDUSE = {
    "residential": "#e0dfdf", "industrial": "#ebdbe8", "commercial": "#f2dad9", "retail": "#ffd6d1",
    "farmland": "#eef0d5", "farmyard": "#f5dcba", "forest": "#add19e", "wood": "#add19e",
    "grass": "#cdebb0", "meadow": "#cdebb0", "park": "#c8facc", "garden": "#c8facc",
    "playground": "#dffce2", "pitch": "#88e0be", "cemetery": "#aacbaf", "allotments": "#c9e1bf",
    "school": "#ffffe5", "hospital": "#ffffe5", "pedestrian": "#dddde8", "railway": "#ebdbe8",
    "military": "#f2e4e4", "wetland": "#d6ebe0",
}

ROADS = [  # minor first, as the app draws them
    ("pedestrian", "#dddde8", "#999999", 4), ("service", "#ffffff", "#bbbbbb", 2.5),
    ("living_street", "#ededed", "#bbbbbb", 4.5), ("residential", "#ffffff", "#bbbbbb", 4.5),
    ("unclassified", "#ffffff", "#bbbbbb", 4.5), ("tertiary", "#ffffff", "#8f8f8f", 6),
    ("secondary", "#f7fabf", "#707d05", 6.5), ("primary", "#fcd6a4", "#a06b00", 7),
    ("trunk", "#f9b29c", "#c84e2f", 7), ("motorway", "#e892a2", "#dc2a67", 7),
]

layers = [{"id": "background", "type": "background", "paint": {"background-color": "#f2efe9"}}]
for kind, color in LANDUSE.items():
    layers.append({"id": f"landuse-{kind}", "type": "fill", "source": "protomaps", "source-layer": "landuse",
                   "filter": ["==", "kind", kind], "paint": {"fill-color": color}})
layers.append({"id": "water", "type": "fill", "source": "protomaps", "source-layer": "water",
               "filter": ["==", "$type", "Polygon"], "paint": {"fill-color": "#aad3df"}})
for kind, w in [("river", 5), ("canal", 4)]:
    layers.append({"id": f"water-{kind}", "type": "line", "source": "protomaps", "source-layer": "water",
                   "filter": ["all", ["==", "$type", "LineString"], ["==", "kind", kind]],
                   "paint": {"line-color": "#aad3df", "line-width": width(w)}})
layers.append({"id": "water-stream", "type": "line", "source": "protomaps", "source-layer": "water",
               "filter": ["all", ["==", "$type", "LineString"], ["!in", "kind", "river", "canal"]],
               "paint": {"line-color": "#aad3df", "line-width": width(1.5)}})
layers.append({"id": "buildings", "type": "fill", "source": "protomaps", "source-layer": "buildings", "minzoom": z(14),
               "paint": {"fill-color": "#d9d0c9"}})
layers.append({"id": "rail", "type": "line", "source": "protomaps", "source-layer": "roads",
               "filter": ["==", "kind", "rail"], "paint": {"line-color": "#707070", "line-width": 2}})
for detail, color, dash in [("track", "#996600", [2, 1]), ("cycleway", "#0000ff", [1, 1]),
                            ("footway", "#fa8072", [1, 1]), ("path", "#fa8072", [1, 1])]:
    layers.append({"id": f"way-{detail}", "type": "line", "source": "protomaps", "source-layer": "roads", "minzoom": z(14),
                   "filter": ["==", "kind_detail", detail],
                   "paint": {"line-color": color, "line-width": 1.2, "line-dasharray": dash}})
for detail, fill, casing, w in ROADS:
    layers.append({"id": f"road-{detail}-casing", "type": "line", "source": "protomaps", "source-layer": "roads",
                   "filter": ["all", ["==", "kind_detail", detail], ["!=", "is_tunnel", True]],
                   "layout": {"line-cap": "round", "line-join": "round"},
                   "paint": {"line-color": casing, "line-width": {"base": 1.6, "stops": [[s, v + 1.5] for s, v in width(w)["stops"]]}}})
for detail, fill, casing, w in ROADS:
    layers.append({"id": f"road-{detail}", "type": "line", "source": "protomaps", "source-layer": "roads",
                   "filter": ["all", ["==", "kind_detail", detail], ["!=", "is_tunnel", True]],
                   "layout": {"line-cap": "round", "line-join": "round"},
                   "paint": {"line-color": fill, "line-width": width(w)}})
layers.append({"id": "boundaries", "type": "line", "source": "protomaps", "source-layer": "boundaries",
               "paint": {"line-color": "#ac46ac", "line-width": 1.5, "line-dasharray": [4, 2]}})
label = {"text-color": "#333333", "text-halo-color": "#ffffff", "text-halo-width": 1.5}
layers.append({"id": "road-names", "type": "symbol", "source": "protomaps", "source-layer": "roads", "minzoom": z(15),
               "filter": ["in", "kind_detail", *[r[0] for r in ROADS]],
               "layout": {"text-field": "{name}", "text-font": ["Noto Sans Regular"], "text-size": 10, "symbol-placement": "line"},
               "paint": label})
layers.append({"id": "housenumbers", "type": "symbol", "source": "protomaps", "source-layer": "buildings", "minzoom": z(17),
               "filter": ["has", "addr_housenumber"],
               "layout": {"text-field": "{addr_housenumber}", "text-font": ["Noto Sans Regular"], "text-size": 9},
               "paint": {"text-color": "#666666"}})
layers.append({"id": "places", "type": "symbol", "source": "protomaps", "source-layer": "places",
               "filter": ["in", "kind", "locality", "neighbourhood", "macrohood"],
               "layout": {"text-field": "{name}", "text-font": ["Noto Sans Regular"], "text-size": 14},
               "paint": label})

style = {"version": 8, "name": "protomaps-carto-lite",
         "sources": {"protomaps": {"type": "vector", "url": "pmtiles://ffb.pmtiles"}},
         "layers": layers}
with open("protomaps-carto-lite.json", "w", encoding="utf-8") as f:
    json.dump(style, f, indent=1)
