"""Read-only Blender source and glTF audit; run after export/import and before delivery."""
import hashlib
import json
import math
import struct
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parent
manifest = json.loads((ROOT/'library.json').read_text())
for path, expected in manifest['files'].items():
    assert hashlib.sha256((ROOT/path).read_bytes()).hexdigest() == expected, path+' checksum mismatch'
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'source/EnvironmentLibrary.blend'))
assert bpy.context.scene.unit_settings.scale_length == 1
assert bpy.context.scene.unit_settings.system == 'METRIC'
mesh_count = 0
for entry in manifest['assets']:
    collection = bpy.data.collections[entry['id']]
    if entry['id'].startswith(('Boulder','Rock')):
        assert abs(entry['blender_bounds'][2][0]) < .001, entry['id']+' floating base'
    assert collection.asset_data is not None, entry['id']+' missing asset-browser collection'
    colliders = [obj for obj in collection.objects if obj.name.endswith('-convcolonly')]
    assert len(colliders) == entry['collision_parts'], entry['id']+' collision count'
    for obj in collection.objects:
        assert all(abs(v-1) < .00001 for v in obj.scale), obj.name+' scale'
        assert obj.location.length < .00001, obj.name+' pivot'
        assert obj.rotation_euler.to_matrix().is_identity, obj.name+' rotation'
        if obj.type != 'MESH':
            continue
        mesh_count += 1
        assert all(math.isfinite(c) for vertex in obj.data.vertices for c in vertex.co)
        if obj not in colliders:
            assert obj.data.uv_layers, obj.name+' missing UVs'
            assert obj.data.materials, obj.name+' missing materials'
            for uv in obj.data.uv_layers.active.uv:
                assert all(math.isfinite(c) and -.001 <= c <= 1.001 for c in uv.vector), obj.name+' invalid UV island'
    data = (ROOT/entry['model']).read_bytes()
    magic, version, size = struct.unpack_from('<III',data)
    assert magic == 0x46546C67 and version == 2 and size == len(data)
    length = struct.unpack_from('<I',data,12)[0]
    gltf = json.loads(data[20:20+length])
    assert not gltf.get('animations')
    for material in gltf.get('materials',[]):
        assert material.get('alphaMode','OPAQUE') == 'OPAQUE', 'Unbudgeted transparent material'
print(f"Blender source audit passed: {len(manifest['assets'])} asset collections, {mesh_count} editable meshes, metre pivots, UVs, materials, collision sources, GLB structure and hashes.")
for name in ['StoneAlbedo','StoneNormal']:
    assert bpy.data.images[name].packed_file, name+' missing packed source texture'
print('Original stone textures packed in editable Blender source.')
