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

points=[]; triangles=0; surfaces=0; heights=[]
for i,n in enumerate(nodes):
    if 'mesh' not in n: continue
    for primitive in doc['meshes'][n['mesh']]['primitives']:
        assert 'TEXCOORD_0' in primitive['attributes'] and 'NORMAL' in primitive['attributes']
        surfaces += 1; triangles += doc['accessors'][primitive['indices']]['count']//3
        bounds = doc['accessors'][primitive['attributes']['POSITION']]
        view = doc['bufferViews'][bounds['bufferView']]
        offset = 28 + length + view.get('byteOffset', 0) + bounds.get('byteOffset', 0)
        stride = view.get('byteStride', 12)
        transformed = [transform(i, struct.unpack_from('<fff', data, offset+j*stride)) for j in range(bounds['count'])]
        points.extend(transformed)
        heights.append((max(p[1] for p in transformed), n['name']))
minimum = [min(p[i] for p in points) for i in range(3)]
maximum = [max(p[i] for p in points) for i in range(3)]
assert all(math.isfinite(v) for p in points for v in p)
print('Highest compact surfaces:', sorted(heights, reverse=True)[:4])
assert triangles < 60000 and surfaces < 64
assert minimum[0] > -.75 and maximum[0] < .75, (minimum,maximum)
assert minimum[1] > -.75 and maximum[1] < .60, (minimum,maximum)
assert minimum[2] > -1.0 and maximum[2] < .80, (minimum,maximum)
print(json.dumps({'triangles':triangles,'surfaces':surfaces,'materials':len(doc['materials']),
    'compact_min':minimum,'compact_max':maximum,'rig_and_uvs':'PASS'},indent=2))
