# Original geometry, paint and hand-drawn lettering; executed by BuildJackInTheBox.py.
tank=empty('Tank',(0,-.40,.38),box)
# Larger vessel sits behind the hydraulic columns, beneath the unchanged rack.
tank.scale=(1.15,1.25,1.25)
def barrel(name,length,radius,material,parent=tank):
 vertices=[];faces=[]
 for i in range(9):
  x=(i/8-.5)*length;r=radius*(.84+.16*math.sin(math.pi*i/8)**.35)
  for j in range(49):
   a=j*math.tau/48;vertices.append(xyz((x,r*math.cos(a),r*math.sin(a))))
 for i in range(8):
  for j in range(48):faces.append((i*49+j,i*49+j+1,(i+1)*49+j+1,(i+1)*49+j))
 faces.extend([tuple(range(48,-1,-1)),tuple(8*49+j for j in range(49))])
 mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],faces);mesh.update();mesh.materials.append(material)
 uv=mesh.uv_layers.new(name='Paint')
 for poly in mesh.polygons:
  poly.use_smooth=True
  for li in poly.loop_indices:
   vi=mesh.loops[li].vertex_index;uv.data[li].uv=(vi//49/8,vi%49/48)
 o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o);o.parent=parent;return o

# Painted diamonds and chipped enamel are baked, avoiding unsupported Blender graphs.
w,h=1024,512;yy,xx=np.mgrid[0:h,0:w];u=xx/w;v=yy/h
diamond=np.abs((u*9)%1-.5)/.5+np.abs((v*4)%1-.5)/.5 < .80
rng=np.random.default_rng(10152);grain=rng.random((h,w));wear=grain<.014
rgb=np.where(diamond[:,:,None],np.array((.67,.012,.018)),np.array((.87,.80,.62)))
rgb*=.87+.13*grain[:,:,None];rgb[wear]=(.09,.07,.04)
# Lettering is baked directly into the curved vessel's Base Color. There is no
# plaque, backing mesh, rim or raised letter geometry between the paint and tank.
# Original stroke alphabet avoids redistributing a font or reference pixels.
glyphs={
'L':[[(0,1),(0,0),(.58,0)]],
'a':[[(.56,.60),(.40,.73),(.15,.73),(0,.55),(0,.18),(.15,0),(.40,0),(.56,.18)],[(.56,.73),(.56,0)]],
'u':[[(0,.73),(0,.18),(.15,0),(.40,0),(.56,.18),(.56,.73)]],
'g':[[(.56,.60),(.40,.73),(.15,.73),(0,.55),(0,.18),(.15,0),(.40,0),(.56,.18)],[(.56,.73),(.56,-.17),(.40,-.29),(.10,-.29)]],
'h':[[(0,0),(0,1)],[(0,.52),(.16,.73),(.40,.73),(.56,.56),(.56,0)]],
'i':[[(.12,0),(.12,.70)],[(.12,.96),(.12,1)]],
'n':[[(0,0),(0,.73)],[(0,.52),(.16,.73),(.40,.73),(.56,.56),(.56,0)]],
'G':[[(.62,.82),(.47,1),(.16,1),(0,.78),(0,.22),(.16,0),(.62,0),(.62,.48),(.36,.48)]],
's':[[(.54,.64),(.40,.73),(.13,.73),(0,.58),(.06,.43),(.48,.30),(.57,.14),(.42,0),(.12,0),(0,.10)]]}
px=(u-.5)*1.24
# The profile interpolates the authored barrel rings, including the tapered ends.
profile=np.interp(u,np.linspace(0,1,9),.25*(.84+.16*np.sin(np.linspace(0,math.pi,9))**.35))
py=profile*np.cos(v*math.tau);front=np.sin(v*math.tau)>.45
# Rubbed enamel follows the entire front circumference, without a rectangular
# label boundary. Diamonds remain around the shoulders and rear of the vessel.
clean=np.clip((np.sin(v*math.tau)-.30)/.32,0,1)*np.clip((.58-np.abs(px))/.05,0,1)
enamel=np.array((.91,.87,.76))*(.94+.06*grain[:,:,None])
rgb=rgb*(1-clean[:,:,None])+enamel*clean[:,:,None]
clear=clean>.5
def painted(strokes,radius,color):
 for stroke in strokes:
  for a,b in zip(stroke,stroke[1:]):
   ax,ay=a;bx,by=b;dx=bx-ax;dy=by-ay
   t=np.clip(((px-ax)*dx+(py-ay)*dy)/max(1e-12,dx*dx+dy*dy),0,1)
   distance=np.sqrt((px-ax-t*dx)**2+(py-ay-t*dy)**2)
   alpha=np.clip((radius+.0008-distance)/.0016,0,1)*front
   rgb[:]=rgb*(1-alpha[:,:,None])+np.array(color)*alpha[:,:,None]
x=-.535;height=.112;gas_start=0
for letter in 'Laughing Gas':
 if letter==' ':x+=height*.34;continue
 if letter=='G':gas_start=x
 painted([[(x+a*height,.027+b*.145) for a,b in stroke] for stroke in glyphs[letter]],.0115,(.003,.002,.002))
 x+=height*(.34 if letter=='i' else .72)
# Two unmistakable red paint strokes cross only Gas, leaving its black letters readable.
painted([[(gas_start-.01,.060),(x-.01,.129)],[(gas_start-.005,.130),(x-.015,.070)]],.0058,(.65,.004,.008))
# Larger informal lower-case oil, with a leaning l and irregular brush strokes.
painted([[(-.27,-.055),(-.32,-.070),(-.34,-.135),(-.32,-.172),(-.27,-.183),(-.22,-.152),(-.205,-.083),(-.23,-.054),(-.27,-.055)],
 [(-.12,-.083),(-.143,-.178)], [(-.104,-.038),(-.102,-.026)],
 [(.018,-.005),(-.015,-.158),(-.003,-.177),(.032,-.166)]],.012,(.67,.005,.010))
rgb[wear&(~clear)]=(.09,.07,.04)
rgba=np.concatenate([rgb,np.ones((h,w,1))],axis=2).astype('float32')
im=bpy.data.images.new('Oil Tank paint',width=w,height=h);im.pixels.foreach_set(rgba.ravel());im.filepath_raw=str(root/'source/Oil Tank paint.png');im.file_format='PNG';im.save();im.pack()
paintmat=bpy.data.materials.new('Tank circus enamel');paintmat.use_nodes=True
p=paintmat.node_tree.nodes.get('Principled BSDF');p.inputs['Roughness'].default_value=.58;p.inputs['Metallic'].default_value=.04
tex=paintmat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=im;paintmat.node_tree.links.new(tex.outputs['Color'],p.inputs['Base Color'])
barrel('Horizontal oil tank',1.24,.25,paintmat)
for x in [-.58,.58]:
 tube('Iron retaining strap',[(x,.252*math.cos(j*math.tau/64),.252*math.sin(j*math.tau/64)) for j in range(65)],.023,steel,tank)
 tube('Brass end weld',[(x*.94,.252*math.cos(j*math.tau/64),.252*math.sin(j*math.tau/64)) for j in range(65)],.009,brass,tank)
 cube('Tank saddle',(x,-.23,0),(.10,.09,.44),steel,tank)
 cube('Saddle hanger',(x,.01,-.18),(.045,.60,.045),steel,tank)
 for a in range(8):ball('Tank end rivet',(x,.20*math.cos(a*math.tau/8),.20*math.sin(a*math.tau/8)),(.019,.019,.019),brass,tank)
for x in [-.38,.40]:
 tube('Top brass fitting',[(x,.225,.06),(x,.30,.06)],.047,brass,tank)
 tube('Fitting flange',[(x,.239,.06),(x,.255,.06)],.066,steel,tank)
tube('Filler cap',[(.40,.30,.06),(.40,.315,.06)],.074,brass,tank)
tube('Cap handle',[(.35,.33,.06),(.35,.36,.06),(.46,.36,.06),(.46,.33,.06)],.011,steel,tank)

# Visual-only liquid level: a separate bottom pivot survives rigid mesh batching.
cube('Gauge brass frame',(.43,-.005,.259),(.19,.37,.047),brass,tank,.025)
cube('Gauge black gasket',(.43,-.005,.289),(.143,.316,.016),black,tank,.023)
cube('Gauge ivory back',(.43,-.005,.300),(.119,.286,.006),cream,tank,.018)
meter=empty('GaugeLiquidLevel',(.43,-.14,.309),tank)
cube('Gauge oil sight',(0,.136,0),(.109,.272,.008),black,meter,.003)
glass=bpy.data.materials.new('Oil sight glass');glass.use_nodes=True;glass.surface_render_method='DITHERED'
gp=glass.node_tree.nodes.get('Principled BSDF');gp.inputs['Base Color'].default_value=(.53,.70,.66,.16);gp.inputs['Alpha'].default_value=.16;gp.inputs['Roughness'].default_value=.13
cube('Gauge glass pane',(.43,-.005,.322),(.119,.286,.003),glass,tank,.008)
for i in range(7):
 y=-.12+i*.038;cube('Sight scale',(.48,y,.315),(.030 if i%3==0 else .016,.004,.003),brass,tank,.001)
cube('Glass reflection',(.39,.005,.325),(.004,.248,.002),white,tank,.002)
for y in [-.158,.148]:
 for x in [.362,.498]:ball('Gauge screw',(x,y,.296),(.009,.009,.005),steel,tank)

# Stepped hex nuts, dark gaskets and scored brass collars read as hose adapters.
def coupling(name,start,end,parent):
 a=Vector(start);b=Vector(end);axis=(b-a).normalized()
 for t,r,length,material,sides in [(0,.070,.018,steel,24),(.22,.061,.035,brass,6),(.57,.048,.042,brass,24),(.78,.050,.009,steel,24),(.91,.043,.022,brass,24)]:
  bpy.ops.mesh.primitive_cylinder_add(vertices=sides,radius=r,depth=length)
  o=bpy.context.object;finish(o,name,material,parent,tuple(a.lerp(b,t)));o.rotation_mode='QUATERNION';o.rotation_quaternion=Vector(xyz(axis)).to_track_quat('Z','Y')
  bevel=o.modifiers.new('Worn adapter edges','BEVEL');bevel.width=.003;bevel.segments=2;o.modifiers.new('Adapter normals','WEIGHTED_NORMAL')
coupling('Tank supply adapter',(-.38,.23,.06),(-.38,.30,.06),tank)
coupling('Box feed adapter',(-.405,.38,.10),(-.50,.38,.10),box)
coupling('Spring hose adapter',(-.29,.53,.16),(-.29,.635,.16),box)
coupling('Temple hose adapter',(-.22,.20,-.015),(-.33,.20,-.015),head)
