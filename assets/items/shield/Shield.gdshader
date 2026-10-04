shader_type spatial;
render_mode specular_schlick_ggx;
uniform vec4 base_color : source_color = vec4(.24, .27, .28, 1.0);
uniform float metal = .7;
uniform float wear : hint_range(0.0, 1.0) = 0.0;
uniform float flash : hint_range(0.0, 1.0) = 0.0;
uniform float lamp = 0.0;
varying vec3 local_position;
float noise_hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float metal_noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(noise_hash(i), noise_hash(i + vec2(1, 0)), f.x),
        mix(noise_hash(i + vec2(0, 1)), noise_hash(i + vec2(1, 1)), f.x), f.y);
}
void vertex() { local_position = VERTEX; }
void fragment() {
    vec3 p = local_position;
    float mottling = metal_noise(p.xy * 7.0);
    float dent = min(length(p.xy - vec2(.43, .10)), length(p.xy - vec2(-.64, -.28)));
    dent = min(dent, length(p.xy - vec2(1.16, .27)));
    float scorch = (1.0 - smoothstep(.06, .20 + mottling * .09, dent)) * smoothstep(.12, .7, wear);
    float jagged = abs(p.x - .43 - p.y * .66 + (metal_noise(p.xy * 24.0) - .5) * .085);
    float crack = (1.0 - smoothstep(.006, .018, jagged)) * (1.0 - smoothstep(.25, .65, length(p.xy - vec2(.43, .1)))) * smoothstep(.45, .9, wear);
    float chips = smoothstep(.84, .94, metal_noise(p.xy * 28.0));
    float scratches = smoothstep(.94, .99, metal_noise(vec2(p.x * 62.0, p.y * 2.0)));
    vec3 weathered = base_color.rgb * (.72 + mottling * .24) * (1.0 - wear * .28);
    weathered = mix(weathered, vec3(.11, .085, .06), chips * .65);
    weathered = mix(weathered, vec3(.3, .31, .30), scratches * .6);
    weathered = mix(weathered, vec3(.08, .043, .022), scorch * .8);
    ALBEDO = mix(weathered, vec3(.008, .009, .01), max(crack, scorch * (1.0 - smoothstep(.035, .07, dent))));
    METALLIC = metal * .65;
    ROUGHNESS = .67 + wear * .25;
    EMISSION = vec3(1.0, .57, .19) * flash * 1.8 + vec3(1.0, .22, .015) * lamp * (1.4 - wear * 1.2);
}
