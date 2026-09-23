@tool
extends EditorScenePostImport
## Concrete visual and identity assignment on the unchanged Blender structural set.

func _post_import(scene: Node) -> Object:
	# The newly reachable deck is a driving surface. Side/underside contacts are
	# still obstacles through the existing upward-normal classification cutoff.
	for body in scene.find_children("*", "StaticBody3D", true, false):
		body.set_meta("surface_identity", load("res://assets/arena/materials/Concrete.tres").get_meta("surface_identity"))
		var part_name := str(body.get_parent().name)
		if str(body.name).begins_with("DeckRoad") or part_name.contains("EdgeBeam"):
			body.add_to_group("landing_terrain", true)
	for child in scene.find_children("*", "MeshInstance3D", true, false):
		var mesh := child as MeshInstance3D
		for index in mesh.mesh.get_surface_count():
			mesh.set_surface_override_material(index, load("res://assets/arena/materials/Concrete.tres"))
	return scene
