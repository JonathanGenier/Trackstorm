"""Offline Blender authoring. Run with blender --background --python this_file.

The supplied master is the geometry authority, not a procedural banking formula.
Source coordinates and section topology are retained exactly; only triangulation,
materials, a boundary-matched flat infield and usable grid markings are authored.
"""
import hashlib
import json
import math
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
MASTER = ROOT / "source/a3efed07-c338-4d10-b122-91356975b5d5-r2.blend"
bpy.ops.wm.open_mainfile(filepath=str(MASTER))
bpy.context.scene.unit_settings.system = "METRIC"
bpy.context.scene.unit_settings.scale_length = 1.0
original = bpy.data.objects["TrackSurface_18m"]
vertices = [original.matrix_world @ v.co for v in original.data.vertices]
sections = len(vertices) // 2
centers = [(vertices[2*i] + vertices[2*i+1]) / 2 for i in range(sections)]
widths = [(vertices[2*i+1] - vertices[2*i]).length for i in range(sections)]
assert sections == 916 and max(abs(w - 18) for w in widths) < 0.0001
assert max(abs(vertices[2*i].z) for i in range(sections)) == 0
faces = []
road_vertices = list(vertices)
rows = []
for i in range(sections):
    row = [2*i]
    for across in range(1, 6):
        row.append(len(road_vertices))
        road_vertices.append(vertices[2*i].lerp(vertices[2*i+1], across/6))
    rows.append(row + [2*i+1])
for i in range(sections):
    for across in range(6):
        a, d = rows[i][across:across+2]
        b, c = rows[(i+1) % sections][across:across+2]
        # Correct the master's downward winding. Six strips reduce diagonal
        # faceting on twisting quads without changing any source section.
        faces.extend([(a, c, b), (a, d, c)])

# Remove delivery graphics, placeholder slab, barrier, lights and camera.
# A clean reference car remains in a separate export for the verification fixture.
reference = [o for o in bpy.data.objects if o.name.startswith("ReferenceCar_")]
for obj in list(bpy.data.objects):
    if obj not in reference:
        bpy.data.objects.remove(obj, do_unlink=True)
for collection in list(bpy.data.collections):
    if not collection.objects:
        bpy.data.collections.remove(collection)
foundation = bpy.data.collections.new("OvalFoundation")
bpy.context.scene.collection.children.link(foundation)


def material(name, color):
    result = bpy.data.materials.new(name)
    result.diffuse_color = (*color, 1)
    bsdf = result.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = 0.9
    return result


def mesh(name, coords, polygons, surface, smooth=False):
    data = bpy.data.meshes.new(name)
    data.from_pydata(coords, [], polygons)
    data.update()
    assert not data.validate()
    result = bpy.data.objects.new(name, data)
    foundation.objects.link(result)
    data.materials.append(surface)
    for polygon in data.polygons:
        polygon.use_smooth = smooth
        assert polygon.normal.z > 0, f"Inverted face in {name}"
    return result


track = mesh("Track", road_vertices, faces, material("FoundationAsphalt", (0.12, 0.14, 0.16)), True)
track["source_object"] = "TrackSurface_18m"
track["surface_width_m"] = 18.0
# The exact inner rim is planar. Shared boundary coordinates prevent a step/gap.
infield_vertices = [Vector((0, 0, 0))] + vertices[::2]
infield_faces = [(0, i+1, (i+1) % sections+1) for i in range(sections)]
infield = mesh("Infield", infield_vertices, infield_faces, material("FoundationInfield", (0.28, 0.32, 0.24)))

# The master's original grid reaches into the banking transition. Put all eight
# slots on the same flat straight, facing +X, with 8 m row pitch and 10 m lanes.
paint_vertices, paint_faces, spawns = [], [], []


def rectangle(x0, y0, x1, y1):
    start = len(paint_vertices)
    paint_vertices.extend([(x0, y0, 0.012), (x1, y0, 0.012), (x1, y1, 0.012), (x0, y1, 0.012)])
    paint_faces.extend([(start, start+1, start+2), (start, start+2, start+3)])


for row in range(4):
    for lane in range(2):
        x, y = -8.0 * row, -96.0 + 10.0 * lane
        slot = len(spawns) + 1
        spawns.append({"id": f"player-{slot:02}", "position": [x, 0.85, -y], "yaw": -math.pi/2,
                       "length_m": 6, "width_m": 3})
        for bounds in [(x-3, y-1.5, x+3, y-1.4), (x-3, y+1.4, x+3, y+1.5),
                       (x-3, y-1.4, x-2.9, y+1.4), (x+2.9, y-1.4, x+3, y+1.4)]:
            rectangle(*bounds)
        marker = bpy.data.objects.new(f"player-{slot:02}", None)
        foundation.objects.link(marker)
        marker.location = (x, y, 0.85)
        marker.empty_display_type = "ARROWS"
        marker["slot_length_m"], marker["slot_width_m"] = 6, 3
grid = mesh("GridMarkings", paint_vertices, paint_faces, material("FoundationGrid", (0.85, 0.83, 0.68)))

# Keep the reference at slot 1 in the source, but exclude it from map geometry.
for obj in reference:
    obj.location.x += 68


def export(filename, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(filepath=str(ROOT / filename), export_format="GLB", use_selection=True,
                              export_yup=True, export_apply=True, export_animations=False, export_extras=False)


export("oval_foundation.glb", [track, infield, grid])
export("reference_vehicle.glb", reference)
bpy.ops.object.select_all(action="DESELECT")
track.select_set(True)
bpy.context.view_layer.objects.active = track
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / "source/OvalFoundation.blend"))

plan_lap = sum((centers[(i+1) % sections].xy - centers[i].xy).length for i in range(sections))
surface_lap = sum((centers[(i+1) % sections] - centers[i]).length for i in range(sections))
turns = [c for c in centers if abs(c.x) > 107.0001]
radii = [math.hypot(abs(c.x)-107, c.y) for c in turns]
assert abs(plan_lap - 999.77) < 0.01
assert max(abs(r-91) for r in radii) < 0.0001
assert abs(max(v.z for v in vertices)-10.324) < 0.001
report = {
    "master_sha256": hashlib.sha256(MASTER.read_bytes()).hexdigest(),
    "master_object": "TrackSurface_18m", "master_vertices_preserved": True,
    "blender_version": bpy.app.version_string, "meters_per_unit": 1,
    "section_count": sections, "track_triangles": len(faces), "infield_triangles": len(infield_faces),
    "surface_width_min_max_m": [min(widths), max(widths)], "straight_length_m": 214,
    "centerline_radius_min_max_m": [min(radii), max(radii)], "plan_lap_m": plan_lap,
    "surface_centerline_lap_m": surface_lap,
    "horizontal_footprint_m": [max(v.x for v in vertices)-min(v.x for v in vertices),
                               max(v.y for v in vertices)-min(v.y for v in vertices)],
    "outside_edge_rise_m": max(v.z for v in vertices),
    "full_bank_degrees": math.degrees(math.asin(max(v.z for v in vertices)/18)),
    "grid": spawns,
    "sections_godot": [[list((v.x, v.z, -v.y)) for v in vertices[i:i+2]] for i in range(0, len(vertices), 2)],
}
(ROOT / "measurements.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print("Oval authored: " + json.dumps({k: v for k, v in report.items() if k not in ("sections_godot", "grid")}))
