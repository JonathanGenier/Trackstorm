shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

uniform float age = 0.0;
uniform float energy = 0.0;
uniform float speed = 0.0;
uniform float phase = 0.0;
uniform float layer = 0.0;

float hash(vec3 p) {
    p = fract(p * 0.3183099 + vec3(0.13, 0.27, 0.41));
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}
float noise(vec3 p) {
    vec3 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash(i), hash(i+vec3(1,0,0)), f.x), mix(hash(i+vec3(0,1,0)), hash(i+vec3(1,1,0)), f.x), f.y),
        mix(mix(hash(i+vec3(0,0,1)), hash(i+vec3(1,0,1)), f.x), mix(hash(i+vec3(0,1,1)), hash(i+vec3(1,1,1)), f.x), f.y), f.z);
}
void vertex() {
    float t = 1.0 - UV.y; // glTF import uses top-origin V.
    float clock = TIME * 16.0 + phase;
    float pulse = sin(clock - t * 24.0) * sin(clock * .71 - t * 15.0);
    float ignition = exp(-age * 12.0);
    VERTEX.xy *= (1.0 + ignition * .38) * (1.0 + pulse * .22 * t);
    VERTEX.z *= (.85 + speed * .36 + ignition * .32) * (1.0 + pulse * .11 * t);
    VERTEX.x += sin(clock * .67 - t * 14.0) * t * t * .24;
    VERTEX.y += sin(clock * .93 - t * 19.0) * t * t * .18;
}
void fragment() {
    float t = 1.0 - UV.y; // glTF import uses top-origin V.
    float core = layer == 1.0 ? 1.0 : 0.0;
    float wisp = layer > 1.5 ? 1.0 : 0.0;
    float clock = TIME * 5.8 + phase;
    vec3 p = vec3(cos(UV.x * 6.283), sin(UV.x * 6.283), t * 6.0 - clock);
    float n = noise(p * 2.4) * .65 + noise(p * 5.1 + clock * .3) * .35;
    float torn = smoothstep(.22 + t * .31 + wisp * .14, .61 + t * .20 + wisp * .13, n);
    float edge = .2 + .8 * pow(abs(dot(normalize(NORMAL), normalize(VIEW))), .6);
    float root = smoothstep(0.0, .018, t);
    float tip = 1.0 - smoothstep(.62, 1.0, t);
    float diamonds = pow(.5 + .5 * cos(t * 29.0 - TIME * 4.0), 6.0) * (1.0 - t);
    vec3 hot = mix(vec3(.12,.26,.80), vec3(.48,.72,1.0), core);
    vec3 tail = mix(vec3(1.0,.23,.018), vec3(.12,.4,.9), core);
    ALBEDO = mix(hot, tail, smoothstep(.03, .5, t)) * (1.0 + diamonds * .5);
    ALPHA = energy * root * tip * edge * mix(torn, .6 + n * .4, core)
        * (1.0 - wisp * .28) * (.8 + exp(-age * 15.0) * .8);
}