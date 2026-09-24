# Run after Blender export, before the Godot editor import. Preserve stable UIDs on rerun.
$ErrorActionPreference = 'Stop'
$library = Get-Content "$PSScriptRoot/library.json" -Raw | ConvertFrom-Json
foreach ($asset in $library.assets) {
    $resource = "res://assets/environment/$($asset.model)"
    $path = "$PSScriptRoot/$($asset.model).import"
    $uidLine = ''
    if (Test-Path $path) {
        $uidLine = @(Get-Content $path | Where-Object { $_ -match '^uid=' }) -join "`n"
    }
    @"
[remap]
importer="scene"
importer_version=1
type="PackedScene"
$uidLine

[deps]
source_file="$resource"

[params]
nodes/root_scale=1.0
nodes/use_name_suffixes=true
meshes/generate_lods=true
meshes/create_shadow_meshes=true
meshes/force_disable_compression=true
animation/import=false
import_script/path="res://assets/environment/ImportAsset.gd"
_subresources={}
gltf/naming_version=2
gltf/embedded_image_handling=1
"@ | Set-Content $path -Encoding utf8
}
