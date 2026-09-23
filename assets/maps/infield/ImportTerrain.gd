@tool
extends EditorScenePostImport
## Bind one material field to rendering and collision identity without changing geometry.

const WATER_LEVEL_METRES := -0.45

func _post_import(scene: Node) -> Object:
	# Retain the immutable terrain source but replace its inherited TS-74 solids.
	# The standalone production structural set owns all tunnel collision now.
	for placeholder in scene.find_children("Tunnel*", "MeshInstance3D", true, false):
		placeholder.free()
	for body in scene.find_children("*", "StaticBody3D", true, false):
		if str(body.get_parent().name).begins_with("InfieldTerrain"):
			body.add_to_group("landing_terrain", true)
			body.add_to_group("water_terrain", true)
			body.set_meta("water_level", WATER_LEVEL_METRES)
			var field := load("res://assets/maps/infield/SurfaceField.tres") as ShaderMaterial
			body.set_meta("surface_field", field.get_shader_parameter("surface_field").resource_path)
			body.set_meta("surface_bounds", field.get_shader_parameter("surface_bounds"))
	for child in scene.find_children("*", "MeshInstance3D", true, false):
		var visual := child as MeshInstance3D
		for index in visual.mesh.get_surface_count():
			var material := visual.mesh.surface_get_material(index) as StandardMaterial3D
			material.roughness = 1.0
			if str(child.name).begins_with("InfieldTerrain"):
				visual.set_surface_override_material(index, load("res://assets/maps/infield/SurfaceField.tres"))
			else:
				material.albedo_color = Color(0.25, 0.27, 0.25)
	# Functional waterline shares the exact TS-77 field footprint; no collision plane.
	var water := MeshInstance3D.new()
	water.name = "WaterSurface"
	var plane := PlaneMesh.new()
	plane.size = Vector2(512, 256)
	water.mesh = plane
	water.position.y = WATER_LEVEL_METRES
	water.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	var water_material := ShaderMaterial.new()
	water_material.shader = load("res://assets/maps/infield/Water.gdshader")
	water_material.set_shader_parameter("surface_field", load("res://assets/maps/infield/surface_field.png"))
	water.material_override = water_material
	scene.add_child(water)
	water.owner = scene
	return scene
