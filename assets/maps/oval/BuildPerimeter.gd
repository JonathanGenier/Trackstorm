extends RefCounted
## Offline concrete/fence geometry. Visible surfaces and collision share the same section profile.
const PROFILE = [Vector2(0.65, 1.3), Vector2(0.65, 4.0), Vector2(0.5, 5.0), Vector2(0.15, 5.8), Vector2(-0.4, 6.4), Vector2(-1.1, 6.8)]

static func child(root: Node, parent: Node, node: Node, label: String) -> Node:
	node.name = label
	parent.add_child(node)
	node.owner = root
	return node

static func quad(tool: SurfaceTool, a: Vector3, b: Vector3, c: Vector3, d: Vector3, u: float = 0, length: float = 1, v: float = 0, height: float = 1) -> void:
	var points = [a,b,c,d]
	var uv = [Vector2(u,v),Vector2(u+length,v),Vector2(u+length,v+height),Vector2(u,v+height)]
	for index in [0,1,2,0,2,3]:
		tool.set_uv(uv[index])
		tool.add_vertex(points[index])

static func rod(tool: SurfaceTool, a: Vector3, b: Vector3, radius: float) -> void:
	var axis := (b-a).normalized()
	var side := axis.cross(Vector3.RIGHT if abs(axis.x) < .8 else Vector3.UP).normalized()
	var other := axis.cross(side)
	for i in 8:
		var x := (side*cos(i*TAU/8)+other*sin(i*TAU/8))*radius
		var y := (side*cos((i+1)*TAU/8)+other*sin((i+1)*TAU/8))*radius
		quad(tool,a+x,b+x,b+y,a+y)

static func bake(map: Node3D, content: Node, measurements: Dictionary) -> void:
	var concrete := SurfaceTool.new()
	var fence := SurfaceTool.new()
	var posts := SurfaceTool.new()
	for tool in [concrete,fence,posts]: tool.begin(Mesh.PRIMITIVE_TRIANGLES)
	var sections: Array = measurements.sections_godot
	var rims: Array[Vector3] = []
	var normals: Array[Vector3] = []
	var boundary := PackedVector3Array()
	for section: Array in sections:
		var a: Array = section[1]
		var b: Array = section[0]
		var rim := Vector3(a[0],a[1],a[2])
		var outward := Vector3(a[0]-b[0],0,a[2]-b[2]).normalized()
		rims.append(rim)
		normals.append(outward)
		boundary.append(rim+outward*1.2)
	var distance := 0.0
	var next_post := 0.0
	for i in sections.size():
		var j := (i+1)%sections.size()
		var a := rims[i]
		var b := rims[j]
		var na := normals[i]
		var nb := normals[j]
		var length := a.distance_to(b)
		# Closed concrete strip extends below the bank; no under-wall gaps or hidden upper wall.
		var ai := a-Vector3.UP*2
		var bi := b-Vector3.UP*2
		var at := a+Vector3.UP*1.3
		var bt := b+Vector3.UP*1.3
		quad(concrete,ai,bi,bt,at)
		quad(concrete,at,bt,bt+nb*1.2,at+na*1.2)
		quad(concrete,at+na*1.2,bt+nb*1.2,bi+nb*1.2,ai+na*1.2)
		quad(concrete,ai+na*1.2,bi+nb*1.2,bi,ai)
		for k in PROFILE.size()-1:
			var p: Vector2 = PROFILE[k]
			var q: Vector2 = PROFILE[k+1]
			var p0 := a+na*p.x+Vector3.UP*p.y
			var p1 := b+nb*p.x+Vector3.UP*p.y
			var q0 := a+na*q.x+Vector3.UP*q.y
			var q1 := b+nb*q.x+Vector3.UP*q.y
			quad(fence,p0,p1,q1,q0,distance,length,p.y,q.y-p.y)
			if distance >= next_post: rod(posts,p0,q0,.055)
		# Continuous upper/lower rail follows the curved visible mesh envelope.
		for k in [0,PROFILE.size()-1]:
			var p: Vector2 = PROFILE[k]
			rod(posts,a+na*p.x+Vector3.UP*p.y,b+nb*p.x+Vector3.UP*p.y,.035)
		if distance >= next_post: next_post = distance+4.5
		distance += length
	var body := child(map,content,StaticBody3D.new(),"PhysicalPerimeter") as StaticBody3D
	body.set_meta("outside_boundary",boundary)
	body.set_meta("minimum_height",-30.0)
	body.set_meta("surface_identity",load("res://assets/arena/materials/Concrete.tres").get_meta("surface_identity"))
	var metal := StandardMaterial3D.new()
	metal.albedo_color = Color(.32,.36,.4)
	metal.metallic = .7
	metal.roughness = .52
	var chain := ShaderMaterial.new()
	chain.shader = load("res://assets/maps/oval/CatchFence.gdshader")
	for entry in [[concrete,"ConcreteBarrier",load("res://assets/arena/materials/Concrete.tres")],[fence,"CatchFence",chain],[posts,"FenceSupports",metal]]:
		var tool: SurfaceTool = entry[0]
		tool.generate_normals()
		var mesh := tool.commit()
		var visual := child(map,body,MeshInstance3D.new(),entry[1]) as MeshInstance3D
		visual.mesh = mesh
		visual.material_override = entry[2]
		if entry[1] != "FenceSupports":
			var shape := mesh.create_trimesh_shape()
			shape.backface_collision = true
			var collider := child(map,body,CollisionShape3D.new(),entry[1]+"Collision") as CollisionShape3D
			collider.shape = shape
