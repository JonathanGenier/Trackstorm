"""Migrate the preserved Trackstorm design in Blender; export applied metre geometry."""
from pathlib import Path
import json
import bpy
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parent
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(ROOT / 'source/LegacyVehicle.glb'))
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
points = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
lo = Vector(tuple(min(p[i] for p in points) for i in range(3)))
hi = Vector(tuple(max(p[i] for p in points) for i in range(3)))
scale = 4.81 / (hi.y - lo.y)
# Godot Y maps to Blender Z. Retain a 0.9 m sprung origin-to-road distance.
shift = -0.9 - lo.z * scale
for o in meshes:
    transform = Matrix.Translation((0, 0, shift)) @ Matrix.Scale(scale, 4) @ o.matrix_world
    o.parent = None
    o.matrix_world = Matrix.Identity(4)
    o.data.transform(transform)
    o.data.update()
for o in list(bpy.context.scene.objects):
    if o.type != 'MESH':
        bpy.data.objects.remove(o, do_unlink=True)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1.0
# Runtime material binding reuses the original Godot triplanar resources. Export
# named material slots only, avoiding duplicate converted texture files.
for material in bpy.data.materials:
    if material.node_tree:
        for node in list(material.node_tree.nodes):
            if node.type == 'TEX_IMAGE':
                material.node_tree.nodes.remove(node)
for image in list(bpy.data.images):
    if image.users == 0:
        bpy.data.images.remove(image)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / 'source/WastelandVehicle.blend'))
bpy.ops.export_scene.gltf(filepath=str(ROOT / 'WastelandVehicle.glb'), export_format='GLB', export_yup=True)
measurements = {'scale': scale, 'vertical_origin_shift_m': shift, 'length_m': 4.81,
                'width_m': (hi.x-lo.x)*scale, 'height_m': (hi.z-lo.z)*scale,
                'wheelbase_m': 1.8954*scale, 'wheel_track_m': 1.19*scale,
                'wheel_radius_m': 0.354*scale, 'rest_origin_height_m': 0.9}
(ROOT / 'measurements.json').write_text(json.dumps(measurements, indent=2)+'\n')
print(measurements)
