"""Read-only GLB contract/budget/compact-envelope audit; Python standard library."""
import json
import math
import struct
from pathlib import Path

root = Path(__file__).resolve().parents[1]
data = (root / 'SalvoCatapult.glb').read_bytes()
assert data[:4] == b'glTF' and struct.unpack_from('<I', data, 4)[0] == 2
length = struct.unpack_from('<I', data, 12)[0]
doc = json.loads(data[20:20+length])
nodes = doc['nodes']; names = {n['name']: i for i, n in enumerate(nodes)}
parents = {child: i for i, n in enumerate(nodes) for child in n.get('children', [])}

def descends(child, ancestor):
    i = names[child]
    while i in parents:
        i = parents[i]
        if i == names[ancestor]: return True
    return False

for name in ['SpoonHinge', 'Spring', 'SupportExtension', 'Magazine', 'BowlSocket', 'CableEye']:
    assert descends(name, 'TurretYaw'), name + ' must rotate with turret'
assert not descends('WinchHousing', 'TurretYaw'), 'Winch stays inside fixed rack'
assert all(descends('Round_'+str(i), 'Magazine') for i in range(5))
assert descends('Magazine', 'ArmTip')

def transform(i, point):
    n = nodes[i]; scale = n.get('scale', [1, 1, 1]); p = [a*b for a,b in zip(point,scale)]
    x,y,z,w = n.get('rotation', [0,0,0,1]); a,b,c = p
    tx,ty,tz = 2*(y*c-z*b),2*(z*a-x*c),2*(x*b-y*a)
    p = [a+w*tx+y*tz-z*ty,b+w*ty+z*tx-x*tz,c+w*tz+x*ty-y*tx]
    p = [a+b for a,b in zip(p,n.get('translation',[0,0,0]))]
    assert 'matrix' not in n, 'Audit expects exported TRS nodes'
    return transform(parents[i], p) if i in parents else p

def positions(accessor):
    a=doc['accessors'][accessor]; view=doc['bufferViews'][a['bufferView']]
    offset=28+length+view.get('byteOffset',0)+a.get('byteOffset',0)
    return [struct.unpack_from('<fff',data,offset+i*view.get('byteStride',12)) for i in range(a['count'])]

points=[]; triangles=0; surfaces=0; heights=[]
for i,n in enumerate(nodes):
    if 'mesh' not in n: continue
    for primitive in doc['meshes'][n['mesh']]['primitives']:
        assert 'TEXCOORD_0' in primitive['attributes'] and 'NORMAL' in primitive['attributes']
        surfaces += 1; triangles += doc['accessors'][primitive['indices']]['count']//3
        vertices=positions(primitive['attributes']['POSITION'])
        weights=n.get('weights',doc['meshes'][n['mesh']].get('weights',[]))
        for weight,target in zip(weights,primitive.get('targets',[])):
            vertices=[tuple(a+weight*b for a,b in zip(p,d)) for p,d in zip(vertices,positions(target['POSITION']))]
        transformed = [transform(i, p) for p in vertices]
        points.extend(transformed)
        heights.append((max(p[1] for p in transformed), n['name']))
minimum = [min(p[i] for p in points) for i in range(3)]
maximum = [max(p[i] for p in points) for i in range(3)]
assert all(math.isfinite(v) for p in points for v in p)
print('Highest compact surfaces:', sorted(heights, reverse=True)[:4])
assert triangles <= 60000 and surfaces <= 88, (triangles,surfaces)
assert minimum[0] > -.75 and maximum[0] < .75, (minimum,maximum)
assert minimum[1] > -.75 and maximum[1] < .60, (minimum,maximum)
assert minimum[2] > -1.0 and maximum[2] < .80, (minimum,maximum)
print(json.dumps({'triangles':triangles,'surfaces':surfaces,'materials':len(doc['materials']),
    'compact_min':minimum,'compact_max':maximum,'rig_and_uvs':'PASS'},indent=2))

# Inspect authored mesh vertices, not merely empty-node names. A missing, baked
# or flattened spring/cable can otherwise pass every hierarchy/count assertion.
coil = nodes[names['SpringCoil']]
coil_mesh = doc['meshes'][coil['mesh']]
assert len(coil_mesh['primitives']) == 1
primitive = coil_mesh['primitives'][0]
assert len(primitive.get('targets', [])) == 1, 'Spring needs its constant-gauge pitch morph'
assert descends('SpringLowerSeat', 'SpringFoot') and descends('SpringUpperSeat', 'SpringTop')
assert descends('SpringFoot', 'SupportExtension') and descends('SpringTop', 'ArmTip')
assert not descends('SpringTop', 'Magazine')
assert descends('TurretSlide', 'PlatformLift') and descends('SpoonHinge', 'HingeCarriage')
assert descends('HingeCarriage', 'TurretYaw')

base=positions(primitive['attributes']['POSITION']); delta=positions(primitive['targets'][0]['POSITION'])
for weight,expected in [(0,.234),(1,2.034)]:
    shaped=[tuple(a+weight*b for a,b in zip(p,d)) for p,d in zip(base,delta)]
    assert abs(max(p[1] for p in shaped)-min(p[1] for p in shaped)-expected)<.002
    assert .26 < max(p[0] for p in shaped)-min(p[0] for p in shaped) < .28
for n in nodes:
    if n['name'].startswith('Unit cable'):
        assert max(abs(x) for x in n.get('translation',[0,0,0])) < .0001
        assert max(abs(x-y) for x,y in zip(n.get('rotation',[0,0,0,1]),[0,0,0,1])) < .0001
        assert max(abs(x-1) for x in n.get('scale',[1,1,1])) < .0001
        p=doc['meshes'][n['mesh']]['primitives'][0]
        points=positions(p['attributes']['POSITION'])
        assert abs(min(v[1] for v in points))<.001 and abs(max(v[1] for v in points)-1)<.001
        assert .065 < max(v[0] for v in points)-min(v[0] for v in points) < .067
        assert doc['materials'][p['material']]['name']=='Salvo carnival rope'
assert descends('CableSpoonGuide','SpoonHinge') and descends('CableEye','ArmTip')
terminal = nodes[names['CableEye']]['translation']
spring_top = nodes[names['SpringTop']]['translation']
assert -.10 < terminal[2] < spring_top[2]-.30 and terminal[1] < spring_top[1], 'Eye must be behind spring, not at bowl'
assert descends('WinchDrum','WinchHousing') and not descends('WinchLevelWind','TurretYaw')
assert 'CableSegment6' in names
assert any(image.get('name') == 'Salvo carnival rope' for image in doc['images'])
assert {'Salvo circus enamel','Salvo circus white'} <= {m['name'] for m in doc['materials']}
print('Spring mesh/morph, rigid seats, normalized cable meshes and platform ancestry: PASS')
