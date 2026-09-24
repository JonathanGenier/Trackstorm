extends SceneTree
## Standalone native asset gallery and physics checks; never instances into the active map.

const BASE := "res://assets/environment/"
var failures: Array[String] = []
var world: Node3D
var camera: Camera3D
var catalog: Dictionary
var gallery: Array[Node3D] = []
var checks := 0
var lod_surfaces := 0
var triangle_count := 0

func _initialize() -> void:
	call_deferred("run")

func require(condition: bool, message: String) -> void:
	checks += 1
	if not condition:
		failures.append(message)
		push_error(message)

func run() -> void:
	catalog = JSON.parse_string(FileAccess.get_file_as_string(BASE + "library.json"))
	world = Node3D.new()
	root.add_child(world)
	var environment := WorldEnvironment.new()
	environment.environment = load("res://assets/maps/oval/Daylight.tres")
	world.add_child(environment)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-48, -32, 0)
	sun.light_energy = 1.6
	sun.shadow_enabled = true
	world.add_child(sun)
	var ground := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(90, 90)
	ground.mesh = plane
	ground.material_override = load("res://assets/maps/oval/Grass.tres")
	ground.position = Vector3(18, -.025, 18)
	world.add_child(ground)
	camera = Camera3D.new()
	camera.far = 1500
	world.add_child(camera)
	camera.make_current()
	for index in catalog.assets.size():
		var entry: Dictionary = catalog.assets[index]
		var packed := load(BASE + str(entry.model)) as PackedScene
		require(packed != null, entry.id + ": PackedScene import")
		if packed == null:
			continue
		var first := packed.instantiate() as Node3D
		world.add_child(first)
		require(first.transform.is_equal_approx(Transform3D.IDENTITY), entry.id + ": metre root transform")
		first.position = Vector3((index % 4) * 13, 0, (index / 4) * 13)
		gallery.append(first)
		var meshes := first.find_children("*", "MeshInstance3D", true, false)
		require(meshes.size() == 1, entry.id + ": one reusable visual mesh")
		var aabb := AABB()
		for child in meshes:
			var instance := child as MeshInstance3D
			aabb = instance.get_aabb()
			require(instance.transform.is_equal_approx(Transform3D.IDENTITY), entry.id + ": baked visual transform")
			var expected: Array = entry.blender_bounds
			var expected_size := Vector3(expected[0][1]-expected[0][0], expected[2][1]-expected[2][0], expected[1][1]-expected[1][0])
			require(aabb.size.distance_to(expected_size) < .002, entry.id + ": Godot Y-up bounds match Blender metres")
			for surface in instance.mesh.get_surface_count():
				var arrays := instance.mesh.surface_get_arrays(surface)
				require(arrays[Mesh.ARRAY_TEX_UV].size() > 0, entry.id + ": UVs retained")
				require(arrays[Mesh.ARRAY_COLOR] != null and arrays[Mesh.ARRAY_COLOR].size() > 0, entry.id + ": vertex shading retained")
				require(instance.get_active_material(surface) != null, entry.id + ": material slot resolves")
				triangle_count += arrays[Mesh.ARRAY_INDEX].size() / 3
				var info := RenderingServer.mesh_get_surface(instance.mesh.get_rid(), surface)
				if info.has("lods") and not info.lods.is_empty():
					lod_surfaces += 1
		var bodies := first.find_children("*", "StaticBody3D", true, false)
		require(bodies.size() == int(entry.collision_parts), entry.id + ": authored collision part count")
		for body in bodies:
			require(body.collision_layer == 1, entry.id + ": vehicle collision layer")
			for shape in body.find_children("*", "CollisionShape3D", true, false):
				require(shape.shape is ConvexPolygonShape3D, entry.id + ": simple convex source import")
		var second := packed.instantiate() as Node3D
		world.add_child(second)
		second.position = first.position + Vector3(0, 0, 100)
		second.rotation.y = PI * .5
		var other := second.find_children("*", "MeshInstance3D", true, false)
		require(other[0].mesh == meshes[0].mesh, entry.id + ": instances share mesh resource")
		require(first.rotation.is_zero_approx(), entry.id + ": independent placement transforms")
		second.queue_free()
	await physics_frame
	await physics_frame
	require(lod_surfaces > 0, "Import generated nonempty native LOD buffers")
	# Open U channel must not become an enclosing convex block.
	var drain := gallery[15]
	var space := world.get_world_3d().direct_space_state
	var along := PhysicsRayQueryParameters3D.create(drain.position+Vector3(-2,.3,0), drain.position+Vector3(2,.3,0))
	require(space.intersect_ray(along).is_empty(), "Drain open longitudinal passage")
	var across := PhysicsRayQueryParameters3D.create(drain.position+Vector3(0,.3,-1), drain.position+Vector3(0,.3,1))
	require(not space.intersect_ray(across).is_empty(), "Drain lips retain solid collision")
	var repeat_modules := Node3D.new()
	world.add_child(repeat_modules)
	for specification in [["Guardrail4m", 4.0, 0.0], ["Barrier3m", 3.0, 4.0], ["DrainChannel2m", 2.0, 8.0]]:
		var module := load(BASE+"models/"+specification[0]+".glb") as PackedScene
		for i in 3:
			var item := module.instantiate() as Node3D
			repeat_modules.add_child(item)
			item.position = Vector3(-28+i*specification[1],0,20+specification[2])
		await physics_frame
		await physics_frame
		if specification[0] != "DrainChannel2m":
			for i in 2:
				var seam := Vector3(-28+(i+.5)*specification[1],.55,20+specification[2])
				for delta in [-.01,.01]:
					var ray := PhysicsRayQueryParameters3D.create(seam+Vector3(delta,0,-1),seam+Vector3(delta,0,1))
					require(not space.intersect_ray(ray).is_empty(), specification[0]+": joined segment collision near seam")
	# Native rigid-body impacts exercise source collision under repeated reuse/rotation.
	for asset_name in ["BoulderTall", "Barrier3m", "Guardrail4m", "LightPole9m"]:
		for angle in [0.0, PI*.5]:
			await impact(asset_name, angle)
	var reference := load("res://assets/maps/oval/reference_vehicle.glb").instantiate() as Node3D
	world.add_child(reference)
	var reference_bounds := AABB()
	var first_bounds := true
	for part in reference.find_children("*", "MeshInstance3D", true, false):
		var bounds: AABB = reference.global_transform.affine_inverse() * part.global_transform * part.get_aabb()
		reference_bounds = bounds if first_bounds else reference_bounds.merge(bounds)
		first_bounds = false
	reference.position = Vector3(20, .05, 46) - Vector3(reference_bounds.get_center().x, reference_bounds.position.y, reference_bounds.get_center().z)
	print("Reference vehicle bounds: ", reference_bounds)
	require(abs(max(reference_bounds.size.x, reference_bounds.size.z)-4.81)<.05, "Reference vehicle remains 4.81 m long")
	var visual := not OS.get_cmdline_user_args().has("--no-visual")
	if visual:
		root.size = Vector2i(1600, 1000)
		camera.projection = Camera3D.PROJECTION_ORTHOGONAL
		camera.size = 61
		await capture("gallery", Vector3(60,43,77), Vector3(18,3,20))
		camera.projection = Camera3D.PROJECTION_PERSPECTIVE
		await capture("rocks", Vector3(12,7,17), Vector3(7,1,0))
		await capture("scrub", Vector3(31,2.7,20), Vector3(28, .6,13))
		await capture("infrastructure", Vector3(24,7,55), Vector3(23,2,39))
		await capture("conifers", Vector3(28,11,48), Vector3(25,6,26))
		await capture("rock-detail", Vector3(4,2.6,5), Vector3(0,.8,0))
		await capture("grass-detail", Vector3(39.9,.65,14.4), Vector3(39,.2,13))
		await capture("scrub-detail", Vector3(27.5,1.4,15.4), Vector3(26,.7,13))
		await capture("ledge-detail", Vector3(6,3.5,19), Vector3(0,1.3,13))
		await capture("module-reuse", Vector3(-17,6,36), Vector3(-23,.4,24))
		# Compare native LODs with base geometry at the same distant view.
		camera.position = Vector3(25,12,95)
		camera.look_at(Vector3(20,5,26))
		root.mesh_lod_threshold = 0.0
		await capture("distance-base",camera.position,Vector3(20,5,26))
		root.mesh_lod_threshold = 2.0
		await capture("distance-lod",camera.position,Vector3(20,5,26))
		# Actual map lighting/terrain context without saving any production placement.
		for item in gallery:
			item.visible = false
		repeat_modules.visible = false
		ground.visible = false
		reference.visible = false
		var map := load("res://scenes/maps/oval_foundation.tscn").instantiate() as Node3D
		world.add_child(map)
		await physics_frame
		await physics_frame
		for i in 3:
			var item := gallery[i]
			var x := -61.0+i*4
			var hit := space.intersect_ray(PhysicsRayQueryParameters3D.create(Vector3(x,40,10),Vector3(x,-15,10)))
			if not hit.is_empty():
				item.position = hit.position
				item.visible = true
		await capture("map-context",Vector3(-43,15,35),Vector3(-57,2,10))
	print("Environment library checks: %d assertions, %d triangles, %d surfaces with native LODs, 8 native impacts." % [checks,triangle_count,lod_surfaces])
	if failures.is_empty():
		print("Environment library passed.")
	else:
		print("Environment library FAILED: ",failures)
	quit(0 if failures.is_empty() else 1)

func impact(asset_name: String, angle: float) -> void:
	var packed := load(BASE+"models/"+asset_name+".glb") as PackedScene
	var target := packed.instantiate() as Node3D
	world.add_child(target)
	target.position = Vector3(-40,0,-40)
	target.rotation.y = angle
	var body := RigidBody3D.new()
	body.mass = 900
	body.gravity_scale = 0
	body.continuous_cd = true
	body.contact_monitor = true
	body.max_contacts_reported = 8
	var collision := CollisionShape3D.new()
	var shape := SphereShape3D.new()
	shape.radius = .35
	collision.shape = shape
	body.add_child(collision)
	world.add_child(body)
	var direction := Vector3(0,0,1).rotated(Vector3.UP,angle)
	body.position = target.position-direction*6+Vector3(0,.55,0)
	body.linear_velocity = direction*12
	var contacted := false
	for frame in 100:
		await physics_frame
		contacted = contacted or body.get_contact_count() > 0
	var advance := (body.position-target.position).dot(direction)
	require(contacted and advance < .5, asset_name+": native impact stops proxy at rotation "+str(angle))
	body.queue_free()
	target.queue_free()
	await physics_frame

func capture(label: String, eye: Vector3, target: Vector3) -> void:
	camera.position = eye
	camera.look_at(target)
	for frame in 12:
		await process_frame
	await RenderingServer.frame_post_draw
	DirAccess.make_dir_recursive_absolute("res://.godot/environment-checks")
	var error := root.get_texture().get_image().save_png("res://.godot/environment-checks/"+label+".png")
	require(error == OK, "Native rendered capture "+label)
