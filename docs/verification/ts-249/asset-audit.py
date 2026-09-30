from pathlib import Path
import bpy, json, hashlib, struct
root=Path.cwd()/'assets/items/proxy-mine'
manifest=json.loads((root/'sources.json').read_text())
for name,digest in manifest['sha256'].items():
    assert hashlib.sha256((root/name).read_bytes()).hexdigest()==digest, name
data=(root/'ProxyMine.glb').read_bytes()
magic,version,length=struct.unpack_from('<III',data)
assert magic==0x46546c67 and version==2 and length==len(data)
json_length,kind=struct.unpack_from('<II',data,12)
gltf=json.loads(data[20:20+json_length])
assert not gltf.get('animations') and not gltf.get('cameras')
assert len(gltf['meshes'])==2
assert {n['name'] for n in gltf['nodes']}=={'ArmoredBody','BeaconLens'}
assert all('uri' not in b for b in gltf['buffers'])
assert all('bufferView' in image for image in gltf['images'])
bpy.ops.wm.open_mainfile(filepath=str(root/'source/ProxyMine.blend'))
source_meshes=len([o for o in bpy.context.scene.objects if o.type=='MESH'])
assert source_meshes>90
assert any(o.modifiers for o in bpy.context.scene.objects)
assert all(image.packed_file for image in bpy.data.images if image.type=='IMAGE')
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(root/'ProxyMine.glb'))
objects=[o for o in bpy.context.scene.objects if o.type=='MESH']
points=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
radius=max((p.x*p.x+p.y*p.y)**.5 for p in points)
bottom=min(p.z for p in points)
top=max(p.z for p in points)
for o in objects: o.data.calc_loop_triangles()
triangles=sum(len(o.data.loop_triangles) for o in objects)
assert radius<=.65 and abs(bottom+.25)<.00001 and top<.35
assert triangles==manifest['audit']['triangles']
result={'checksums':'all five match','source_editable_meshes':source_meshes,
        'source_modifiers_and_packed_textures':True,'glb_nodes':[o.name for o in objects],
        'triangles':triangles,'radius':radius,'bottom':bottom,'top':top,
        'no_external_glb_dependencies':True,'no_colliders_or_animation':True}
(Path.cwd()/'.godot/ts249-asset-audit.json').write_text(json.dumps(result,indent=2))
print('PROXY_MINE_INDEPENDENT_ASSET_AUDIT_PASS '+json.dumps(result))
