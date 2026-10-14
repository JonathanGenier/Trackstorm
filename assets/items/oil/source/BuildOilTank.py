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
diamond=np.abs((u*8)%1-.5)/.5+np.abs((v*5)%1-.5)/.5 < .96
rng=np.random.default_rng(10152);grain=rng.random((h,w));wear=grain<.014
rgb=np.where(diamond[:,:,None],np.array((.53,.009,.014)),np.array((.76,.67,.49)))
cloud=.93+.035*np.sin(u*39+np.sin(v*23))+.025*np.sin(u*137-v*61)
rgb*=cloud[:,:,None]*(.91+.09*grain[:,:,None]);rgb[wear]=(.09,.07,.04)
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
# A scalloped painted cartouche follows the curved skin, not a rectangular sign.
# Leave substantial harlequin diamonds above, below and around the wordmark.
outline=.136+.020*np.cos((px+.13)*22)+.010*np.cos(px*49)
center=.040+.024*np.cos((px+.13)*4)
clean=np.clip((outline-np.abs(py-center))/.009,0,1)*np.clip((.55-np.abs(px+.075))/.035,0,1)*front
# A rubbed ivory brush area under the red correction keeps it distinct from
# nearby crimson diamonds at the rear chase angle; it joins the painted field.
correction=np.sqrt(((px-.145)/.202)**2+((py+.095)/.116)**2)
clean=np.maximum(clean,np.clip((1.04+.035*np.cos(px*81)-correction)/.10,0,1)*front)
enamel=np.array((.84,.76,.59))*(cloud[:,:,None])*(.96+.04*grain[:,:,None])
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
# Original circus lettering: bowed baseline, restrained serifs, warm pinstripe
# and a black face. All layers are paint in one texture, with no extra geometry.
x=-.525;height=.111;gas_start=0;word=[]
for letter in 'Laughing Gas':
 if letter==' ':x+=height*.34;continue
 if letter=='G':gas_start=x
 baseline=.014+.027*math.cos((x+.13)*4)
 word.extend([[(x+(a+.075*b)*height,baseline+b*.146) for a,b in stroke] for stroke in glyphs[letter]])
 if letter in 'Lhni':
  # Short wedge-like feet give the hand-drawn alphabet a signwriter's character.
  stem=.12 if letter=='i' else 0
  word.append([(x+(stem-.105)*height,baseline-.002),(x+(stem+.16)*height,baseline-.002)])
 if letter in 'Lh':
  word.append([(x-.012+height*.075,baseline+.146),(x+.025+height*.075,baseline+.146)])
 x+=height*(.34 if letter=='i' else .72)
ink=(.008,.003,.002);ochre=(.46,.22,.045);ivory=(.91,.84,.68)
painted(word,.0185,ivory)
painted(word,.0150,ochre)
painted(word,.0105,ink)
# A swept underline and split-end flourishes make the branding a single painted
# circus wordmark. They stop before the gauge and leave the correction legible.
flourish=[[(-.52,-.029),(-.44,-.053),(-.31,-.057),(-.17,-.033),(-.025,-.006)],
 [(-.49,-.025),(-.47,-.009),(-.445,-.015)],
 [(-.17,-.033),(-.075,-.045),(-.010,-.025)]]
painted(flourish,.0085,ochre);painted(flourish,.0035,ink)
# Two unmistakable red paint strokes cross only Gas, leaving its black letters readable.
painted([[(gas_start-.015,.039),(x-.013,.148)],[(gas_start-.008,.147),(x-.010,.049)]],.0078,(.62,.003,.008))
# Larger informal lower-case oil, with a leaning l and irregular brush strokes.
oil=[[(.055,-.076),(.020,-.086),(.003,-.146),(.015,-.184),(.054,-.192),(.091,-.165),(.107,-.098),(.088,-.074),(.055,-.076)],
 [(.166,-.097),(.141,-.188)],[(.175,-.061),(.178,-.046)],
 [(.285,-.035),(.247,-.166),(.257,-.185),(.282,-.174)]]
oil=[[(x,y+.030) for x,y in stroke] for stroke in oil]
painted(oil,.019,ivory);painted(oil,.0135,(.59,.002,.007))
# Worn double pinstripes circle both shoulders; small stencil stars and rivet
# witnesses reinforce the prop's circus-industrial body without a new plaque.
for sx in [-.535,.535]:
 for offset in [-.011,.011]:
  stripe=np.clip((.003-np.abs(px-sx-offset))/.0015,0,1)
  rgb[:]=rgb*(1-stripe[:,:,None])+np.array(ochre)*stripe[:,:,None]
for cx,cy,size in [(-.405,-.14,.026),(-.22,-.155,.022),(.32,.205,.017)]:
 points=[]
 for j in range(11):
  a=math.pi/2+j*math.pi/5;r=size if j%2==0 else size*.43
  points.append((cx+r*math.cos(a),cy+r*math.sin(a)))
 painted([points],.0035,ochre)
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
