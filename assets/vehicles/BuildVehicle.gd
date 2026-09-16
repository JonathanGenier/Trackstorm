extends SceneTree
## Offline authoring tool: godot --headless --path . --script assets/vehicles/BuildVehicle.gd
## Rebuilds the static scene from the preserved Kenney source; never runs in gameplay.

var model := Node3D.new()
var armor = load("res://assets/vehicles/materials/Armor.tres")

func _initialize():
	model.name = "WastelandVehicle"
	var source = load("res://assets/vehicles/kenney/hatchback-sports.glb").instantiate()
	var palette = load("res://assets/vehicles/kenney/Textures/colormap.png").get_image()
	var trim := StandardMaterial3D.new()
	trim.albedo_color = Color(0.11, 0.15, 0.17)
	trim.metallic = 0.45
	trim.roughness = 0.65
	var body = load("res://assets/vehicles/materials/Body.tres")
	var tire = load("res://assets/vehicles/materials/Tire.tres")
	for original in source.find_children("*", "MeshInstance3D"):
		var mesh := MeshInstance3D.new()
		mesh.name = original.name
		var surface := ArrayMesh.new()
		var groups := {}
		var arrays = original.mesh.surface_get_arrays(0)
		var indices = arrays[Mesh.ARRAY_INDEX]
		for t in range(0, indices.size(), 3):
			var uv = arrays[Mesh.ARRAY_TEX_UV][indices[t]]
			var color = palette.get_pixel(clampi(int(uv.x * palette.get_width()), 0, palette.get_width()-1), clampi(int(uv.y * palette.get_height()), 0, palette.get_height()-1))
			var key = "paint" if color.s > 0.35 else "trim"
			if "wheel" in str(original.name):
				key = "rubber" if color.v < 0.5 else "trim"
			if not groups.has(key):
				groups[key] = SurfaceTool.new()
				groups[key].begin(Mesh.PRIMITIVE_TRIANGLES)
				groups[key].set_material(body if key == "paint" else tire if key == "rubber" else trim)
			for k in range(3):
				var i = indices[t+k]
				groups[key].set_normal(arrays[Mesh.ARRAY_NORMAL][i])
				groups[key].set_uv(arrays[Mesh.ARRAY_TEX_UV][i])
				groups[key].add_vertex(arrays[Mesh.ARRAY_VERTEX][i])
		for group in groups.values():
			group.generate_tangents()
			group.commit(surface)
		mesh.mesh = surface
		mesh.transform = original.transform
		var base := Node3D.new()
		base.name = str(original.name) + "Mount"
		base.scale = Vector3(1.4, 1.18, 1.17)
		base.rotation.y = PI
		base.position.y = -0.5
		model.add_child(base)
		base.owner = model
		base.add_child(mesh)
		mesh.owner = model
	# Original fabricated armor, kept within the 2 x 3.6 metre footprint.
	box("FrontRam", Vector3(1.82, 0.23, 0.18), Vector3(0, -0.06, -1.7))
	box("RearGuard", Vector3(1.75, 0.16, 0.13), Vector3(0, -0.05, 1.65))
	box("HoodPlate", Vector3(1.12, 0.06, 0.67), Vector3(-0.06, 0.34, -0.95), Vector3(-0.10, 0.04, 0))
	box("RoofSalvage", Vector3(1.13, 0.055, 0.77), Vector3(0, 0.80, 0.14), Vector3(0, 0.04, 0.025))
	for side in [-1, 1]:
		box("DoorArmor" + str(side), Vector3(0.065, 0.33, 1.02), Vector3(side * 0.91, 0.13, 0.15), Vector3(0.03, 0, side * 0.06))
		box("Sill" + str(side), Vector3(0.10, 0.12, 1.6), Vector3(side * 0.92, -0.17, 0.1))
		for z in [-0.25, 0.05, 0.35]:
			box("Weld" + str(side) + str(z), Vector3(0.018, 0.27, 0.024), Vector3(side * 0.95, 0.13, z))
		var pipe := MeshInstance3D.new()
		pipe.name = "Exhaust" + str(side)
		var cylinder := CylinderMesh.new()
		cylinder.top_radius = 0.055
		cylinder.bottom_radius = 0.065
		cylinder.height = 0.76
		cylinder.radial_segments = 10
		pipe.mesh = cylinder
		pipe.material_override = armor
		pipe.position = Vector3(side * 0.79, 0.35, 1.15)
		pipe.rotation.z = side * -0.14
		model.add_child(pipe)
		pipe.owner = model
	# Static windshield protection leaves the glass readable.
	for x in [-0.43, 0, 0.43]:
		box("ScreenBar" + str(x), Vector3(0.035, 0.42, 0.045), Vector3(x, 0.59, -0.48), Vector3(-0.48, 0, 0))
	box("Identification", Vector3(0.28, 0.025, 0.65), Vector3(0.18, 0.84, 0.14))
	# Static tires meet level ground at the unchanged suspension's settled ride height.
	for mount in model.get_children():
		mount.position.y -= 0.235
	var scene := PackedScene.new()
	scene.pack(model)
	ResourceSaver.save(scene, "res://assets/vehicles/WastelandVehicle.tscn")
	source.free()
	model.free()
	quit()

func box(label: String, size: Vector3, position: Vector3, rotation := Vector3.ZERO):
	var node := MeshInstance3D.new()
	node.name = label
	var mesh := BoxMesh.new()
	mesh.size = size
	node.mesh = mesh
	node.position = position
	node.rotation = rotation
	node.material_override = armor
	model.add_child(node)
	node.owner = model
