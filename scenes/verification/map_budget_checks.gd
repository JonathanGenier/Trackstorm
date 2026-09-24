extends SceneTree
## Read-only production-map inventory. Counts are budgets, not frame-time claims.

var meshes := {}
var materials := {}
var shapes := {}
var textures := {}
var report := {
	"nodes": 0, "mesh_instances": 0, "multimesh_batches": 0,
	"multimesh_instances": 0, "collision_instances": 0,
	"visible_base_triangles": 0, "unique_mesh_triangles": 0,
	"lod_surfaces": 0, "unique_concave_triangles": 0, "unique_convex_points": 0,
	"distance_limited_batches": 0,
}

func _initialize() -> void:
	var map := load("res://scenes/maps/oval_foundation.tscn").instantiate() as Node3D
	root.add_child(map)
	inspect(map)
	report["unique_meshes"] = meshes.size()
	report["unique_materials"] = materials.size()
	report["unique_shapes"] = shapes.size()
	report["textures"] = textures.values()
	assert(map.get_node("ItemSpawns").get_child_count() == 27)
	assert(map.get_node("PlayerSpawns").get_child_count() == 8)
	assert(report.multimesh_instances >= 2447 + 864)
	assert(report.lod_surfaces > 0)
	for texture in textures.values():
		assert(not str(texture.path).begins_with("res://assets/environment/models/"), "Unused embedded texture retained under a shared material")
	var output := "res://.godot/map-budget-checks"
	DirAccess.make_dir_recursive_absolute(output)
	var file := FileAccess.open(output + "/inventory.json", FileAccess.WRITE)
	file.store_string(JSON.stringify(report, "\t") + "\n")
	file.close()
	print("Map budget inventory passed: ", JSON.stringify(report))
	map.free()
	quit()

func inspect(node: Node) -> void:
	report.nodes += 1
	if node is MeshInstance3D:
		report.mesh_instances += 1
		inspect_mesh(node.mesh, 1 if node.is_visible_in_tree() else 0)
		for surface in node.mesh.get_surface_count():
			inspect_material(node.get_active_material(surface))
	elif node is MultiMeshInstance3D:
		report.multimesh_batches += 1
		var count: int = node.multimesh.instance_count
		report.multimesh_instances += count
		if node.visibility_range_end > 0:
			report.distance_limited_batches += 1
		inspect_mesh(node.multimesh.mesh, count)
	elif node is CollisionShape3D:
		report.collision_instances += 1
		var shape: Shape3D = node.shape
		if not shapes.has(shape.get_instance_id()):
			shapes[shape.get_instance_id()] = true
			if shape is ConcavePolygonShape3D:
				report.unique_concave_triangles += shape.get_faces().size() / 3
			elif shape is ConvexPolygonShape3D:
				report.unique_convex_points += shape.points.size()
	for child in node.get_children():
		inspect(child)

func inspect_mesh(mesh: Mesh, instances: int) -> void:
	var fresh := not meshes.has(mesh.get_instance_id())
	meshes[mesh.get_instance_id()] = true
	for surface in mesh.get_surface_count():
		var arrays := mesh.surface_get_arrays(surface)
		var triangles: int = arrays[Mesh.ARRAY_INDEX].size() / 3 if arrays[Mesh.ARRAY_INDEX] != null and arrays[Mesh.ARRAY_INDEX].size() > 0 else arrays[Mesh.ARRAY_VERTEX].size() / 3
		report.visible_base_triangles += triangles * instances
		if fresh:
			report.unique_mesh_triangles += triangles
			var info := RenderingServer.mesh_get_surface(mesh.get_rid(), surface)
			if info.has("lods") and not info.lods.is_empty():
				report.lod_surfaces += 1
		inspect_material(mesh.surface_get_material(surface))

func inspect_material(material: Material) -> void:
	if material == null or materials.has(material.get_instance_id()):
		return
	materials[material.get_instance_id()] = true
	for property in material.get_property_list():
		if property.type != TYPE_OBJECT:
			continue
		var value = material.get(property.name)
		if value is Texture2D:
			textures[value.get_instance_id()] = {"path": value.resource_path, "width": value.get_width(), "height": value.get_height()}
