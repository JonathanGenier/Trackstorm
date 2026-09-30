"""Independent reopening, GLB reimport and provenance check; run from repo root."""
from pathlib import Path
import bpy, json, struct, hashlib
root=Path.cwd()/'assets/items/proxy-mine'
manifest=json.loads((root/'sources.json').read_text())
for path,digest in manifest['sha256'].items():
    assert hashlib.sha256((root/path).read_bytes()).hexdigest()==digest,path
bpy.ops.wm.open_mainfile(filepath=str(root/'source/ProxyMinePlacementArm.blend'))
source=[o for o in bpy.data.objects if o.type=='MESH']
assert len(source)==97
assert all(o.modifiers for o in source)
assert len([i for i in bpy.data.images if i.packed_file])==5
data=(root/'ProxyMinePlacementArm.glb').read_bytes()
magic,version,length=struct.unpack_from('<III',data)
assert magic==0x46546c67 and version==2 and length==len(data)
size,kind=struct.unpack_from('<II',data,12)
gltf=json.loads(data[20:20+size])
assert len(gltf['meshes'])==8 and len(gltf['materials'])==5
assert not gltf.get('animations') and not gltf.get('cameras')
assert all('uri' not in b for b in gltf['buffers'])
assert all('bufferView' in i for i in gltf['images'])
assert all(not any(mark in node.get('name','').lower() for mark in ['colonly','collision','rigid']) for node in gltf['nodes'])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(root/'ProxyMinePlacementArm.glb'))
meshes=[o for o in bpy.data.objects if o.type=='MESH']
for o in meshes:o.data.calc_loop_triangles()
triangles=sum(len(o.data.loop_triangles) for o in meshes)
for name in ['Mount','Upper','Forearm','Wrist','JawLeft','JawRight']:
    assert bpy.data.objects.get(name),name
result={'sha256_checked':len(manifest['sha256']),'editable_meshes':len(source),'packed_original_maps':5,
        'rigid_runtime_meshes':len(meshes),'triangles':triangles,'material_surfaces':sum(len(m.data.materials) for m in meshes),
        'no_external_dependencies':True,'articulation_pivots_present':True,'no_collision_names':True}
(Path.cwd()/'.godot/ts249-arm-asset-audit.json').write_text(json.dumps(result,indent=2))
print('TS249_ARM_ASSET_AUDIT_PASS '+json.dumps(result))
