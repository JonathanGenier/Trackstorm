shader_type spatial;
render_mode cull_disabled, depth_draw_never;
uniform float elapsed;
varying vec4 instance_data;
void vertex() { instance_data = INSTANCE_CUSTOM; }
void fragment() {
    float age = elapsed - instance_data.x;
    float fade = 1.0 - smoothstep(instance_data.y * 0.7, instance_data.y, age);
    float edge = smoothstep(0.0, 0.12, UV.x) * smoothstep(0.0, 0.12, 1.0-UV.x);
    float ends = smoothstep(0.0, 0.08, UV.y) * smoothstep(0.0, 0.08, 1.0-UV.y);
    float tread = mix(0.65, 1.0, step(0.3, fract(UV.y * 12.0 + abs(UV.x-0.5)*3.0)));
    tread = mix(1.0,tread,instance_data.w);
    float grit = fract(sin(dot(floor(UV*vec2(40.0,80.0)),vec2(127.1,311.7)))*43758.5453);
    edge *= mix(1.0,.65+grit*.35,instance_data.w);
    ALBEDO = COLOR.rgb;
    ROUGHNESS = instance_data.z;
    ALPHA = COLOR.a * edge * ends * tread * fade * (1.0-smoothstep(65.0,100.0,length(VERTEX))) * step(0.0, age);
}
