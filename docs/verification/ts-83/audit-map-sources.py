import bpy, hashlib, json
from pathlib import Path
root = Path.cwd()
for name in ('terrain-sources.json', 'surface-sources.json', 'structures-sources.json'):
    base = root / 'assets/maps/infield'
    for path, expected in json.loads((base / name).read_text())['files'].items():
        assert hashlib.sha256((base/path).read_bytes()).hexdigest() == expected, path
    print('PASS manifest:', name)
base = root / 'assets/maps/oval'
manifest = json.loads((base/'sources.json').read_text())['assets'][0]
assert hashlib.sha256((base/manifest['retained_source']).read_bytes()).hexdigest() == manifest['sha256']
for entry in json.loads((base/'environment-sources.json').read_text())['files']:
    if entry['path'].endswith(('.blend', '.glb', '.png')):
        assert hashlib.sha256((base/entry['path']).read_bytes()).hexdigest() == entry['sha256'], entry['path']
print('PASS oval retained master/environment asset hashes')
for path in ('assets/maps/oval/source/OvalFoundation.blend', 'assets/maps/oval/source/OvalEnvironment.blend',
             'assets/maps/infield/source/InfieldTerrain.blend', 'assets/maps/infield/source/InfieldStructures.blend'):
    bpy.ops.wm.open_mainfile(filepath=str(root/path))
    meshes = [obj for obj in bpy.data.objects if obj.type == 'MESH']
    assert meshes and bpy.context.scene.unit_settings.scale_length == 1
    print('PASS reopen:', path, 'meshes=',len(meshes), 'vertices=',sum(len(obj.data.vertices) for obj in meshes))
print('Map source audit passed; no regeneration or writes performed.')
