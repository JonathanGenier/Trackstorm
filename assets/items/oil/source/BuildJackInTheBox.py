import bpy, math, random
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1]
import numpy as np
noise=np.random.default_rng(245).random((256,256))
for k in [2,4,8]: noise += np.repeat(np.repeat(np.random.default_rng(245+k).random((256//k,256//k)),k,0),k,1)
noise=np.clip(noise/4,.22,1)
paint=np.stack([noise,noise,noise,np.ones_like(noise)],axis=-1).astype('float32')
patina=bpy.data.images.new('Original oil patina',width=256,height=256);patina.pixels.foreach_set(paint.ravel());patina.filepath_raw=str(root/'source/Patina.png');patina.file_format='PNG';patina.save();patina.pack()
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
random.seed(245)
def xyz(p): return (p[0],-p[2],p[1])
def mat(name,color,metal=0,rough=.5):
 m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
 tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=patina
 mix=m.node_tree.nodes.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=.8;mix.inputs[1].default_value=(*color,1);m.node_tree.links.new(tex.outputs['Color'],mix.inputs[2]);m.node_tree.links.new(mix.outputs[0],p.inputs['Base Color']);return m
red=mat('Weathered crimson',(.32,.018,.012),.25,.5);cream=mat('Aged ivory',(.58,.49,.32),.1,.62);black=mat('Black enamel',(.018,.012,.009),.2,.38);steel=mat('Worn iron',(.08,.065,.048),.8,.3);brass=mat('Old brass',(.33,.17,.035),.8,.3);white=mat('Teeth ivory',(.72,.63,.42),.1,.3)
def empty(name,pos,parent=None):
 o=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(o);o.location=xyz(pos);o.parent=parent;return o
box=empty('OilBox',(0,0,0));lid=empty('LidPivot',(0,.68,-.39),box);head=empty('JesterPivot',(0,1.42,0),box);spring=empty('Spring',(0,.7,0),box)
def finish(o,name,material,parent,pos):
 o.name=name;o.data.materials.append(material);o.parent=parent;o.location=xyz(pos);return o
def cube(name,pos,size,material,parent=box,bevel=.015):
 bpy.ops.mesh.primitive_cube_add();o=bpy.context.object;finish(o,name,material,parent,pos);o.scale=(size[0]/2,size[2]/2,size[1]/2);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bevel: mod=o.modifiers.new('Machined edges','BEVEL');mod.width=bevel;mod.segments=2;o.modifiers.new('Normals','WEIGHTED_NORMAL')
 return o
def ball(name,pos,size,material,parent=head):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=16);o=bpy.context.object;finish(o,name,material,parent,pos);o.scale=(size[0],size[2],size[1]);
 for p in o.data.polygons:p.use_smooth=True
 return o
def tube(name,points,radius,material,parent):
 c=bpy.data.curves.new(name,'CURVE');c.dimensions='3D';c.bevel_depth=radius;c.bevel_resolution=3;s=c.splines.new('POLY');s.points.add(len(points)-1)
 for q,p in zip(s.points,points):q.co=(*xyz(p),1)
 o=bpy.data.objects.new(name,c);bpy.context.collection.objects.link(o);o.parent=parent;c.materials.append(material);return o
# Square hollow crate, metal bands, rivets and diamond paint.
cube('Floor',(0,.05,0),(.82,.1,.82),steel)
for side in range(4):
 ang=side*math.pi/2;out=Vector((math.sin(ang),0,math.cos(ang)));axis=Vector((math.cos(ang),0,-math.sin(ang)))
 panel=cube('Ivory panel '+str(side),tuple(out*.385+Vector((0,.38,0))),(.75,.62,.035),cream);panel.rotation_euler[2]=-ang
 for i in range(-2,3):
  for j in range(2):
   p=out*.407+axis*(i*.145)+Vector((0,.21+j*.27,0));d=cube('Crimson diamond',tuple(p),(.1,.17,.008),red,bevel=.003);d.rotation_euler[2]=-ang;d.rotation_euler[1]=math.pi/4
 for y in [.09,.65]:
  p=out*.416+Vector((0,y,0));o=cube('Border strap',tuple(p),(.84,.06,.025),steel);o.rotation_euler[2]=-ang
  for x in [-.34,-.17,0,.17,.34]:ball('Rivet',tuple(p+axis*x),(.013,.013,.013),brass,box)
 for x in [-.38,.38]:
  o=cube('Corner strap',tuple(out*.414+axis*x+Vector((0,.37,0))),(.055,.6,.025),steel);o.rotation_euler[2]=-ang
# Original painted jester crest on the rear panel; reference pixels are never used.
ball('Crate mask',(0,.37,.435),(.14,.19,.012),cream,box)
for side in [-1,1]:
 ball('Crest eye',(side*.047,.41,.45),(.037,.043,.008),black,box)
 brow=cube('Crest brow',(side*.047,.463,.448),(.077,.015,.006),black,box,.001);brow.rotation_euler[1]=side*.25
ball('Crest grin',(0,.30,.45),(.093,.053,.007),black,box)
ball('Crest nose',(0,.365,.46),(.03,.028,.014),red,box)
for j in range(7):cube('Crest tooth',((j-3)*.018,.325,.46),(.012,.015,.003),white,box,.001)
# Lid is authored open; runtime owns one rigid hinge.
cube('Lid plate',(0,0,.39),(.86,.06,.86),red,lid)
for x in [-.4,.4]:cube('Lid brace',(x,.025,.39),(.04,.035,.82),steel,lid)
for z in [0,.8]:cube('Lid brace',(0,.025,z),(.82,.035,.04),steel,lid)
# Stable helical spring between the box floor and the collar.
pts=[(.16*math.cos(i*math.tau*5/160),i*.62/160,.16*math.sin(i*math.tau*5/160)) for i in range(161)]
tube('SpringCoil',pts,.027,steel,spring)
# Project-authored sculpted jester: cheek planes, brows, pointed nose, black eyes, grin and teeth.
ball('Face',(0,.16,0),(.24,.29,.2),cream)
ball('Jaw',(0,-.045,.055),(.19,.14,.19),cream)
ball('Mouth',(0,.025,.187),(.145,.10,.032),black)
for side in [-1,1]:
 ball('Cheek',(side*.16,.12,.15),(.075,.10,.07),cream)
 eye=ball('Eye',(side*.093,.235,.17),(.063,.067,.035),black);eye.rotation_euler[1]=side*.25
 ball('Eye glint',(side*.083,.245,.204),(.014,.016,.008),white)
 brow=ball('Brow',(side*.095,.3,.18),(.105,.025,.035),black);brow.rotation_euler[1]=side*.32
 ball('Ear',(side*.255,.19,-.025),(.05,.10,.05),cream)
 for i in range(6):
  a=i*math.pi/6;ball('Collar ruffle',(side*.22*math.sin(a),-.17+ .025*math.cos(a),.18*math.cos(a)),(.085,.09,.07),cream)
ball('Nose',(0,.15,.238),(.058,.055,.059),red)
for i in range(9):
 x=(i-4)*.027;cube('Upper tooth',(x,.055-.04*(x/.12)**2,.22),(.019,.033,.012),white,head,.004)
for i in range(7):cube('Lower tooth',((i-3)*.026,-.018,.216),(.017,.023,.013),white,head,.003)
# Two curved, segmented red/black hat horns and brass bells.
for side in [-1,1]:
 for i in range(9):
  t=i/8;x=side*(.08+.40*t);y=.43+.22*math.sin(t*math.pi)-.16*t
  ball('Hat segment',(x,y,-.025),(.115*(1-.8*t),.13*(1-.75*t),.105*(1-.8*t)),red if i%2==0 else black)
 ball('Hat bell',(side*.50,.25,-.025),(.048,.048,.048),brass)
# Paint wear consists of original geometry, no external textures.
for i in range(65):
 x=random.uniform(-.36,.36);y=random.uniform(.12,.61);cube('Paint chip',(x,y,.408),(random.uniform(.005,.025),random.uniform(.003,.012),.001),steel,box,0)
bpy.ops.wm.save_as_mainfile(filepath=str(root/'source/JackInTheBox.blend'))
for parent in [box,lid,head,spring]:
 objects=[o for o in bpy.context.scene.objects if o.parent==parent and o.type in {'MESH','CURVE'}]
 bpy.ops.object.select_all(action='DESELECT')
 for o in objects:o.select_set(True)
 if objects:
  bpy.context.view_layer.objects.active=objects[0];bpy.ops.object.convert(target='MESH');bpy.ops.object.join();bpy.context.object.name=parent.name+'Geometry'
bpy.ops.export_scene.gltf(filepath=str(root/'JackInTheBox.glb'),export_format='GLB',export_apply=True,export_cameras=False,export_lights=False)
