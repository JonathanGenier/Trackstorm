@tool
extends EditorScenePostImport
## Use the Blender terrain's authored color attribute under normal world lighting.

func _post_import(scene: Node) -> Object:
	# Retain the immutable terrain source but replace its inherited TS-74 solids.
	# The standalone production structural set owns all tunnel collision now.
	for placeholder in scene.find_children("Tunnel*", "MeshInstance3D", true, false):
		placeholder.free()
	for body in scene.find_children("*", "StaticBody3D", true, false):
		if str(body.get_parent().name).begins_with("InfieldTerrain"):
			body.add_to_group("landing_terrain", true)
	for child in scene.find_children("*", "MeshInstance3D", true, false):
		var visual := child as MeshInstance3D
		for index in visual.mesh.get_surface_count():
			var material := visual.mesh.surface_get_material(index) as StandardMaterial3D
			material.roughness = 1.0
			if str(child.name).begins_with("InfieldTerrain"):
				material.vertex_color_use_as_albedo = true
				material.albedo_color = Color.WHITE
			else:
				material.albedo_color = Color(0.25, 0.27, 0.25)
	return scene
