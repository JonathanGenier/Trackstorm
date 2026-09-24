shader_type spatial;
render_mode cull_disabled;
uniform sampler2D surface_field : filter_linear, repeat_disable;
varying vec3 point;
void vertex() { point = VERTEX; }
void fragment() {
    float bank = texture(surface_field, UV).a;
    if (bank < 0.5) { discard; }
    vec2 p = point.xz;
    float ripple = sin(p.x*3.1 + p.y*1.7 + TIME*1.3) * sin(p.y*4.2-p.x*.8-TIME*.9);
    float edge = (1.0-smoothstep(.5,.68,bank))*(.55+.45*sin(p.x*8.0+p.y*5.0+TIME));
    ALBEDO = mix(vec3(.065,.18,.19), vec3(.22,.29,.25), edge*.65) + ripple*.009;
    ROUGHNESS = mix(.21,.5,edge);
    NORMAL_MAP = normalize(vec3(ripple*.12,cos(p.x*3.1+p.y*1.7+TIME*1.3)*.08,1.0))*.5+.5;
    NORMAL_MAP_DEPTH = .4;
    METALLIC = .08;
}
