"""Original TS-296 production meshes and editable rigid animation storyboard.

Coordinates below use Godot metres (+Y up, -Z forward); exported pivots preserve
that contract. Rebuilding replaces the generated masters: preserve manual edits.
"""
from pathlib import Path
import math
import json
import hashlib
import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parents[2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
bpy.context.scene.unit_settings.system = 'METRIC'
collection = bpy.context.collection

def point(v):
    return Vector((v[0], -v[2], v[1]))

def material(name, rgb, metal, rough):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*rgb, 1)
    mat.use_nodes = True
    bs = mat.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = (*rgb, 1)
    bs.inputs['Metallic'].default_value = metal
    bs.inputs['Roughness'].default_value = rough
    # Packed original UV enamel wear, also carried by GLB (no external maps).
    n = 512
    y, x = np.mgrid[:n, :n] / n
    rng = np.random.default_rng(296)
    noise = rng.random((n, n))
    def worn_field(cells):
        grid=rng.random((cells+1,cells+1))
        yy,xx=np.mgrid[:n,:n]/n*cells
        ix=xx.astype(int); iy=yy.astype(int); fx=xx-ix; fy=yy-iy
        fx=fx*fx*(3-2*fx); fy=fy*fy*(3-2*fy)
        return (grid[iy,ix]*(1-fx)+grid[iy,ix+1]*fx)*(1-fy)+(grid[iy+1,ix]*(1-fx)+grid[iy+1,ix+1]*fx)*fy
    grain = np.sin(x * 231 + np.sin(y * 63)*2) * np.sin(y * 173 + np.cos(x * 41))
    clouds = worn_field(12)*.58+worn_field(37)*.27+worn_field(91)*.15
    shade = .84 + .12 * clouds + .04 * noise
    chips = ((clouds > .70) & (noise > .18)) | ((grain > .94) & (noise > .7))
    scratches = (np.sin(x*810+y*72) > .998) & (clouds > -.1)
    if 'balloon' in name or 'rubber' in name or 'eyes' in name:
        chips = noise > .9985
        scratches &= noise > .7
        shade = .95 + .03 * clouds + .02 * noise
    pixels = np.ones((n, n, 4), dtype=np.float32)
    pixels[:, :, :3] = shade[:, :, None] * np.array(rgb)
    pixels[chips, :3] = (.19, .16, .12)
    pixels[scratches, :3] = (.56, .58, .56)
    image = bpy.data.images.new(name + ' patina', width=n, height=n)
    image.pixels.foreach_set(pixels.ravel())
    image.pack()
    tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = image
    mat.node_tree.links.new(tex.outputs['Color'], bs.inputs['Base Color'])
    # Real glTF roughness/metallic and tangent normal maps, packed in the editable master.
    packed = np.ones((n,n,4),dtype=np.float32)
    packed[:,:,1] = np.clip(rough + (noise-.5)*.12 + chips*.24 - scratches*.14,.12,.94)
    packed[:,:,2] = np.where(chips, .78, metal)
    orm = bpy.data.images.new(name+' surface',width=n,height=n)
    orm.colorspace_settings.name='Non-Color'; orm.pixels.foreach_set(packed.ravel()); orm.pack()
    surface = mat.node_tree.nodes.new('ShaderNodeTexImage'); surface.image=orm
    channels = mat.node_tree.nodes.new('ShaderNodeSeparateColor')
    mat.node_tree.links.new(surface.outputs['Color'],channels.inputs['Color'])
    mat.node_tree.links.new(channels.outputs['Green'],bs.inputs['Roughness'])
    mat.node_tree.links.new(channels.outputs['Blue'],bs.inputs['Metallic'])
    height = noise*.12 - chips*.45 + scratches*.15
    dy,dx=np.gradient(height)
    normal=np.stack((-dx*1.7,-dy*1.7,np.ones_like(dx)),axis=2)
    normal/=np.linalg.norm(normal,axis=2)[:,:,None]
    packed[:,:,:3]=normal*.5+.5
    normalimage=bpy.data.images.new(name+' micro normal',width=n,height=n)
    normalimage.colorspace_settings.name='Non-Color'; normalimage.pixels.foreach_set(packed.ravel()); normalimage.pack()
    normaltex=mat.node_tree.nodes.new('ShaderNodeTexImage'); normaltex.image=normalimage
    normalnode=mat.node_tree.nodes.new('ShaderNodeNormalMap'); normalnode.inputs['Strength'].default_value=.55
    mat.node_tree.links.new(normaltex.outputs['Color'],normalnode.inputs['Color'])
    mat.node_tree.links.new(normalnode.outputs['Normal'],bs.inputs['Normal'])
    return mat

red = material('EMP crimson enamel', (.48, .022, .018), .48, .35)
ivory = material('EMP warm porcelain', (.76, .72, .61), .35, .38)
steel = material('EMP brushed steel', (.34, .39, .43), .86, .30)
iron = material('EMP blackened iron', (.042, .052, .064), .80, .42)
gold = material('EMP brass fittings', (.65, .37, .07), .80, .29)
rubber = material('EMP rubber hoses', (.016, .020, .025), .05, .58)
nosemat = material('EMP balloon red', (.65, .012, .018), .18, .23)
blue = material('EMP energized eyes', (.04, .38, .95), .20, .22)
bs = blue.node_tree.nodes.get('Principled BSDF')
bs.inputs['Emission Color'].default_value = (.025, .24, 1, 1)
bs.inputs['Emission Strength'].default_value = 2

def node(name, parent=None, pos=(0, 0, 0)):
    obj = bpy.data.objects.new(name, None)
    collection.objects.link(obj)
    obj.parent = parent
    obj.location = point(pos)
    obj.empty_display_size = .04
    return obj

def finish(obj, name, mat, parent, bevel=0):
    obj.name = name
    obj.parent = parent
    obj.data.materials.append(mat)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new('Machined edge', 'BEVEL')
        mod.width = bevel
        mod.segments = 2
        obj.modifiers.new('Weighted normal', 'WEIGHTED_NORMAL')
    return obj

def box(name, pos, size, mat, parent, bevel=.004):
    bpy.ops.mesh.primitive_cube_add(size=1, location=point(pos))
    obj = bpy.context.object
    obj.scale = (size[0], size[2], size[1])
    return finish(obj, name, mat, parent, bevel)

def pin(name, a, b, r, mat, parent, sides=24):
    a, b = point(a), point(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=r, depth=(b-a).length, location=(a+b)/2)
    obj = bpy.context.object
    obj.rotation_euler = (b-a).to_track_quat('Z', 'Y').to_euler()
    for face in obj.data.polygons:
        face.use_smooth = len(face.vertices) == 4
    return finish(obj, name, mat, parent, .002)

def ball(name, pos, size, mat, parent):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, location=point(pos))
    obj = bpy.context.object
    obj.scale = (size[0], size[2], size[1])
    for face in obj.data.polygons:
        face.use_smooth = True
    return finish(obj, name, mat, parent)

def mesh(name, verts, faces, mat, parent):
    data = bpy.data.meshes.new(name)
    data.from_pydata([point(v) for v in verts], [], faces)
    bm = bmesh.new()
    bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data)
    bm.free()
    obj = bpy.data.objects.new(name, data)
    collection.objects.link(obj)
    obj.parent = parent
    data.materials.append(mat)
    return obj

def plate(name, verts, mat, parent, thickness=.003):
    obj = mesh(name, verts, [list(range(len(verts)))], mat, parent)
    mod = obj.modifiers.new('Sheet thickness', 'SOLIDIFY')
    mod.thickness = thickness
    if thickness >= .007:
        bevel=obj.modifiers.new('Rounded armor edges','BEVEL'); bevel.width=.002; bevel.segments=3
        obj.modifiers.new('Armor corner normals','WEIGHTED_NORMAL')
    return obj

def tube(name, z0, z1, radius, thickness, mat, parent, x=0, y=0):
    n = 48
    verts = [(x+math.cos(i*math.tau/n)*r, y+math.sin(i*math.tau/n)*r, z)
             for z, r in [(z0, radius), (z1, radius), (z0, radius-thickness), (z1, radius-thickness)] for i in range(n)]
    faces = []
    for i in range(n):
        j = (i+1) % n
        faces += [(i,j,n+j,n+i),(2*n+i,3*n+i,3*n+j,2*n+j),
                  (i,2*n+i,2*n+j,j),(n+i,n+j,3*n+j,3*n+i)]
    return mesh(name, verts, faces, mat, parent)

def hose(name, points, r, parent, mat=rubber):
    curve = bpy.data.curves.new(name, 'CURVE')
    curve.dimensions = '3D'
    curve.bevel_depth = r
    curve.bevel_resolution = 2
    spline = curve.splines.new('BEZIER')
    spline.bezier_points.add(len(points)-1)
    for p, v in zip(spline.bezier_points, points):
        p.co = point(v)
        p.handle_left_type = p.handle_right_type = 'AUTO'
    obj = bpy.data.objects.new(name, curve)
    collection.objects.link(obj)
    obj.parent = parent
    curve.materials.append(mat)
    return obj

launcher = node('EmpClawLauncher')
box('Rack saddle', (0, .015, 0), (1.10, .045, .66), iron, launcher)
for x in [-.48, .48]:
    for z in [-.26, .26]:
        box('Red clamp', (x,.045,z), (.14,.04,.12), red, launcher)
        pin('Captive bolt', (x,.06,z), (x,.08,z), .024, gold, launcher, 6)
pin('Slew bearing fixed', (0,.045,0),(0,.105,0),.25,steel,launcher,48)
yaw = node('TurretYaw', launcher)
pin('Geared turntable',(0,.105,0),(0,.14,0),.24,red,yaw,48)
for i in range(40):
    a=i*math.tau/40
    obj=box('Slew gear tooth',(.245*math.cos(a),.115,.245*math.sin(a)),(.027,.03,.024),iron,yaw,.001)
    obj.rotation_euler[2]=a
for x in [-.40,.40]:
    pin('Vertical lift guide',(x,.08,0),(x,.23,0),.045,iron,yaw)
lift=node('CannonLift',yaw)
for x in [-.40,.40]:
    pin('Polished lift piston',(x,-.06,0),(x,.20,0),.028,steel,lift)
    box('Cradle trunnion',(x,.18,0),(.08,.18,.19),red,lift)
    pin('Pitch bearing',(x-.055,.23,0),(x+.055,.23,0),.068,steel,lift)
# Keep the physical trunnions concentric with the lowered cannon pivot.
for obj in list(lift.children):
    obj.location.z -= .20
pitch=node('CannonPitch',lift,(0,0,0))
recoil=node('BarrelRecoil',pitch)
tube('Open pressure chamber',-.08,.22,.210,.014,red,recoil)
breech=node('BreechHinge',recoil,(-.225,0,.25))
pin('Breech door',(.225,0,-.018),(.225,0,.018),.215,iron,breech,64)
pin('Breech pressure plate',(.225,0,.019),(.225,0,.035),.16,red,breech,48)
for i in range(8):
    a=i*math.tau/8
    pin('Breech captive bolt',(.225+.178*math.cos(a),.178*math.sin(a),.020),(.225+.178*math.cos(a),.178*math.sin(a),.036),.012,gold,breech,6)
tube('Breech seal',-.023,-.018,.202,.012,gold,breech,x=.225)
for y in [-.15,.15]:
    pin('Breech hinge knuckle',(0,y-.04,0),(0,y+.04,0),.035,steel,breech)
box('Breech latch',(.43,0,.035),(.04,.12,.03),gold,breech)
tube('Fixed receiver',-.43,.05,.210,.014,red,recoil)
for z in [-.27,-.02,.15]:
    tube('Receiver steel band',z-.023,z+.023,.222,.018,steel,recoil)
    for i in range(12):
        a=i*math.tau/12
        ball('Band rivet',(.224*math.cos(a),.224*math.sin(a),z),(.010,.010,.010),gold,recoil)
for i in range(8):
    a=i*math.tau/8
    def receiverpos(angle,z,r=.212): return (r*math.cos(angle),r*math.sin(angle),z)
    plate('Circus pressure vessel diamond',[receiverpos(a-.21,-.145),receiverpos(a,-.23),receiverpos(a+.21,-.145),receiverpos(a,-.06)],ivory,recoil)
    plate('Diamond red inset',[receiverpos(a-.08,-.145,.216),receiverpos(a,-.18,.216),receiverpos(a+.08,-.145,.216),receiverpos(a,-.11,.216)],red,recoil)
middle=node('BarrelMiddle',recoil)
tube('Intermediate telescopic sleeve',-.51,.05,.195,.012,red,middle)
tube('Intermediate steel collar',-.53,-.48,.208,.020,steel,middle)
for z in [-.34,-.07]:
    tube('Intermediate sleeve retaining ring',z-.012,z+.012,.201,.010,iron,middle)
    for i in range(10):
        a=i*math.tau/10
        ball('Sleeve captive screw',(.202*math.cos(a),.202*math.sin(a),z),(.009,.009,.009),steel,middle)
for i in range(8):
    a=i*math.tau/8
    def sleevepos(angle,z): return (.197*math.cos(angle),.197*math.sin(angle),z)
    plate('Intermediate ivory diamond',[sleevepos(a-.20,-.27),sleevepos(a,-.37),sleevepos(a+.20,-.27),sleevepos(a,-.17)],ivory,middle)
extension=node('BarrelExtension',recoil)
tube('Telescopic cannon bore',-.57,0,.180,.014,red,extension)
tube('Black muzzle lining',-.595,-.32,.166,.009,iron,extension)
tube('Flared muzzle crown',-.62,-.56,.220,.040,steel,extension)
tube('Muzzle lip polished edge',-.625,-.611,.222,.012,steel,extension)
for i in range(12):
    a=i*math.tau/12
    def ringpos(angle,z): return (.182*math.cos(angle),.182*math.sin(angle),z)
    plate('Ivory circus muzzle tooth',[ringpos(a-.20,-.53),ringpos(a+.20,-.53),ringpos(a,-.26)],ivory,extension)
    ball('Muzzle fixing rivet',(.205*math.cos(a),.205*math.sin(a),-.622),(.008,.008,.006),gold,extension)
node('Muzzle',extension,(0,0,-.63))
for x in [-.19,.19]:
    pin('Barrel guide',(x,-.07,-.32),(x,-.07,.13),.012,steel,recoil,12)
    pin('Guide collar',(x,-.07,-.19),(x,-.07,-.14),.023,gold,recoil,12)
# Breech jacket, pneumatic accumulator, regulator and articulated pipework.
for side in [-1,1]:
    x=side*.245
    box('Cradle side plate',(x,-.095,.045),(.035,.19,.30),iron,pitch,.016)
    box('Red access plate',(x+side*.023,-.08,.045),(.012,.14,.23),red,pitch,.008)
    for y in [-.13,-.025]:
        for z in [-.04,.13]:
            pin('Cradle hex bolt',(x+side*.026,y,z),(x+side*.038,y,z),.014,gold,pitch,6)
    pin('Pitch axle cap',(x+side*.028,-.08,.04),(x+side*.045,-.08,.04),.047,steel,pitch,32)
    hose('Braided pressure return',[(x,-.13,.19),(side*.30,-.14,.23),(side*.30,.06,.17),(side*.18,.07,.19)],.013,pitch)
    for z in [.12,.16,.20]:
        tube('Rear breech heat fins',z,z+.011,.205,.025,iron,recoil)
pin('Pneumatic accumulator',(.29,-.07,-.32),(.29,-.07,.03),.070,red,pitch,40)
for z in [-.32,.03]:
    ball('Accumulator dome',(.29,-.07,z),(.07,.07,.045),red,pitch)
    tube('Accumulator clamp',z-.006,z+.012,.078,.011,steel,pitch,x=.29,y=-.07)
pin('Pressure regulator',(.29,-.07,.075),(.29,-.07,.13),.029,gold,pitch,6)
hose('Breech supply hose',[(.29,-.07,.13),(.34,-.04,.20),(.30,.10,.23),(.13,.13,.19)],.014,pitch)
for z in [-.37,-.29,-.21,-.13,-.05,.03]:
    box('Lower longitudinal rail',(0,-.205,z),(.29,.018,.045),iron,pitch,.004)
# Enlarge the pressure vessel radially for the larger folded cartridge.
for obj in list(pitch.children_recursive):
    if obj.type == 'CURVE':
        bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
        bpy.context.view_layer.objects.active=obj
        bpy.ops.object.convert(target='MESH')
# Bake each mesh around its rigid parent's origin, then enlarge parent translations.
for obj in list(pitch.children_recursive):
    if obj.type != 'MESH': continue
    matrix=obj.matrix_local.copy()
    for vertex in obj.data.vertices:
        v=matrix @ vertex.co
        v.x *= 1.65; v.z *= 1.65
        vertex.co=v
    obj.matrix_local=Matrix.Identity(4)
for obj in list(pitch.children_recursive):
    if obj.type == 'EMPTY':
        obj.location.x *= 1.65; obj.location.z *= 1.65
# Larger transverse air reservoir beneath the fixed rack, ahead of the magazine.
pin('Air tank',(-.43,-.19,-.23),(.43,-.19,-.23),.16,red,launcher,48)
for x in [-.43,.43]:
    ball('Tank dished end',(x,-.19,-.23),(.07,.16,.16),red,launcher)
for x in [-.32,.32]:
    hose('Tank retaining strap',[(x,-.02,-.23),(x,-.08,-.09),(x,-.30,-.11),(x,-.36,-.23),(x,-.30,-.35),(x,-.08,-.37),(x,-.02,-.23)],.018,launcher,steel)
    box('Tank mounting bracket',(x,-.025,-.23),(.09,.055,.12),iron,launcher)
pin('Valve stem',(.49,-.19,-.23),(.56,-.19,-.23),.026,gold,launcher)
pin('Valve wheel',(.55,-.19,-.25),(.55,-.19,-.21),.060,red,launcher)
hose('Fixed pneumatic line',[(.55,-.19,-.23),(.55,.05,-.12),(.30,.07,.14),(.23,.11,.06)],.019,launcher)
pin('Pressure gauge housing',(-.46,-.13,-.30),(-.46,-.13,-.36),.065,gold,launcher)
pin('Pressure gauge face',(-.46,-.13,-.361),(-.46,-.13,-.365),.055,ivory,launcher)
pin('Gauge needle',(-.46,-.13,-.367),(-.48,-.10,-.367),.004,iron,launcher,8)
node('RoundSocket_0',launcher)
for index,x in enumerate([-.34,.34],1):
    node('RoundSocket_'+str(index),launcher,(x,-.27,.40))
    box('Magazine saddle',(x,-.51,.40),(.31,.025,.23),red,launcher)
    for sx in [-.20,.20]:
        pin('Magazine hanger',(x+sx,.015,.20),(x+sx,-.48,.20),.014,iron,launcher)
        box('Retaining cheek',(x+sx,-.40,.40),(.025,.14,.15),steel,launcher)
box('Loader base bracket',(0,-.215,.30),(.12,.43,.10),iron,launcher)
pin('Loader base shaft',(0,-.43,.30),(0,-.43,.38),.045,steel,launcher)
# One two-link retrieval arm and a wrist with opening gripper fingers.
for name in ['LoaderUpper','LoaderLower']:
    link=node(name,launcher)
    box('Arm boxed link',(0,.4,0),(.075,.8,.065),red,link)
    pin('Arm hydraulic rod',(.05,.08,0),(.05,.72,0),.012,steel,link)
    for y in [0,.8]:
        pin('Articulated joint',(-.055,y,0),(.055,y,0),.065,iron,link)
        pin('Joint pin',(-.059,y,0),(.059,y,0),.030,gold,link)
gripper=node('LoaderGripper',launcher)
box('Gripper palm',(0,0,.09),(.75,.065,.06),iron,gripper)
pin('Gripper screw drive',(-.35,0,.09),(.35,0,.09),.018,gold,gripper)
for side in [-1,1]:
    finger=node('LoaderFinger_'+str(side),gripper)
    box('Gripper finger',(0,0,-.055),(.028,.07,.23),steel,finger)
    box('Rubber grip pad',(-side*.014,0,-.12),(.014,.07,.05),rubber,finger)

# Clown device faces -Z; its Attachment is the rear metal-contact plane.
claw=node('EmpClaw')
claw.scale=(1.5,)*3
pin('Device drum',(0,0,.045),(0,0,-.025),.135,iron,claw,48)
tube('Armored rim',-.045,.04,.143,.018,steel,claw)
ball('Clown porcelain face',(0,0,-.045),(.123,.125,.045),ivory,claw)
for z in [.014,.030,.045]:
    tube('Rear induction coil',z,z+.007,.137,.007,gold,claw)
for side in [-1,1]:
    ball('Sculpted cheekbone',(side*.083,-.013,-.076),(.036,.037,.023),ivory,claw)
    ball('Deep temple recess',(side*.104,.039,-.052),(.022,.039,.016),iron,claw)
    hose('Porcelain cheek crease',[(side*.094,.009,-.098),(side*.099,-.018,-.098),(side*.084,-.043,-.102)],.003,claw,iron)
    hose('Red eye tear',[(side*.063,.013,-.104),(side*.073,-.012,-.108),(side*.069,-.027,-.103)],.004,claw,red)
    hose('External induction cable',[(side*.123,-.065,.029),(side*.144,0,.019),(side*.116,.081,.002)],.009,claw)
    for yy in [-.069,.076]:
        pin('Cable coupling',(side*.118,yy,.018),(side*.118,yy,.036),.014,gold,claw,6)
ball('Forehead armored dome',(0,.082,-.053),(.085,.050,.037),ivory,claw)
for x in [-.037,-.018,0,.018,.037]:
    box('Forehead cooling grille',(x,.106,-.075),(.009,.031,.009),iron,claw,.003)
ball('Nasal bridge',(0,.039,-.086),(.019,.038,.023),ivory,claw)
ball('Chin guard',(0,-.104,-.058),(.054,.023,.029),steel,claw)
for i in range(20):
    a=i*math.tau/20
    box('Vent slot',(.137*math.cos(a),.137*math.sin(a),.01),(.012,.012,.028),iron,claw,.001)
    ball('Rim rivet',(.129*math.cos(a),.129*math.sin(a),-.052),(.007,.007,.005),gold,claw)
for side in [-1,1]:
    ball('Eye socket',(side*.048,.032,-.080),(.041,.033,.018),iron,claw)
    ball('Iris steel bezel',(side*.049,.031,-.096),(.025,.021,.008),steel,claw)
    ball('Blue iris',(side*.049,.031,-.104),(.016,.013,.006),blue,claw)
    ball('Eye black pupil',(side*.049,.031,-.111),(.006,.013,.003),iron,claw)
    plate('Angry brow',[(side*.009,.061,-.104),(side*.090,.074,-.090),(side*.083,.041,-.109),(side*.028,.040,-.112)],ivory,claw,.012)
    hose('Sculpted eyebrow ridge',[(side*.016,.052,-.116),(side*.044,.065,-.116),(side*.080,.070,-.104)],.010,claw,ivory)
    plate('Clown eye paint',[(side*.043,.075,-.078),(side*.065,.110,-.067),(side*.076,.067,-.079)],red,claw)
ball('Grinning mouth',(0,-.050,-.080),(.090,.049,.012),iron,claw)
for i in range(9):
    x=(i-4)*.018
    top=-.037-.015*(1-abs(i-4)/4)
    plate('Upper steel grin',[(x-.007,top,-.101),(x+.007,top,-.101),(x,top-.027,-.108)],gold,claw,.007)
    bottom=-.083+.014*abs(i-4)/4
    plate('Lower steel grin',[(x-.006,bottom,-.099),(x+.006,bottom,-.099),(x,bottom+.018,-.106)],ivory,claw,.006)
hose('Painted grin',[(-.095,-.035,-.083),(-.065,-.077,-.089),(0,-.101,-.076),(.065,-.077,-.089),(.095,-.035,-.083)],.006,claw,red)
nose=node('BalloonNose',claw,(0,-.002,-.10))
ball('NoseMembrane',(0,0,-.025),(.050,.050,.050),nosemat,nose)
pin('Balloon ferrule',(0,0,.006),(0,0,-.012),.026,gold,nose)
node('NoseHitAnchor',nose,(0,0,-.025))
node('Attachment',claw,(0,0,.055))
node('TrailSocket',claw,(0,0,.07))
for i in range(4):
    a=math.pi/4+i*math.pi/2
    radial=node('JawRadial_'+str(i),claw)
    radial.rotation_euler[1]=-a
    joint=node('JawHinge_'+str(i),radial,(.116,0,0))
    pin('Hinge axle',(0,-.021,0),(0,.021,0),.025,gold,joint)
    # Swept taper with a hooked metal contact at +Z when deployed.
    plate('Serrated claw armor',[(-.014,-.033,-.016),(.095,-.041,.004),(.145,-.029,.055),(.115,.035,.062),(.015,.038,.006)],ivory,joint,.028)
    plate('Claw outer steel reveal',[(-.016,-.037,-.012),(.090,-.045,.008),(.147,-.031,.060),(.117,.037,.069),(.015,.042,.012)],steel,joint,.007)
    plate('Claw layered dorsal armor',[(.013,-.028,-.024),(.065,-.032,-.013),(.102,-.023,.016),(.078,.025,.022),(.017,.028,-.015)],ivory,joint,.012)
    for side in [-1,1]:
        pin('Jaw pivot flanged cap',(0,side*.020,0),(0,side*.030,0),.029,steel,joint,32)
        pin('Jaw pivot socket bolt',(0,side*.031,0),(0,side*.036,0),.014,gold,joint,6)
        pin('Jaw pivot dark socket',(0,side*.036,0),(0,side*.037,0),.006,iron,joint,6)
        pin('Jaw knuckle pin',(.083,side*.021,.018),(.083,side*.031,.018),.017,gold,joint,24)
        pin('Knuckle cap recess',(.083,side*.031,.018),(.083,side*.033,.018),.009,iron,joint,6)
    hose('Jaw hydraulic line',[(.006,.026,-.017),(.027,.032,-.024),(.064,.029,-.003),(.086,.025,.018)],.005,joint)
    box('Forged jaw core',(.048,0,.014),(.080,.050,.031),steel,joint,.006)
    pin('Claw spine',(.008,0,.008),(.082,0,.024),.019,steel,joint)
    pin('Hooked contact',(.082,0,.024),(.140,0,.065),.015,iron,joint,12)
    for t in [.025,.048,.072]:
        for side in [-1,1]:
            plate('Claw tooth',[(t,side*.017,.024),(t+.015,side*.017,.029),(t+.008,side*.032,.043)],steel,joint,.006)
    plate('Claw red slash',[(.020,-.026,.008),(.042,-.028,.013),(.066,-.015,.025)],red,joint)
    node('GripContact_'+str(i),joint,(.140,0,.065))

# Preserve continuous primitive UVs, especially the balloon's smooth tangent frame.
# Custom armor gets a full-density per-part unwrap instead of tiny shared atlas islands.
for obj in list(collection.objects):
    if obj.type!='MESH' or obj.data.uv_layers: continue
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
    bpy.context.view_layer.objects.active=obj
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.015)
    bpy.ops.object.mode_set(mode='OBJECT')
# Preserve all source parts and the named rigid animation controls in the master.
scene=bpy.context.scene
scene.frame_start=1
scene.frame_end=180
scene['Reference attachments']='TS-296 10155/10156/10157/10158; original geometry, reference pixels not redistributed'
scene['Runtime axes']='Godot metres; CannonPitch deployed at rack + (0,.47,0), matching WeaponAim.Pivot'
for frame, height, lift_height, extent in [(1,-.08,0,0),(32,1.34,0,0),(44,1.34,.62,0),(54,1.34,.47,0),(72,1.34,.47,1)]:
    launcher.location=point((0,height,1.845)); launcher.keyframe_insert(data_path='location',frame=frame)
    lift.location=point((0,lift_height,0)); lift.keyframe_insert(data_path='location',frame=frame)
    extension.location=point((0,0,-.99*extent)); extension.keyframe_insert(data_path='location',frame=frame)
    middle.location=point((0,0,-.50*extent)); middle.keyframe_insert(data_path='location',frame=frame)
for frame, spread, inflation in [(1,0,1),(78,.35,1),(100,1,1),(120,1,2.6),(155,1,.05),(175,0,.05)]:
    for i in range(4):
        joint=bpy.data.objects['JawHinge_'+str(i)]
        joint.rotation_euler[2]=(1-spread)*math.radians(-78)
        joint.keyframe_insert(data_path='rotation_euler',frame=frame)
    nose.scale=(inflation,)*3; nose.keyframe_insert(data_path='scale',frame=frame)
for frame,label in [(1,'STOWED'),(32,'RACK_RAISED'),(44,'EMERGED'),(54,'LOWERED'),(72,'READY'),(78,'FIRED'),(100,'LANDED'),(120,'ACTIVATED'),(155,'DEFLATED'),(175,'RELEASED')]:
    scene.timeline_markers.new(label,frame=frame)
car=REPO/'assets/vehicles/source/TrackstormCar.blend'
with bpy.data.libraries.load(str(car),link=True) as (src,dst):
    dst.objects=src.objects
reference=bpy.data.collections.new('Production Car reference (unchanged)')
scene.collection.children.link(reference)
reference.hide_render=True
for obj in dst.objects:
    if obj: reference.objects.link(obj)
for library in bpy.data.libraries:
    library.filepath=bpy.path.relpath(library.filepath,start=str(ROOT/'source'))
scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/EmpClaw.blend'))
for obj in [launcher,lift,middle,extension,nose]+[bpy.data.objects['JawHinge_'+str(i)] for i in range(4)]:
    obj.animation_data_clear()
launcher.location=(0,0,0)
lift.location=(0,0,0)
extension.location=(0,0,0)
middle.location=(0,0,0)
nose.scale=(1,1,1)
for i in range(4): bpy.data.objects['JawHinge_'+str(i)].rotation_euler=(0,0,0)
# Merge only surfaces sharing a rigid parent/material in the runtime exports.
for parent in [o for o in list(collection.objects) if o.type=='EMPTY']:
    for mat in bpy.data.materials:
        group=[o for o in parent.children if o.type in ('MESH','CURVE') and o.data.materials and o.data.materials[0]==mat]
        if not group: continue
        bpy.ops.object.select_all(action='DESELECT')
        for obj in group: obj.select_set(True)
        bpy.context.view_layer.objects.active=group[0]
        bpy.ops.object.convert(target='MESH')
        if len(group)>1: bpy.ops.object.join()
        bpy.context.object.name=parent.name+'_'+mat.name.replace(' ','_')
for root,filename in [(launcher,'EmpClawLauncher.glb'),(claw,'EmpClaw.glb')]:
    bpy.ops.object.select_all(action='DESELECT')
    for obj in [root]+list(root.children_recursive): obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(ROOT/filename),export_format='GLB',use_selection=True,export_yup=True,export_animations=False,export_apply=True)
paths=[ROOT/'source/BuildEmpClaw.py',ROOT/'source/EmpClaw.blend',ROOT/'EmpClaw.glb',ROOT/'EmpClawLauncher.glb']
manifest={'authoring':'Original Blender geometry, UV patina and rigid animation; no third-party acquisition',
 'references':{'10155':'t shirt launcher.png','10156':'Back stowed.png','10157':'emp claw activated.png','10158':'emp claw landed.png'},
 'reference_policy':'User-supplied Jira design direction; no reference pixels redistributed',
 'car_source_sha256':hashlib.sha256(car.read_bytes()).hexdigest(),
 'sha256':{p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}}
(ROOT/'sources.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print('EMP_CLAW_AUTHORING_COMPLETE',flush=True)
