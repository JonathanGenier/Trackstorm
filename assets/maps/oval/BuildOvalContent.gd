extends RefCounted
## Offline dressing of the immutable production sections. No runtime geometry generation.

static func vec(a: Array) -> Vector3:
	return Vector3(a[0], a[1], a[2])

static func child(root: Node, parent: Node, node: Node, label: String) -> Node:
	node.name = label
	parent.add_child(node)
	node.owner = root
	return node

static func bake(map: Node3D, measurements: Dictionary) -> void:
	assert(DisplayServer.get_name() != "headless", "Bake with a rendering backend: headless discards MultiMesh transforms.")
	var geometry := map.get_node("Geometry")
	map.set_editable_instance(geometry, true)
	for pair in [["Track", "Asphalt"], ["Infield", "Grass"]]:
		var mesh := geometry.get_node(pair[0]) as MeshInstance3D
		mesh.material_override = load("res://assets/maps/oval/" + pair[1] + ".tres")
	var content := map.get_node("MapContent")
	var sections: Array = measurements.sections_godot
	var inner: Array[Vector3] = []
	var outer: Array[Vector3] = []
	for section: Array in sections:
		inner.append(vec(section[0]))
		outer.append(vec(section[1]))
	# Closed strip follows every master section, with its inner face exactly at the outer edge.
	var barrier := SurfaceTool.new()
	barrier.begin(Mesh.PRIMITIVE_TRIANGLES)
	var wall := child(map, content, StaticBody3D.new(), "OuterContainment") as StaticBody3D
	for i in outer.size():
		var j := (i + 1) % outer.size()
		var a := outer[i]
		var b := outer[j]
		var na := Vector3(a.x-inner[i].x, 0, a.z-inner[i].z).normalized()
		var nb := Vector3(b.x-inner[j].x, 0, b.z-inner[j].z).normalized()
		var points: Array[Vector3] = [a-Vector3.UP*.3,b-Vector3.UP*.3,b+Vector3.UP*1.3,a+Vector3.UP*1.3,a+na*1.2-Vector3.UP*.3,b+nb*1.2-Vector3.UP*.3,b+nb*1.2+Vector3.UP*1.3,a+na*1.2+Vector3.UP*1.3]
		for face in [[0,2,1],[0,3,2],[3,7,6],[3,6,2],[4,5,6],[4,6,7]]:
			for index: int in face:
				barrier.add_vertex(points[index])
	# Four-section collision spans avoid hundreds of redundant physics shapes.
	# A 5 cm exterior offset keeps the chord outside the curved road surface.
	for i in range(0, outer.size(), 4):
		var j := (i + 4) % outer.size()
		var na := Vector3(outer[i].x-inner[i].x, 0, outer[i].z-inner[i].z).normalized()
		var nb := Vector3(outer[j].x-inner[j].x, 0, outer[j].z-inner[j].z).normalized()
		var a := outer[i] + na*.05
		var b := outer[j] + nb*.05
		# Overlapping solid convex prisms: thickness is exterior, usable road stays 18 m.
		# Vertical extent includes the visible barrier and invisible upper containment.
		var tangent := (b-a).normalized()*.025
		var hull := PackedVector3Array()
		for endpoint in [[a-tangent,na],[b+tangent,nb]]:
			var p: Vector3 = endpoint[0]
			var n: Vector3 = endpoint[1]
			for offset in [Vector3.ZERO,n*4.0]:
				hull.append(p+offset-Vector3.UP*2)
				hull.append(Vector3(p.x,45,p.z)+offset)
		var shape := ConvexPolygonShape3D.new()
		shape.points = hull
		var collider := child(map, wall, CollisionShape3D.new(), "Section%03d" % i) as CollisionShape3D
		collider.shape = shape
	barrier.generate_normals()
	var concrete := child(map, content, MeshInstance3D.new(), "ConcreteBarrier") as MeshInstance3D
	concrete.mesh = barrier.commit()
	concrete.material_override = load("res://assets/arena/materials/Concrete.tres")
	# Reference image placement only: approximate X/Z targets projected to master sections.
	# Bottom is +Z, grid faces +X. Lanes are 3/9/15 m across the 18 m surface.
	var markers := child(map, map, Node3D.new(), "ItemSpawns")
	var groups := [Vector2(-65,91),Vector2(169,66),Vector2(150,-79),Vector2(-144,-83),Vector2(-190,36)]
	var singles := [[Vector2(40,97),15.0],[Vector2(79,-85),3.0],[Vector2(201,-27),15.0],[Vector2(-187,-47),15.0],[Vector2(-151,79),15.0]]
	for group in groups.size():
		for lane in 3:
			marker(map,markers,inner,outer,groups[group],3.0+lane*6.0,"item-triple-%02d-%d" % [group+1,lane+1])
	for i in singles.size():
		marker(map,markers,inner,outer,singles[i][0],singles[i][1],"item-single-%02d" % (i+1))
	# Exterior annulus follows the banked rim and slopes into forest ground, never covering infield.
	var ground := SurfaceTool.new()
	ground.begin(Mesh.PRIMITIVE_TRIANGLES)
	for i in outer.size():
		var j := (i+1)%outer.size()
		var a := outer[i]
		var b := outer[j]
		var na := Vector3(a.x-inner[i].x,0,a.z-inner[i].z).normalized()
		var nb := Vector3(b.x-inner[j].x,0,b.z-inner[j].z).normalized()
		var c := Vector3(b.x,0,b.z)+nb*700
		var d := Vector3(a.x,0,a.z)+na*700
		for p in [a,b,c,a,c,d]: ground.add_vertex(p)
	ground.generate_normals()
	var terrain := child(map,content,MeshInstance3D.new(),"ExteriorGround") as MeshInstance3D
	terrain.mesh = ground.commit()
	terrain.material_override = load("res://assets/maps/oval/Grass.tres")
	var trees := load("res://assets/maps/oval/conifers.glb").instantiate() as Node3D
	for tree: MeshInstance3D in trees.get_children():
		for surface in tree.mesh.get_surface_count():
			var material := tree.mesh.surface_get_material(surface).duplicate() as StandardMaterial3D
			material.albedo_color *= Color(0.35, 0.45, 0.3, 1)
			material.metallic_specular = 0.0
			material.roughness = 1.0
			tree.mesh.surface_set_material(surface, material)
	var random := RandomNumberGenerator.new()
	random.seed = 200
	var occupied: Array[Vector3] = []
	# Irregular mixed-age stands, not species-specific rings. Chunking still bounds draw calls.
	for chunk in 16:
		var centers: Array[Vector2] = []
		for stand in 4:
			centers.append(Vector2((chunk+random.randf())*outer.size()/16.0,random.randf_range(25,95)))
		for variant in 3:
			var transforms: Array[Transform3D] = []
			for step in 18:
				var p := Vector3.ZERO
				for attempt in 100:
					var center := centers[random.randi_range(0,centers.size()-1)]
					var section := fposmod(center.x+random.randfn(0,13),outer.size())
					var i := int(section)
					var j := (i+1)%outer.size()
					var rim := outer[i].lerp(outer[j],section-i)
					var inside := inner[i].lerp(inner[j],section-i)
					var outward := Vector3(rim.x-inside.x,0,rim.z-inside.z).normalized()
					var distance := clampf(center.y+random.randfn(0,17),12.5,125)
					p = rim+outward*distance
					p.y = rim.y*(1-distance/700.0)-.1
					if occupied.all(func(other: Vector3) -> bool: return Vector2(other.x,other.z).distance_to(Vector2(p.x,p.z))>3.5):
						break
					assert(attempt<99,"Forest placement exhausted its spacing budget.")
				occupied.append(p)
				var scale := random.randf_range(.65,1.35)
				transforms.append(Transform3D(Basis(Vector3.UP,random.randf()*TAU).scaled(Vector3(scale*random.randf_range(.9,1.15),scale*random.randf_range(.85,1.2),scale)),p))
			var batch := child(map,content,MultiMeshInstance3D.new(),"Forest%02d_%d" % [chunk,variant]) as MultiMeshInstance3D
			batch.multimesh = MultiMesh.new()
			batch.multimesh.transform_format = MultiMesh.TRANSFORM_3D
			batch.multimesh.mesh = (trees.get_node("Conifer%d" % variant) as MeshInstance3D).mesh
			batch.multimesh.instance_count = transforms.size()
			for i in transforms.size(): batch.multimesh.set_instance_transform(i,transforms[i])
	trees.free()

static func marker(map: Node3D, parent: Node, inner: Array[Vector3], outer: Array[Vector3], target: Vector2, lane: float, id: String) -> void:
	var nearest := 0
	var distance := INF
	for i in inner.size():
		var center := (inner[i]+outer[i])*.5
		var candidate := Vector2(center.x,center.z).distance_squared_to(target)
		if candidate < distance:
			distance = candidate
			nearest = i
	var spawn := child(map,parent,Marker3D.new(),id) as Marker3D
	spawn.position = inner[nearest].lerp(outer[nearest],lane/18.0)
	spawn.set_meta("surface_lane_m",lane)
	spawn.set_meta("source_section",nearest)
