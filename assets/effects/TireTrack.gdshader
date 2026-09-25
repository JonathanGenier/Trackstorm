shader_type spatial;
render_mode cull_disabled, depth_draw_never;
uniform float elapsed;
uniform float visibility_distance = 100.0;
varying vec4 instance_data;
void vertex() {
    instance_data = INSTANCE_CUSTOM;
    if (elapsed - instance_data.x >= instance_data.y) { VERTEX = vec3(0.0); }
}
void fragment() {
    float age = elapsed - instance_data.x;
    float fade = 1.0 - smoothstep(instance_data.y - instance_data.z, instance_data.y, age);
    float soft = step(0.0, instance_data.w);
    float edge = smoothstep(0.0, 0.12, UV.x) * smoothstep(0.0, 0.12, 1.0-UV.x);
    float ends = smoothstep(0.0, 0.08, UV.y) * smoothstep(0.0, 0.08, 1.0-UV.y);
    float tread = mix(0.65, 1.0, step(0.3, fract(UV.y * 12.0 + abs(UV.x-0.5)*3.0)));
    tread = mix(1.0,tread,soft);
    float grit = fract(sin(dot(floor(UV*vec2(40.0,80.0)),vec2(127.1,311.7)))*43758.5453);
    edge *= mix(1.0,.65+grit*.35,soft);
    ALBEDO = COLOR.rgb;
    ROUGHNESS = abs(instance_data.w);
    ALPHA = COLOR.a * edge * ends * tread * fade * (1.0-smoothstep(visibility_distance*.65,visibility_distance,length(VERTEX))) * step(0.0, age);
}
