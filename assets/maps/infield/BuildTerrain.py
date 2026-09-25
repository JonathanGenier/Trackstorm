"""Shape TS-74's unchanged metre-scale topology into editable production terrain.

Run with Blender --background --python-exit-code 1 --python this_file.
The retained graybox is the authoring baseline, not a runtime stacked floor.
"""
import hashlib
import json
from pathlib import Path

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parent
layout = json.loads((ROOT / 'layout.json').read_text())
sections = np.array(json.loads((ROOT.parent / 'oval/measurements.json').read_text())['sections_godot'])
bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'source/InfieldGraybox.blend'))
for obj in list(bpy.data.objects):
    if obj.name == 'LayoutFloor':
        bpy.data.objects.remove(obj, do_unlink=True)

# Concentric subdivisions retain EVERY exact oval inner-edge vertex, avoiding
# independently approximated boundaries. Radial spacing stays below one metre.
rim = sections[:, 0, :][:, [0, 2]]
count, rings = len(rim), 220
points = np.vstack([np.zeros((1, 2))] + [rim * (r / rings) for r in range(1, rings + 1)])
x, z = points.T


def smooth(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)


def mound(cx, cz, rx, rz, height):
    radius = np.sqrt(((x - cx) / rx)**2 + ((z - cz) / rz)**2)
    return height * (1 - smooth(radius))


# Broad rises and shallow valleys; central junction remains at its existing datum.
y = mound(-145, -26, 34, 28, 3.2) + mound(145, -26, 34, 28, 3.2)
y += mound(-48, -52, 38, 21, 2.4) + mound(48, -52, 38, 21, 2.4)
y += mound(-46, 31, 26, 22, 1.6) + mound(46, 31, 26, 22, 1.6)
y -= mound(-120, 30, 27, 20, .65) + mound(120, 30, 27, 20, .65)

basins = [(-57, -29, 17, 10), (77, -35, 16, 8), (-85, 28, 15, 9), (85, 28, 15, 9)]
for bx, bz, rx, rz in basins:
    y -= mound(bx, bz, rx, rz, 1.8)

# Broad low berms on the outside of the approved side-loop corners.
for side in [-1, 1]:
    y += mound(side * 166, -12, 15, 27, 2.1)
    # Three rounded rollers, with soft lateral shoulders and generous spacing.
    for cx in [72, 90, 108]:
        y += mound(side * cx, 48, 7, 12, .65)

# Integrated dirt tabletops. The entire 100 m reservation remains driveable slowly;
# at the intended 16 m/s the car leaves the preserved takeoff lip.
s = 125 - np.abs(x)
profile = np.zeros_like(x)
t = np.clip((s - 30) / 12, 0, 1)
kicker = (-2*t**3 + 3*t*t) * 4.8 + (t**3 - t*t) * 8.4
profile = np.where((s >= 30) & (s <= 42), kicker, profile)
t = np.clip((s - 42) / 13, 0, 1)
profile = np.where((s > 42) & (s < 55), 4.8 - 1.6 * smooth(t), profile)
t = np.clip((s - 55) / 30, 0, 1)
profile = np.where((s >= 55) & (s <= 85), 3.2 * (1 - smooth(t)), profile)
weight = 1 - smooth((np.abs(z) - 8) / 10)
y = y * (1 - weight * smooth((s - 20)/10) * (1-smooth((s-85)/10))) + profile * weight

# Preserve the original junction datum before applying approved end fills.
y *= smooth((np.maximum(np.abs(x)/28, np.abs(z)/23) - 1) / .4)

# Match the bank's inward derivative at the exact rim, then ease into terrain
# over twenty-eight metres. This shallow swale removes the old 35-degree grade break.
distance = np.linalg.norm(points, axis=1) * 0
bank_slope = np.zeros_like(x)
for r in range(1, rings + 1):
    start = 1 + (r-1)*count
    distance[start:start+count] = np.linalg.norm(rim, axis=1) * (1-r/rings)
    road = sections[:, 1, :] - sections[:, 0, :]
    radial = rim / np.linalg.norm(rim, axis=1)[:, None]
    bank_slope[start:start+count] = road[:, 1] / np.linalg.norm(road[:, [0, 2]], axis=1) * np.sum(radial * road[:, [0, 2]] / np.linalg.norm(road[:, [0, 2]], axis=1)[:, None], axis=1)
distance[0] = 100
blend = smooth(distance/28)
y = y * blend - bank_slope * distance * np.maximum(0, 1-distance/28)**2

# Approved TS-76 tabletop connection. Preserve every takeoff-face vertex and
# all terrain outside the two local end fills. Both lateral sides share the
# same rounded profile; only the outer five-metre collar follows existing land.
baseline_y = y.copy()
deck_height = 6.35
end_height = 4.8 + (deck_height - 4.8) * smooth((83 - np.abs(x)) / 13)
side_profile = (1 - smooth((np.abs(z) - 11.25) / 17)) ** 1.5
end_blend = smooth((np.abs(x) - 9) / 1.75) * smooth((83 - np.abs(x)) / 13)
# Narrow the far toe beside the kicker to preserve the adjacent basin approach.
toe_limit = 23.25 + 5 * smooth((83 - np.abs(x)) / 13)
end_blend *= 1 - smooth((np.abs(z) - (toe_limit - 5)) / 5)
# The existing southern hills meet these fills. Mirror their positive envelope
# into the authored toe so both main climbs match without cutting the old hill.
# Only the last five metres blend back to the asymmetric surrounding ground.
shoulder = np.zeros_like(x)
for sx in [-1, 1]:
    for sz in [-1, 1]:
        shoulder += mound(sx * 46, sz * 31, 26, 22, 1.6)
shoulder *= smooth((np.maximum(np.abs(x)/28, np.abs(z)/23) - 1) / .4)
profile_height = end_height * side_profile
radius = 2.0
fill = np.maximum(shoulder, profile_height) + np.maximum(radius - np.abs(profile_height - shoulder), 0)**2 / (4 * radius)
y = baseline_y * (1 - end_blend) + fill * end_blend
# TS-162: bridge each preserved 4.8 m kicker lip directly onto the 6.35 m
# tabletop, in this same continuous mesh/collider. Ease upward beyond the
# unchanged takeoff face without the former dip caused by blending to baseline.
lip_distance = np.clip(83 - np.abs(x), 0, 13)
fill_grade = .55
blend_length = 2 * (deck_height - 4.8) / fill_grade
rise_distance = np.minimum(lip_distance, blend_length)
lip_height = 4.8 + fill_grade * rise_distance - fill_grade * rise_distance**2 / (2 * blend_length)
# A bounded lateral grade adds material only where needed; multiplying the
# raised top by the old narrow kicker taper would steepen adjacent route edges.
lip_fill = lip_height - .45 * np.maximum(np.abs(z) - 6, 0)
y = np.where((np.abs(x) < 83) & (np.abs(x) >= 70) & (np.abs(z) < 28.25), np.maximum(y, lip_fill), y)
assert np.array_equal(y[np.abs(x) >= 83], baseline_y[np.abs(x) >= 83])
assert np.array_equal(y[np.abs(z) >= 28.25], baseline_y[np.abs(z) >= 28.25])

# Soft dirt-to-grass color transition follows the original routes and corridors.
route_distance = np.full(len(x), 1000.0)
for route in layout['routes']:
    path = np.array(route['points'])
    for a, b in zip(path[:-1], path[1:]):
        d = b-a
        t = np.clip(((points-a) @ d) / (d @ d), 0, 1)
        route_distance = np.minimum(route_distance, np.linalg.norm(points-a-t[:, None]*d, axis=1) - route['width_m']/2)
dirt = 1 - smooth(route_distance/7)
dirt = np.maximum(dirt, weight * ((s >= 0) & (s <= 100)))
dirt = np.maximum(dirt, end_blend)
for bx, bz, rx, rz in basins:
    dirt = np.maximum(dirt, 1-smooth((np.sqrt(((x-bx)/rx)**2+((z-bz)/rz)**2)-.8)/.5))
variation = 1 + .035*np.sin(x*.045 + np.sin(z*.06))*np.cos(z*.075)
colors = ((1-dirt[:, None])*np.array([.065, .11, .033]) + dirt[:, None]*np.array([.19,.105,.045])) * variation[:, None]
vertices = np.column_stack([x, -z, y]).tolist()
faces = [(0, 1+(i+1)%count, 1+i) for i in range(count)]
for r in range(1, rings):
    a, b = 1+(r-1)*count, 1+r*count
    for i in range(count):
        j = (i+1)%count
        faces.extend([(a+i, a+j, b+j), (a+i, b+j, b+i)])
data = bpy.data.meshes.new('Continuous infield metre terrain')
# Master sections traverse the boundary clockwise in Godot X/Z.
faces = [tuple(reversed(face)) for face in faces]
data.from_pydata(vertices, [], faces)
data.update()
terrain = bpy.data.objects.new('InfieldTerrain-col', data)
bpy.context.collection.objects.link(terrain)
for polygon in data.polygons:
    polygon.use_smooth = True
attribute = data.color_attributes.new(name='TerrainColor', type='FLOAT_COLOR', domain='POINT')
attribute.data.foreach_set('color', np.column_stack([colors,np.ones(len(x))]).astype(np.float32).ravel())
mat = bpy.data.materials.new('Natural dirt and grass terrain')
mat.use_nodes = True
bsdf = mat.node_tree.nodes.get('Principled BSDF')
bsdf.inputs['Roughness'].default_value = 1
color = mat.node_tree.nodes.new('ShaderNodeVertexColor')
color.layer_name = 'TerrainColor'
mat.node_tree.links.new(color.outputs['Color'], bsdf.inputs['Base Color'])
data.materials.append(mat)
bpy.ops.object.select_all(action='DESELECT')
for obj in bpy.context.scene.objects:
    if obj.name.endswith('-col'):
        obj.hide_set(False)
        obj.select_set(True)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/InfieldTerrain.blend'))
bpy.ops.export_scene.gltf(filepath=str(ROOT/'infield_terrain.glb'), use_selection=True, export_format='GLB', export_yup=True, export_animations=False, export_extras=True)
report = dict(units='metres', topology_sha256=hashlib.sha256((ROOT/'layout.json').read_bytes()).hexdigest(), vertices=len(vertices), triangles=len(faces), elevation_min_m=float(y.min()), elevation_max_m=float(y.max()), jump_target_speed_mps=16, basin_depth_m=1.8, boundary_vertices=count, transition_depth_m=28)
report['tabletop'] = dict(deck_height_m=deck_height, flat_half_width_m=11.25,
                         flat_outer_x_m=70, unchanged_kicker_lip_x_m=83,
                         symmetric_side_profile_to_z_m=23.25, blend_limit_z_m=28.25,
                         changed_vertices=int(np.count_nonzero(y != baseline_y)))
(ROOT/'terrain.json').write_text(json.dumps(report,indent=2)+'\n', newline='\n')
(ROOT/'terrain-sources.json').write_text(json.dumps(dict(provenance='Original Trackstorm Blender terrain derived from the approved TS-74 topology. No acquired assets.',files={p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in ['source/InfieldTerrain.blend','infield_terrain.glb','terrain.json','layout.json']}),indent=2)+'\n', newline='\n')
print(json.dumps(report))
