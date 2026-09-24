shader_type sky;
render_mode disable_fog;
uniform vec3 zenith : source_color;
uniform vec3 horizon : source_color;
uniform vec3 light_color : source_color;
uniform float cloud_amount = 0.0;
uniform bool night = false;
uniform bool sunset = false;
float hash(vec2 p) { return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }
float noise(vec2 p) {
    vec2 i=floor(p), f=fract(p); f=f*f*(3.0-2.0*f);
    return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1,1)),f.x),f.y);
}
void sky() {
    float elevation = max(EYEDIR.y,0.0);
    vec3 base = mix(horizon,zenith,smoothstep(0.0,sunset ? .12 : .65,elevation));
    if (sunset) { base = mix(vec3(.9,.36,.13),base,smoothstep(0.0,.045,elevation)); }
    vec2 p = EYEDIR.xz / max(.15,elevation+.15) * 2.5;
    float cloud = noise(p)*.55+noise(p*2.1)*.3+noise(p*4.3)*.15;
    float veil = smoothstep(.45,.72,cloud)*cloud_amount*smoothstep(0.0,.13,elevation);
    base = mix(base, mix(zenith*.42,horizon*.75,cloud),veil);
    if (night) {
        vec2 stars = SKY_COORDS*vec2(1200.0,600.0);
        float spark = step(.997,hash(floor(stars))) * (1.0-smoothstep(.04,.3,length(fract(stars)-.5)));
        base += vec3(.6,.7,1.0)*spark*smoothstep(.08,.4,elevation);
    }
    if (LIGHT0_ENABLED) {
        float disc = smoothstep(night ? .99988 : .99965, night ? .99996 : .99986, dot(EYEDIR,LIGHT0_DIRECTION));
        float glow = pow(max(dot(EYEDIR,LIGHT0_DIRECTION),0.0), night ? 600.0 : 90.0);
        base += light_color*(disc*(night ? 1.3 : 3.0)+glow*.13);
    }
    COLOR = base;
}
