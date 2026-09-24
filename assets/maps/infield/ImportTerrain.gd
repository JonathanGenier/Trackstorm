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
	var field := load("res://assets/maps/infield/SurfaceField.tres") as ShaderMaterial
	var texture := field.get_shader_parameter("surface_field") as Texture2D
	var bounds: Vector4 = field.get_shader_parameter("surface_bounds")
	var image := texture.get_image()
	# Include every potentially visible texel plus a bilinear-filter guard band.
	# This is an import-time optimization, never a second gameplay water field.
	var minimum := Vector2i(image.get_width(), image.get_height())
	var maximum := Vector2i(-1, -1)
	for y in image.get_height():
		for x in image.get_width():
			if image.get_pixel(x, y).a >= 0.5:
				minimum = minimum.min(Vector2i(x, y))
				maximum = maximum.max(Vector2i(x, y))
	assert(maximum.x >= 0, "The authored water field must contain water.")
	var texel := Vector2(bounds.z / image.get_width(), bounds.w / image.get_height())
	var start := Vector2(bounds.x, bounds.y) + Vector2(minimum - Vector2i.ONE) * texel
	var end := Vector2(bounds.x, bounds.y) + Vector2(maximum + Vector2i(2, 2)) * texel
	plane.size = end - start
	water.mesh = plane
	water.position = Vector3((start.x + end.x) * 0.5, WATER_LEVEL_METRES, (start.y + end.y) * 0.5)
	water.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	var water_material := ShaderMaterial.new()
	water_material.shader = load("res://assets/maps/infield/Water.gdshader")
	water_material.set_shader_parameter("surface_field", texture)
	water_material.set_shader_parameter("surface_bounds", bounds)
	water_material.set_shader_parameter("water_origin", Vector2(water.position.x, water.position.z))
	water.material_override = water_material
	scene.add_child(water)
	water.owner = scene
	return scene
