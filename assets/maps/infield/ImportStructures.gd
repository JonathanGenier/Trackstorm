@tool
extends EditorScenePostImport
## Neutral structural readability; final surface identity belongs to material work.

func _post_import(scene: Node) -> Object:
	for child in scene.find_children("*", "MeshInstance3D", true, false):
		var mesh := child as MeshInstance3D
		for index in mesh.mesh.get_surface_count():
			var material := mesh.mesh.surface_get_material(index) as StandardMaterial3D
			material.albedo_color = Color(0.25, 0.27, 0.25)
			material.roughness = 1.0
	return scene
