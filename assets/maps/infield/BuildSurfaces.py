"""Author material field and UVs on the existing production blend without moving geometry.

Run after BuildTerrain.py when regenerating geometry. RGBA is linear data:
dirt coverage, soil saturation, rock coverage, designated water coverage.
"""
import hashlib
import json
from pathlib import Path
import bpy
import numpy as np

ROOT = Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'source/InfieldTerrain.blend'))
terrain = bpy.data.objects['InfieldTerrain-col']
mesh = terrain.data

def geometry_hash():
    vertices = np.empty(len(mesh.vertices)*3, dtype=np.float32)
    mesh.vertices.foreach_get('co', vertices)
    indices = np.empty(len(mesh.loops), dtype=np.int32)
    mesh.loops.foreach_get('vertex_index', indices)
    return hashlib.sha256(vertices.tobytes()+indices.tobytes()).hexdigest()

before = geometry_hash()
width, height = 1024, 512
x, z = np.meshgrid((np.arange(width)+.5)/width*512-256,
                   (np.arange(height)+.5)/height*256-128)

def smooth(t):
    t = np.clip(t, 0, 1)
    return t*t*(3-2*t)

distance = np.full(x.shape, 1000.)
layout = json.loads((ROOT/'layout.json').read_text())
for route in layout['routes']:
    for a, b in zip(route['points'][:-1], route['points'][1:]):
        dx, dz = b[0]-a[0], b[1]-a[1]
        t = np.clip(((x-a[0])*dx+(z-a[1])*dz)/(dx*dx+dz*dz), 0, 1)
        distance = np.minimum(distance, np.hypot(x-a[0]-t*dx,z-a[1]-t*dz)-route['width_m']/2)
# Disturbed earth beyond the lane edge; irregular but continuous grassy shoulders.
edge = .7*np.sin(x*.31)*np.cos(z*.43)+.35*np.sin(x*.91+z*.63)
dirt = 1-smooth((distance+edge)/7)
dirt = np.maximum(dirt, (1-smooth((np.abs(z)-10.5)/7))*smooth((np.abs(x)-8)/4)*smooth((125-np.abs(x))/6))
wet = np.zeros_like(x)
water = np.zeros_like(x)
for bx,bz,rx,rz in [(-57,-29,17,10),(77,-35,16,8),(-85,28,15,9),(85,28,15,9)]:
    radius = np.hypot((x-bx)/rx,(z-bz)/rz)+edge*.018
    soil = 1-smooth((radius-.35)/.7)
    dirt = np.maximum(dirt,1-smooth((radius-.85)/.5))
    wet = np.maximum(wet,soil)
    if (bx,bz)==(77,-35):
        water = 1-smooth((radius-.53)/.12)
# Exposed weathered shoulders on the existing side-loop hills, away from dirt routes.
rock = np.zeros_like(x)
for bx,bz in [(-145,-26),(145,-26),(-40,31),(40,31)]:
    rock = np.maximum(rock,1-smooth((np.hypot((x-bx)/10,(z-bz)/7)+edge*.05-.4)/.6))
rock *= 1-smooth(dirt)
pixels = np.stack([dirt,wet,rock,water],axis=-1).astype(np.float32)
image = bpy.data.images.get('SurfaceField') or bpy.data.images.new('SurfaceField',width=width,height=height,alpha=True)
image.colorspace_settings.name = 'Non-Color'
# Blender stores image rows bottom-up; Godot samples rows top-down.
image.pixels.foreach_set(np.flipud(pixels).copy().ravel())
image.filepath_raw = str(ROOT/'surface_field.png')
image.file_format = 'PNG'
image.save()
uv = mesh.uv_layers.get('SurfaceUV') or mesh.uv_layers.new(name='SurfaceUV')
for loop in mesh.loops:
    p = mesh.vertices[loop.vertex_index].co
    uv.data[loop.index].uv = ((p.x+256)/512,1-(-p.y+128)/256)
mesh.materials[0].name = 'SurfaceField_Dirt_Grass_Mud_DeepMud_Rock_Water'
assert geometry_hash() == before, 'Material authoring must not alter production geometry'
bpy.ops.object.select_all(action='DESELECT')
for obj in bpy.context.scene.objects:
    if obj.name.endswith('-col'):
        obj.hide_set(False)
        obj.select_set(True)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/InfieldTerrain.blend'))
bpy.ops.export_scene.gltf(filepath=str(ROOT/'infield_terrain.glb'),use_selection=True,export_format='GLB',export_yup=True,export_animations=False,export_extras=True)
report = dict(provenance='Original Trackstorm material field; no acquired assets.',
              bounds_xz=[-256,-128,512,256], channels=['Dirt','Wet soil','Rock','Water'],
              water_basin_xz=[77,-35], geometry_before_sha256=before,geometry_after_sha256=geometry_hash(),
              files={p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in ['surface_field.png','source/InfieldTerrain.blend','infield_terrain.glb']})
(ROOT/'surface-sources.json').write_text(json.dumps(report,indent=2)+'\n',newline='\n')
manifest=json.loads((ROOT/'terrain-sources.json').read_text())
for p in ['source/InfieldTerrain.blend','infield_terrain.glb']:
    manifest['files'][p]=report['files'][p]
(ROOT/'terrain-sources.json').write_text(json.dumps(manifest,indent=2)+'\n',newline='\n')
print(json.dumps(report))
