@tool
extends EditorScenePostImport
## Apply shared map shading and bounded decoration distances at import, never at runtime.

func _post_import(scene: Node) -> Object:
	var asset_name := get_source_file().get_file().get_basename()
	for body in scene.find_children("*", "StaticBody3D", true, false):
		body.collision_layer = 1
		body.collision_mask = 1
		body.set_meta("surface_identity", "Rock" if asset_name.begins_with("Rock") or asset_name.begins_with("Boulder") else "Concrete")
	for node in scene.find_children("*", "MeshInstance3D", true, false):
		var instance := node as MeshInstance3D
		for index in instance.mesh.get_surface_count():
			var material := instance.mesh.surface_get_material(index) as StandardMaterial3D
			if material == null:
				continue
			if material.resource_name == "Concrete":
				instance.mesh.surface_set_material(index, load("res://assets/arena/materials/Concrete.tres"))
			elif material.resource_name == "Rock":
				# Replace the imported slot so unused embedded texture copies are not
				# retained underneath a per-instance override in every loaded asset.
				instance.mesh.surface_set_material(index, load("res://assets/environment/Rock.tres"))
			else:
				material.vertex_color_use_as_albedo = true
				if asset_name in ["FirMature", "PineOpen", "SpruceYoung"]:
					# Same treatment as the production oval forest baker.
					material.albedo_color *= Color(.35, .45, .3, 1)
					material.metallic_specular = 0.0
					material.roughness = 1.0
				if material.resource_name in ["Leaf", "DryGrass"]:
					material.cull_mode = BaseMaterial3D.CULL_DISABLED
					material.albedo_color *= Color(.72, .78, .48, 1)
					material.metallic_specular = 0.0
				if material.resource_name == "Bark":
					material.albedo_color *= Color(.35, .30, .25, 1)
		if asset_name.contains("Grass"):
			instance.visibility_range_end = 65.0
			instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		elif asset_name.begins_with("Scrub"):
			instance.visibility_range_end = 120.0
	return scene
