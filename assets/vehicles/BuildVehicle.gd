extends SceneTree
## Bind existing Godot materials to Blender geometry; never authors meshes.
func _initialize():
	var model = load("res://assets/vehicles/WastelandVehicle.glb").instantiate()
	var lines = ['[gd_scene load_steps=6 format=3]', '', '[ext_resource type="PackedScene" path="res://assets/vehicles/WastelandVehicle.glb" id="1"]']
	for kind in ["Body", "Tire", "Armor", "Trim"]:
		lines.append('[ext_resource type="Material" path="res://assets/vehicles/materials/%s.tres" id="%s"]' % [kind, kind])
	lines.append('\n[node name="WastelandVehicle" instance=ExtResource("1")]')
	for mesh in model.get_children():
		if not mesh is MeshInstance3D:
			continue
		lines.append('\n[node name="%s" parent="." index="%d"]' % [mesh.name, mesh.get_index()])
		for i in mesh.mesh.get_surface_count():
			var key = str(mesh.mesh.surface_get_material(i).resource_name)
			var kind = "Trim"
			for candidate in ["Body", "Tire", "Armor"]:
				if key.begins_with(candidate):
					kind = candidate
			lines.append('surface_material_override/%d = ExtResource("%s")' % [i, kind])
	FileAccess.open("res://assets/vehicles/WastelandVehicle.tscn", FileAccess.WRITE).store_string('\n'.join(lines) + '\n')
	model.free()
	quit()
