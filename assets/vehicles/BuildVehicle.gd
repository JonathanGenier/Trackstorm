extends SceneTree
## Rebuild the thin resource wrapper; geometry and materials are imported from Blender.
func _initialize():
	var text = '[gd_scene load_steps=2 format=3]\n[ext_resource type="PackedScene" path="res://assets/vehicles/TrackstormCar.glb" id="1"]\n[node name="WastelandVehicle" instance=ExtResource("1")]\n'
	FileAccess.open("res://assets/vehicles/WastelandVehicle.tscn", FileAccess.WRITE).store_string(text)
	quit()
