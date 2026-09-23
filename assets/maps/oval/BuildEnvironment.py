"""Original TS-100 conifer meshes and grass maps; Blender metres, deterministic seed.
Run Blender --background --python-exit-code 1 --python assets/maps/oval/BuildEnvironment.py.
Foundation geometry is read-only and remains owned by BuildOval.py.
"""
import bpy
import math
import random
import numpy as np
from pathlib import Path
from mathutils import Vector
ROOT = Path(__file__).resolve().parent
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1
rng = random.Random(100)
materials = []
for name, color in [('Bark', (.16,.105,.064)), ('NeedlesDark', (.038,.095,.047)), ('Needles', (.067,.16,.078)), ('NeedlesSun', (.11,.22,.095))]:
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color,1)
    m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (*color,1)
    m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .94
    materials.append(m)
for variant in range(3):
    verts, faces, slots = [], [], []
    def cone(center, radius, height, sides, material, phase=0):
        start = len(verts)
        x,y,z = center
        for i in range(sides):
            angle = 2*math.pi*i/sides + phase
            r = radius*rng.uniform(.83,1.13)
            verts.append((x+math.cos(angle)*r,y+math.sin(angle)*r,z+rng.uniform(-.12,.12)))
        verts.append((x,y,z+height))
        for i in range(sides):
            faces.append((start+i,start+(i+1)%sides,start+sides)); slots.append(material)
        faces.append(tuple(start+i for i in reversed(range(sides)))); slots.append(material)
    height = 13 + variant*2
    cone((0,0,0),.24,height,9,0)
    for tier in range(11):
        z = 2.2+tier*(height-3)/11
        radius = (height-z)*.24
        cone((0,0,z),radius,.9+radius*.65,11,1+tier%3,rng.random()*6.28)
        # Separate hanging branch tips break up the silhouette, rather than stacked smooth cones.
        for branch in range(8):
            angle = branch*math.tau/8+tier*2.4
            reach = radius*.66
            cone((math.cos(angle)*reach,math.sin(angle)*reach,z-.3),radius*.43,radius*.9,6,1+rng.randrange(3),angle)
    cone((0,0,height-1.7),.65,2.2,9,2)
    mesh=bpy.data.meshes.new('Conifer'+str(variant)); mesh.from_pydata(verts,[],faces); mesh.update()
    obj=bpy.data.objects.new('Conifer'+str(variant),mesh); bpy.context.collection.objects.link(obj)
    for m in materials: mesh.materials.append(m)
    for p,slot in zip(mesh.polygons,slots): p.material_index=slot
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/OvalEnvironment.blend'))
bpy.ops.export_scene.gltf(filepath=str(ROOT/'conifers.glb'), export_format='GLB', export_yup=True, export_animations=False)
# Original seamless 2 m grass patch with fine blade detail, soil flecks and broad variation.
size=1024
nr=np.random.default_rng(100)
y,x=np.mgrid[0:size,0:size]
base=.5+nr.random((size,size))*.16
for i in range(34000):
    xx,yy=nr.integers(0,size,2); length=int(nr.integers(3,14)); slope=nr.uniform(-.5,.5)
    shade=nr.uniform(.18,.94)
    for step in range(length): base[(yy+step)%size,(xx+int(step*slope))%size]=shade
rgb=np.stack((.14+base*.19,.18+base*.24,.055+base*.095),axis=-1)
def save(name,values):
    image=bpy.data.images.new(name,width=size,height=size,alpha=True)
    rgba=np.ones((size,size,4),dtype=np.float32); rgba[:,:,:3]=values
    image.pixels.foreach_set(rgba.ravel()); image.filepath_raw=str(ROOT/(name+'.png')); image.file_format='PNG'; image.save()
save('grass_albedo',rgb)
dx=(np.roll(base,-1,axis=1)-np.roll(base,1,axis=1))*.8
dy=(np.roll(base,-1,axis=0)-np.roll(base,1,axis=0))*.8
normal=np.stack((-dx,-dy,np.ones_like(base)),axis=-1); normal/=np.linalg.norm(normal,axis=-1)[:,:,None]
save('grass_normal',normal*.5+.5)
# A separate 64 m macro layer breaks up distant repetition without enlarging grain/blades.
macro=np.zeros((size,size))
for cells,weight in [(8,.5),(16,.3),(32,.15),(64,.05)]:
    grid=nr.random((cells,cells))
    coordinate=np.arange(size)*cells/size
    index=coordinate.astype(int); fraction=coordinate-index
    fraction=fraction*fraction*(3-2*fraction)
    horizontal=grid[:,index]*(1-fraction)+grid[:,(index+1)%cells]*fraction
    macro+=(horizontal[index,:]*(1-fraction[:,None])+horizontal[(index+1)%cells,:]*fraction[:,None])*weight
macro=.55+macro*.45
save('ground_macro',np.repeat(macro[:,:,None],3,axis=2))
print('Authored three conifers and original seamless grass maps.')
