@tool
extends EditorScenePostImport
## Flat diagram colors make reservations legible without introducing final materials.

func _post_import(scene: Node) -> Object:
	for child in scene.find_children("*", "MeshInstance3D", true, false):
		var visual := child as MeshInstance3D
		visual.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		for index in visual.mesh.get_surface_count():
			var material := visual.mesh.surface_get_material(index) as StandardMaterial3D
			material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	return scene
