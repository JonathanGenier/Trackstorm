extends SceneTree
## Offline bake: imported Blender surfaces become fixed, inspectable collision resources.

func _initialize() -> void:
	var map := Node3D.new()
	map.name = "OvalFoundation"
	var geometry := load("res://assets/maps/oval/oval_foundation.glb").instantiate() as Node3D
	geometry.name = "Geometry"
	map.add_child(geometry)
	geometry.owner = map
	var collision := Node3D.new()
	collision.name = "Collision"
	map.add_child(collision)
	collision.owner = map
	for surface in ["Track", "Infield"]:
		var visual := geometry.find_child(surface, true, false) as MeshInstance3D
		assert(visual != null and visual.transform.is_equal_approx(Transform3D.IDENTITY))
		var shape := visual.mesh.create_trimesh_shape()
		assert(ResourceSaver.save(shape, "res://assets/maps/oval/" + surface + "Collision.tres") == OK)
		var body := StaticBody3D.new()
		body.name = surface
		collision.add_child(body)
		body.owner = map
		var collider := CollisionShape3D.new()
		collider.name = "Shape"
		collider.shape = load("res://assets/maps/oval/" + surface + "Collision.tres")
		body.add_child(collider)
		collider.owner = map
	var markers := Node3D.new()
	markers.name = "PlayerSpawns"
	map.add_child(markers)
	markers.owner = map
	var measurements: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://assets/maps/oval/measurements.json"))
	for slot: Dictionary in measurements.grid:
		var marker := Marker3D.new()
		marker.name = slot.id
		marker.position = Vector3(slot.position[0], slot.position[1], slot.position[2])
		marker.rotation.y = slot.yaw
		marker.set_meta("slot_length_m", slot.length_m)
		marker.set_meta("slot_width_m", slot.width_m)
		markers.add_child(marker)
		marker.owner = map
	var content := Node3D.new()
	content.name = "MapContent"
	map.add_child(content)
	content.owner = map
	var packed := PackedScene.new()
	assert(packed.pack(map) == OK)
	assert(ResourceSaver.save(packed, "res://scenes/maps/oval_foundation.tscn") == OK)
	map.free()
	print("Oval scene baked successfully.")
	quit()
