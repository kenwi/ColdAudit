#version 330

// Baked-lighting fragment shader. The atlas holds incoming irradiance/pi with the
// surface's albedo excluded (tools/bake_lightmaps.py), so the whole lighting model is
// one multiply: outgoing radiance = albedo * (E/pi). Real-time lights, shadows and
// light volumes are all absent by construction.
//
// The PNG is 8-bit sRGB, which spends its bits where an office's irradiance actually
// sits, and is scaled down by encode_scale before encoding so the lit surfaces are not
// clipped. Both steps have to be undone here; encode_scale lives in the sidecar JSON.

in vec2 fragTexCoord;
in vec2 fragLightMapCoord;

out vec4 finalColor;

uniform sampler2D albedoMap;
uniform sampler2D lightMap;
uniform vec4 albedoColor;

// Guard for a material whose albedo map is genuinely empty: sampling unit 0 would then
// pick up whatever the previous material left bound there.
uniform int useTexAlbedo;
uniform int useLightMap;

// 1/encode_scale, from the atlas sidecar.
uniform float lightMapScale;

// Scene units -> display. A fixed exposure is enough because the bake is static: the
// level's realtime curve (pow(c, c+1) in pbr.fs) expands highlights rather than
// compressing them, and a baked office has most visible surfaces several times brighter
// than the dim realtime levels it was tuned on.
uniform float exposure;

vec3 SrgbToLinear(vec3 c)
{
    vec3 low = c/12.92;
    vec3 high = pow((c + 0.055)/1.055, vec3(2.4));
    return mix(low, high, step(vec3(0.04045), c));
}

void main()
{
    // glTF baseColorFactor is linear; the baked albedo textures are sRGB PNGs.
    vec3 albedo = albedoColor.rgb;
    if (useTexAlbedo == 1)
    {
        albedo *= SrgbToLinear(texture(albedoMap, fragTexCoord).rgb);
    }

    vec3 light = vec3(1.0);
    if (useLightMap == 1)
    {
        light = SrgbToLinear(texture(lightMap, fragLightMapCoord).rgb)*lightMapScale;
    }

    vec3 color = albedo*light*exposure;

    // Reinhard. Unlike pbr.fs's pow(c, c+1), this is monotonic and cannot clip, which
    // matters when the baked level legitimately runs several stops brighter than the
    // realtime path it replaces.
    color = color/(1.0 + color);
    color = pow(color, vec3(1.0/2.2));

    finalColor = vec4(color, 1.0);
}
