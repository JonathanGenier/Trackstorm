"""Reopen the editable master and verify retained authoring data and manifest bytes."""
from pathlib import Path
import hashlib
import json
import bpy

root = Path(__file__).resolve().parents[1]
manifest = json.loads((root/'sources.json').read_text())
for name, expected in manifest['files'].items():
    assert hashlib.sha256((root/name).read_bytes()).hexdigest() == expected, name
bpy.ops.wm.open_mainfile(filepath=str(root/'source/CarnivalCrate.blend'))
crates = [bpy.data.objects[name] for name in ['CrateLeft', 'CrateRight', 'CrateTop']]
assert all(c.parent.name == 'CarnivalCrateStack' for c in crates)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
assert len(meshes) > 500, 'Source must retain individually editable components'
assert all(o.data.uv_layers for o in meshes), 'Missing authored UVs'
images = [i for i in bpy.data.images if i.type == 'IMAGE']
assert all(i.packed_file for i in images), 'Texture depends on an external path'
assert len(images) == 9, 'Six albedos and three wood normal maps'
assert not any(o.rigid_body for o in meshes)
report = {'sourceMeshes': len(meshes), 'packedTextures': len(images),
          'crateGroups': [c.name for c in crates], 'manifestHashes': 'PASS',
          'editableUVs': 'PASS', 'rigidBodies': 0}
output = root.parents[2]/'.godot/ts-310/asset-audit.json'
output.write_text(json.dumps(report, indent=2)+'\n')
print('Carnival asset audit passed:', report)
