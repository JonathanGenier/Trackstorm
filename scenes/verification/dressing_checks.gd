extends SceneTree
## Checks the committed production composition, not a temporary prop gallery.

var failures: Array[String] = []
var checks := 0
var map: Node3D
var camera: Camera3D

func _initialize() -> void:
	call_deferred("run")

func require(value: bool, message: String) -> void:
	checks += 1
	if not value:
		failures.append(message)
		push_error(message)

func run() -> void:
	map = load("res://scenes/maps/oval_foundation.tscn").instantiate()
	root.add_child(map)
	await physics_frame
	await physics_frame
	var layer := map.get_node_or_null("EnvironmentDressing")
	require(layer != null,"Production map includes environment dressing")
	if layer == null:
		quit(1)
		return
	var meshes: Dictionary = {}
	var props := 0
	var foliage := 0
	for node in layer.find_children("*","MeshInstance3D",true,false):
		var path: String = node.mesh.resource_path
		require(not path.is_empty(),"Prop mesh retains imported resource identity")
		if meshes.has(path):
			require(meshes[path] == node.mesh,"Repeated props share imported meshes")
		meshes[path] = node.mesh
		props += 1
	for node in layer.find_children("*","MultiMeshInstance3D",true,false):
		foliage += node.multimesh.instance_count
		require(node.multimesh.mesh != null,"Foliage batch retains source mesh")
		require(node.multimesh.instance_count > 0,"Foliage spatial batch is populated")
		for i in node.multimesh.instance_count:
			var position: Vector3 = node.position+node.multimesh.get_instance_transform(i).origin
			require(position.length() > 1,"Saved foliage transform survives reload")
			require(abs(position.x-node.position.x) <= 16 and abs(position.z-node.position.z) <= 16,"Foliage cutoff uses local spatial cell")
	require(props > 80,"Major areas contain substantial reused rock/infrastructure props")
	require(foliage > 300,"Ground cover is populated")
	# Independent native overlap probes cover the lane width plus recovery shoulders.
	# Existing terrain/structures are allowed; no new dressing collider may intrude.
	var space := map.get_world_3d().direct_space_state
	var layout: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://assets/maps/infield/layout.json"))
	var box := BoxShape3D.new()
	box.size = Vector3(3.2,2.4,5.5)
	var query := PhysicsShapeQueryParameters3D.new()
	query.shape = box
	query.collision_mask = 1
	var probes := 0
	var dressing_bodies: Array[RID] = []
	for body in layer.find_children("*","StaticBody3D",true,false):
		dressing_bodies.append(body.get_rid())
	for route: Dictionary in layout.routes:
		for i in range(0,route.points.size()-1,3):
			var p := Vector3(route.points[i][0],0,route.points[i][1])
			var next := Vector3(route.points[i+1][0],0,route.points[i+1][1])
			var tangent := (next-p).normalized()
			var side := tangent.cross(Vector3.UP)
			for offset in [-float(route.width_m)*.5-1.5,0.0,float(route.width_m)*.5+1.5]:
				var lane: Vector3 = p+side*offset
				var ground_ray := PhysicsRayQueryParameters3D.create(lane+Vector3.UP*20,lane-Vector3.UP*8,1)
				ground_ray.exclude = dressing_bodies
				var ground := space.intersect_ray(ground_ray)
				if ground.is_empty():
					continue
				lane.y = ground.position.y+1.5
				query.transform = Transform3D(Basis(Vector3.UP,atan2(-tangent.x,-tangent.z)),lane)
				for hit in space.intersect_shape(query,64):
					require(not layer.is_ancestor_of(hit.collider),"Dressing blocks lane/shoulder: %s at %s" % [route.id,lane])
				probes += 1
	if DisplayServer.get_name() != "headless":
		var environment := WorldEnvironment.new()
		environment.environment = load("res://assets/maps/oval/Daylight.tres")
		root.add_child(environment)
		var sun := DirectionalLight3D.new()
		sun.rotation_degrees = Vector3(-65,-25,0)
		sun.light_energy = 1.4
		sun.shadow_enabled = true
		root.add_child(sun)
		camera = Camera3D.new()
		camera.far = 1800
		camera.fov = 55
		root.add_child(camera)
		camera.make_current()
		await capture("overview",Vector3(0,285,190),Vector3.ZERO)
		await capture("west-islands",Vector3(-105,62,65),Vector3(-90,0,-15))
		await capture("east-islands",Vector3(110,60,60),Vector3(90,0,-15))
		await capture("north-ridge",Vector3(-75,9,-43),Vector3(-45,2,-68))
		await capture("south-island",Vector3(-15,6,40),Vector3(-46,2,64))
		await capture("tunnel-sightline",Vector3(0,2.5,24),Vector3(0,2.5,-40))
		await capture("jump-approach",Vector3(-128,4,1),Vector3(-60,7,0))
		await capture("perimeter",Vector3(25,4,89),Vector3(90,8,94))
	print("Dressing checks: %d assertions; %d vehicle envelope probes; %d props; %d foliage; %d failures." % [checks,probes,props,foliage,failures.size()])
	quit(0 if failures.is_empty() else 1)

func capture(label: String, eye: Vector3, target: Vector3) -> void:
	camera.position = eye
	camera.look_at(target)
	for i in 12:
		await process_frame
	await RenderingServer.frame_post_draw
	DirAccess.make_dir_recursive_absolute("res://.godot/dressing-checks")
	require(root.get_texture().get_image().save_png("res://.godot/dressing-checks/"+label+".png") == OK,"Rendered "+label)
