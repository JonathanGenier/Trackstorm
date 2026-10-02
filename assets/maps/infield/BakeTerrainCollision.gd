extends SceneTree
## Offline resource packing; no gameplay-time collision generation.

func _initialize() -> void:
	var bytes := FileAccess.get_file_as_bytes("res://.godot/terrain-collision-bake/faces.bin")
	assert(not bytes.is_empty() and bytes.size() % 36 == 0)
	var faces := PackedVector3Array()
	faces.resize(bytes.size() / 12)
	for index in faces.size():
		faces[index] = Vector3(bytes.decode_float(index * 12), bytes.decode_float(index * 12 + 4), bytes.decode_float(index * 12 + 8))
	var shape := ConcavePolygonShape3D.new()
	shape.set_faces(faces)
	var path := "res://assets/maps/infield/TerrainCollision.res"
	assert(ResourceSaver.save(shape, path, ResourceSaver.FLAG_COMPRESS) == OK)
	var report: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://.godot/terrain-collision-bake/audit.json"))
	report["collision_resource"] = "TerrainCollision.res"
	report["collision_sha256"] = FileAccess.get_sha256(path)
	var file := FileAccess.open("res://assets/maps/infield/collision-audit.json", FileAccess.WRITE)
	file.store_string(JSON.stringify(report, "  ") + "\n")
	file.close()
	print("Terrain collision baked: ", faces.size() / 3, " triangles")
	quit()
