@tool
extends EditorScenePostImport
## Bind one material field to rendering and collision identity without changing geometry.

func _post_import(scene: Node) -> Object:
	# Retain the immutable terrain source but replace its inherited TS-74 solids.
	# The standalone production structural set owns all tunnel collision now.
	for placeholder in scene.find_children("Tunnel*", "MeshInstance3D", true, false):
		placeholder.free()
	for body in scene.find_children("*", "StaticBody3D", true, false):
		if str(body.get_parent().name).begins_with("InfieldTerrain"):
			body.add_to_group("landing_terrain", true)
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
	return scene
