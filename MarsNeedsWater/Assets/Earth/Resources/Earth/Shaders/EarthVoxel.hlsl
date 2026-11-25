// Shared by the voxel shaders (the voxel-style experiment): one random number per block, so every block gets its
// own shade and no pattern ever repeats.
#ifndef EARTH_VOXEL_INCLUDED
#define EARTH_VOXEL_INCLUDED

float VHash(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

half3 VUnpack(float c)
{
    float r = floor(c / 65536.0);
    float g = floor((c - r * 65536.0) / 256.0);
    float b = c - r * 65536.0 - g * 256.0;
    half3 s = half3(r, g, b) / 255.0;
    return s * (s * (s * 0.305306011 + 0.682171111) + 0.012522878);     // sRGB to linear, near enough
}

// A block's own shade: block = whole-number place of the block, inside = 0..1 across its face, seed = anything.
// amount: how far apart blocks may be in brightness. far: 0 near, 1 where blocks are smaller than a pixel.
half3 VShade(half3 col, float3 block, float2 inside, float seed, half amount, half far)
{
    float h = VHash(block + seed * 0.37);
    float h2 = VHash(block.zxy * 1.7 + seed + 11.0);
    half k = 1.0 + (h - 0.5) * 2.0 * amount * (1.0 - 0.6 * far);
    col *= k;
    // a little warmer or cooler, block by block
    col *= 1.0 + (h2 - 0.5) * amount * half3(0.5, 0.1, -0.5) * (1.0 - far);
    // and a grain of 4 x 4 specks on each face
    float2 px = floor(saturate(inside) * 4.0);
    float h3 = VHash(float3(block.xy * 4.0 + px, block.z + seed + 5.0));
    col *= 1.0 + (h3 - 0.5) * 0.09 * saturate(1.0 - far * 3.0);
    return col;
}
#endif
