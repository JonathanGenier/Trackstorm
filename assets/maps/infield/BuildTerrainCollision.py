"""Bake bounded-complexity collision from the unchanged authored terrain.

Run with Blender 5.2.2, then BakeTerrainCollision.gd with Godot 4.7.2.
The render mesh and editable source are read only. All distances are metres.
"""
import hashlib
import json
import struct
from pathlib import Path

import bmesh
import bpy
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[2]
SOURCE = ROOT / 'source/InfieldTerrain.blend'
OUTPUT = PROJECT / '.godot/terrain-collision-bake'
OUTPUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
source = bpy.data.objects['InfieldTerrain-col'].data
original = bmesh.new()
original.from_mesh(source)
original_tree = BVHTree.FromBMesh(original)
probe = bpy.data.objects.new('TerrainCollisionBake', source.copy())
bpy.context.collection.objects.link(probe)
modifier = probe.modifiers.new('Bounded collision complexity', 'DECIMATE')
modifier.ratio = 0.1
modifier.use_collapse_triangulate = True
evaluated = probe.evaluated_get(bpy.context.evaluated_depsgraph_get())
mesh = bmesh.new()
mesh.from_mesh(evaluated.to_mesh())
bmesh.ops.triangulate(mesh, faces=list(mesh.faces))
tree = BVHTree.FromBMesh(mesh)

# Audit both directions: retaining original vertices alone misses new faces
# bridging a valley. Centroids supplement the vertices; runtime ray/route
# checks independently verify support and the track seam.
maximum = 0.0
for geometry, other in [(original, tree), (mesh, original_tree)]:
    for vertex in geometry.verts:
        maximum = max(maximum, other.find_nearest(vertex.co)[3])
    for face in geometry.faces:
        maximum = max(maximum, other.find_nearest(face.calc_center_median())[3])
boundary = max(tree.find_nearest(vertex.co)[3] for vertex in original.verts if vertex.is_boundary)
assert maximum < 0.04, f'Collision deviation exceeds 4 cm: {maximum}'
assert boundary < 0.002, f'Outer seam deviation exceeds 2 mm: {boundary}'
assert len(mesh.faces) <= 40500, 'Terrain collision triangle budget exceeded'
with (OUTPUT / 'faces.bin').open('wb') as output:
    for face in mesh.faces:
        # Blender Z-up/CCW to Godot Y-up/clockwise.
        for vertex in reversed(face.verts):
            point = vertex.co
            output.write(struct.pack('<fff', point.x, point.z, -point.y))
report = dict(
    source='source/InfieldTerrain.blend', source_sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
    render_source='infield_terrain.glb', render_sha256=hashlib.sha256((ROOT / 'infield_terrain.glb').read_bytes()).hexdigest(),
    blender_version=bpy.app.version_string, ratio=modifier.ratio,
    source_triangles=len(original.faces), collision_triangles=len(mesh.faces),
    maximum_bidirectional_vertex_centroid_distance_m=maximum, maximum_boundary_distance_m=boundary,
    provenance='Original Trackstorm terrain; no new third-party geometry. Visual/source geometry unchanged.',
)
(OUTPUT / 'audit.json').write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report))
