extends SceneTree
## Offline continuous solid perimeter. Shared vertices avoid buried convex end caps.

static func create_shape(measurements: Dictionary) -> ConcavePolygonShape3D:
	var sections: Array = measurements.sections_godot
	var faces := PackedVector3Array()
	for i in sections.size():
		var points: Array[Vector3] = []
		for index in [i, (i + 1) % sections.size()]:
			var source: Array = sections[index][1]
			var inside: Array = sections[index][0]
			var rim := Vector3(source[0], source[1], source[2])
			var outward := Vector3(source[0] - inside[0], 0, source[2] - inside[2]).normalized()
			var p := rim + outward * .05
			points.append(p - Vector3.UP * 2)
			points.append(Vector3(p.x, 45, p.z))
			points.append(p + outward * 4 - Vector3.UP * 2)
			points.append(Vector3(p.x, 45, p.z) + outward * 4)
		for face in [[0,5,4],[0,1,5],[2,6,7],[2,7,3],[1,3,7],[1,7,5],[0,4,6],[0,6,2]]:
			for index: int in face:
				faces.append(points[index])
	var shape := ConcavePolygonShape3D.new()
	shape.backface_collision = true
	shape.set_faces(faces)
	return shape

func _initialize() -> void:
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://assets/maps/oval/measurements.json"))
	assert(ResourceSaver.save(create_shape(data), "res://assets/maps/oval/ContainmentCollision.tres") == OK)
	print("Continuous oval containment baked.")
	quit()
