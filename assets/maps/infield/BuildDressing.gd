extends SceneTree
## Offline placement only. All production meshes/colliders remain shared Blender imports.

const OUTPUT := "res://scenes/maps/infield_dressing.tscn"
const LIBRARY := "res://assets/environment/models/"
var layout: Dictionary
var map: Node3D
var dressing: Node3D
var rng := RandomNumberGenerator.new()
var templates: Dictionary = {}
var batches: Dictionary = {}
var occupied: Array[Vector3] = []
var counts: Dictionary = {}
var inner_boundary := PackedVector2Array()

func _initialize() -> void:
	call_deferred("bake")

func bake() -> void:
	assert(DisplayServer.get_name() != "headless", "MultiMesh bake requires a rendering backend.")
	layout = JSON.parse_string(FileAccess.get_file_as_string("res://assets/maps/infield/layout.json"))
	map = load("res://scenes/maps/oval_foundation.tscn").instantiate()
	if map.has_node("EnvironmentDressing"):
		map.get_node("EnvironmentDressing").free()
	root.add_child(map)
	await physics_frame
	await physics_frame
	dressing = Node3D.new()
	dressing.name = "EnvironmentDressing"
	rng.seed = 81
	var measurements: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://assets/maps/oval/measurements.json"))
	var sections: Array = measurements.sections_godot
	for section: Array in sections:
		inner_boundary.append(Vector2(section[0][0],section[0][2]))
	var landmarks := Node3D.new()
	landmarks.name = "RockLandmarks"
	dressing.add_child(landmarks)
	landmarks.owner = dressing
	for spec in [[-43,64,2.5,.35],[43,-69,2.3,-.4],[143,-12,2.0,1.2],[-140,45,1.8,-.8]]:
		place(landmarks,"BoulderTall",Vector2(spec[0],spec[1]),spec[2],spec[3],false)
	# Distinct island compositions: broken ridges north, low rubble by the basins,
	# compact outcrops south. All candidates are clipped against preserved routes.
	for zone in [
		["NorthWestRidge", Vector2(-55,-64), Vector2(32,12), 1.7],
		["NorthEastRidge", Vector2(48,-64), Vector2(29,12), 1.4],
		["WestBasin", Vector2(-60,-28), Vector2(36,18), 1.1],
		["EastBasin", Vector2(78,-30), Vector2(37,18), 1.3],
		["WestTurn", Vector2(-148,0), Vector2(20,52), 1.0],
		["EastTurn", Vector2(148,0), Vector2(20,52), 1.2],
		["SouthWestIsland", Vector2(-47,58), Vector2(28,18), 1.6],
		["SouthEastIsland", Vector2(43,57), Vector2(25,19), 1.2],
		["WestWetShoulder", Vector2(-88,30), Vector2(30,16), .9],
		["EastWetShoulder", Vector2(90,31), Vector2(30,16), .9]]:
		var group := Node3D.new()
		group.name = zone[0]
		dressing.add_child(group)
		group.owner = dressing
		for i in 70:
			var p: Vector2 = zone[1] + Vector2(rng.randf_range(-1,1),rng.randf_range(-1,1)) * zone[2]
			var asset: String = ["BoulderTall","RockLedge","RockCluster","BoulderLow","RockSlab"][i % 5]
			var size: float = rng.randf_range(.65,1.15) * float(zone[3])
			if place(group, asset, p, size, rng.randf_range(-PI,PI), false):
				# Smaller loose stones form natural debris aprons, never dynamic clutter.
				for j in 3:
					place(group,"BoulderLow",p+Vector2(rng.randf_range(-5,5),rng.randf_range(-5,5)),rng.randf_range(.25,.5),rng.randf_range(-PI,PI),false)
	# Low ground cover is spatially batched, with broken patches rather than a grid.
	var cover := Node3D.new()
	cover.name = "GroundCover"
	dressing.add_child(cover)
	cover.owner = dressing
	for i in 14000:
		var p := Vector2(rng.randf_range(-170,170),rng.randf_range(-77,77))
		var patch := sin(p.x*.13+p.y*.08) + sin(p.y*.27-p.x*.04)
		if patch < -.4:
			continue
		var asset: String = ["GrassClump","DryGrassClump","ScrubLow","GrassClump","ScrubTall"][i%5]
		place(cover,asset,p,rng.randf_range(.7,1.25),rng.randf_range(-PI,PI),true)
	# Perimeter fixtures sit beyond the existing wall, never inside usable asphalt.
	var infrastructure := Node3D.new()
	infrastructure.name = "PerimeterFixtures"
	dressing.add_child(infrastructure)
	infrastructure.owner = dressing
	for i in range(0,sections.size(),57):
		var a: Array = sections[i][0]
		var b: Array = sections[i][1]
		var edge := Vector3(b[0],b[1],b[2])
		var outward := Vector3(b[0]-a[0],0,b[2]-a[2]).normalized()
		var pole := instance_asset(infrastructure,"LightPole9m",Transform3D(Basis(Vector3.UP,atan2(outward.x,outward.z)),edge+outward*2.2-Vector3.UP*.2))
		pole.set_meta("placement_role","exterior fixture")
		for j in 3:
			var tangent := Vector3(outward.z,0,-outward.x)
			instance_asset(infrastructure,"Guardrail4m",Transform3D(Basis(Vector3.UP,atan2(-tangent.z,tangent.x)),edge+outward*1.6+tangent*(j-1)*4))
	for key: String in batches:
		var data: Dictionary = batches[key]
		var template: Node3D = template_for(data.asset)
		var visual := template.find_children("*","MeshInstance3D",true,false)[0] as MeshInstance3D
		var multi := MultiMesh.new()
		multi.transform_format = MultiMesh.TRANSFORM_3D
		multi.mesh = visual.mesh
		multi.instance_count = data.transforms.size()
		for i in data.transforms.size():
			var local_pose: Transform3D = data.transforms[i]
			local_pose.origin -= data.center
			multi.set_instance_transform(i,local_pose)
		var batch := MultiMeshInstance3D.new()
		batch.name = key
		batch.position = data.center
		batch.multimesh = multi
		batch.cast_shadow = visual.cast_shadow
		batch.visibility_range_end = visual.visibility_range_end
		cover.add_child(batch)
		batch.owner = dressing
	dressing.set_meta("asset_counts",counts)
	dressing.set_meta("placement_seed",81)
	var packed := PackedScene.new()
	assert(packed.pack(dressing) == OK)
	assert(ResourceSaver.save(packed,OUTPUT) == OK)
	print("Dressing baked: ",counts,"; spatial foliage batches: ",batches.size())
	for template: Node3D in templates.values():
		template.free()
	dressing.free()
	map.free()
	quit()

func template_for(asset: String) -> Node3D:
	if not templates.has(asset):
		templates[asset] = load(LIBRARY+asset+".glb").instantiate()
	return templates[asset]

func instance_asset(parent: Node3D, asset: String, pose: Transform3D) -> Node3D:
	var item := load(LIBRARY+asset+".glb").instantiate() as Node3D
	item.name = asset+"_%03d" % int(counts.get(asset,0))
	item.transform = pose
	parent.add_child(item)
	item.owner = dressing
	counts[asset] = int(counts.get(asset,0))+1
	return item

func place(parent: Node3D, asset: String, p: Vector2, size: float, yaw: float, batched: bool) -> bool:
	var visual := template_for(asset).find_children("*","MeshInstance3D",true,false)[0] as MeshInstance3D
	var bounds := visual.get_aabb()
	var radius := Vector2(maxf(abs(bounds.position.x),abs(bounds.end.x)),maxf(abs(bounds.position.z),abs(bounds.end.z))).length()*size
	# Protect whole visual bounds, not just the trunk or collision hull.
	var margin := 1.0 if batched else 4.0
	if not clear_reservations(p,radius,margin):
		return false
	for i in inner_boundary.size():
		if p.distance_to(Geometry2D.get_closest_point_to_segment(p,inner_boundary[i],inner_boundary[(i+1)%inner_boundary.size()])) < radius+4:
			return false # Preserve the curved asphalt edge as well as the straights.
	if not batched:
		for other in occupied:
			if p.distance_to(Vector2(other.x,other.y)) < radius+other.z+1.0:
				return false
	var ray := PhysicsRayQueryParameters3D.create(Vector3(p.x,20,p.y),Vector3(p.x,-8,p.y),1)
	var hit := map.get_world_3d().direct_space_state.intersect_ray(ray)
	if hit.is_empty() or not map.get_node("InfieldTerrain").is_ancestor_of(hit.collider):
		return false
	if hit.position.y < -.7 or hit.normal.y < .93:
		return false
	var pose := Transform3D(Basis(Vector3.UP,yaw).scaled(Vector3.ONE*size),hit.position-Vector3.UP*(.06 if batched else .18*size))
	if batched:
		var cell := Vector2(floor(p.x/32),floor(p.y/32))
		var key := "%s_%d_%d" % [asset,int(cell.x),int(cell.y)]
		if not batches.has(key):
			batches[key] = {"asset":asset,"transforms":[],"center":Vector3((cell.x+.5)*32,0,(cell.y+.5)*32)}
		batches[key].transforms.append(pose)
		counts[asset] = int(counts.get(asset,0))+1
	else:
		var item := instance_asset(parent,asset,pose)
		item.set_meta("route_clearance_radius_m",radius)
		occupied.append(Vector3(p.x,p.y,radius))
	return true

func clear_reservations(p: Vector2, radius: float, shoulder: float) -> bool:
	if abs(p.x) < 86+radius and abs(p.y) < 30+radius:
		return false # Entire tabletop, side climbs, collars and tunnel approaches.
	if abs(p.y)+radius > 78:
		return false # Inner-straight transition and grid access.
	for basin in [Vector2(-57,-29),Vector2(77,-35),Vector2(-85,28),Vector2(85,28)]:
		if p.distance_to(basin) < 12+radius:
			return false
	for route: Dictionary in layout.routes:
		for i in route.points.size()-1:
			var a := Vector2(route.points[i][0],route.points[i][1])
			var b := Vector2(route.points[i+1][0],route.points[i+1][1])
			if p.distance_to(Geometry2D.get_closest_point_to_segment(p,a,b)) < float(route.width_m)*.5+shoulder+radius:
				return false
	for jump: Dictionary in layout.jumps:
		var a := Vector2(jump.start[0],jump.start[1])
		var b := a+Vector2(jump.direction[0],jump.direction[1])*100
		if p.distance_to(Geometry2D.get_closest_point_to_segment(p,a,b)) < 10+radius:
			return false
	return true
