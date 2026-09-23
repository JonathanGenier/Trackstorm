@tool
extends EditorScenePostImport
## Use the Blender terrain's authored color attribute under normal world lighting.

func _post_import(scene: Node) -> Object:
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
