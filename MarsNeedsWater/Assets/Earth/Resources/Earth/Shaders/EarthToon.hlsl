// The stylized look shared by every Earth shader: bright saturated colours, light in soft bands rather than a
// smooth falloff, coloured (never black) shade, and a little light on edges facing away from the eye.
// Tune the look here and every surface follows.
#ifndef EARTH_TOON_INCLUDED
#define EARTH_TOON_INCLUDED

// How strongly colours are pushed away from grey (1 = as painted).
#define TOON_SATURATION 1.38
// The shade: sky light plus this cool fill, so shadows read blue-violet instead of dark.
#define TOON_SHADE_FILL half3(0.045, 0.065, 0.16)

// More colourful and a touch brighter in the mid-tones; near-greys (roads, concrete) stay calm.
half3 ToonPop(half3 c, half amount)
{
    half l = dot(c, half3(0.2126, 0.7152, 0.0722));
    half3 d = c - l;
    c = max(l + d * amount, 0.0);
    // lift the mid-tones a little, keeping black and white where they are
    return c * (1.0 + 0.22 * saturate(1.0 - l * 1.4));
}

half3 ToonPop(half3 c) { return ToonPop(c, TOON_SATURATION); }

// Light in two soft bands. ndl is the raw dot of normal and sun (-1..1), shadow the sun's shadow (0..1).
half ToonBand(half ndl, half shadow)
{
    half band = smoothstep(-0.03, 0.14, ndl);
    band *= 0.8 + 0.2 * smoothstep(0.3, 0.8, ndl);
    return band * smoothstep(0.3, 0.62, shadow);
}

// The lit colour of a matte surface.
half3 ToonLight(half3 albedo, half ndl, half shadow, half3 sunColor, half3 sky, half ao)
{
    half lit = ToonBand(ndl, shadow);
    half3 shade = (sky * 0.95 + TOON_SHADE_FILL) * lerp(0.72, 1.0, ao);
    half3 light = sunColor * 1.02 + sky * 0.55;
    return albedo * lerp(shade, light, lit);
}

// A soft glow on edges turned away from the eye, on the sunny side: makes shapes read as chunky.
half3 ToonRim(float3 n, float3 v, float3 sunDir, half3 sky, half strength)
{
    half rim = pow(1.0 - saturate(dot(n, v)), 3.5) * saturate(dot(n, sunDir) * 0.5 + 0.6);
    return (sky + 0.15) * smoothstep(0.25, 0.6, rim) * strength;
}

// A highlight with a hard edge instead of a smooth blob.
half ToonSpec(half ndh, half smoothness)
{
    half s = pow(saturate(ndh), exp2(10.0 * smoothness + 1.0));
    return smoothstep(0.45, 0.6, s) * smoothness * 0.3;
}

#endif
