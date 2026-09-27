@tool
extends EditorScenePostImport
## Preserve Blender geometry and pivots; bind shared game-ready weathered materials.
func _post_import(scene):
	# glTF adds a file root above Blender's semantic root. Flatten only that identity node.
	var car = scene.get_node("Car")
	for child in car.get_children():
		_owners(child, null)
		child.reparent(scene, false)
		_owners(child, scene)
	car.free()
	var paint = load("res://assets/vehicles/materials/CarPaint.tres")
	var armor = load("res://assets/vehicles/materials/CarSteel.tres")
	var tire = load("res://assets/vehicles/materials/Tire.tres")
	_bind(scene, paint, armor, tire)
	return scene

func _owners(node, owner_node):
	node.owner = owner_node
	for child in node.get_children():
		_owners(child, owner_node)

func _bind(node, paint, armor, tire):
	if node is MeshInstance3D:
		for i in node.mesh.get_surface_count():
			var mat = node.mesh.surface_get_material(i)
			if mat == null:
				continue
			match mat.resource_name:
				"Car_OxideRed": node.mesh.surface_set_material(i, paint)
				"Car_WornSteel": node.mesh.surface_set_material(i, armor)
				"Car_Rubber": node.mesh.surface_set_material(i, tire)
	for child in node.get_children():
		_bind(child, paint, armor, tire)
