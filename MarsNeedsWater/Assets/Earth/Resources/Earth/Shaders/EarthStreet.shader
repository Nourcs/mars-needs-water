// Roads, streets, paths, pavements, railways and their painted lines (meshes made by Core/RoadNet.cs with
// Core/RoadPaint.cs). Every piece is a flat shape lying exactly on the ground; the mesh lists them bottom layer
// first and nothing here writes depth, so later shapes simply paint over earlier ones. The outline of each shape
// is worked out per pixel from numbers on its vertices, which keeps edges crisp from any distance:
//   uv0  x, y: where the pixel is inside its shape, metres from the shape's middle
//   uv1  x, y: half the shape's size; z: how round its corners are (m; below zero: all that lies outside a circle
//        of that radius); w: how soft its edge is (m, 0 = crisp)
//   uv2  x: colour (r*65536 + g*256 + b); y: what it is made of; z: a random number; w: spare
// No textures: slabs, cobbles, sleepers and wear all come from noise in world position, so nothing repeats.
Shader "MarsNeedsWater/EarthStreet"
{
    Properties
    {
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Geometry+15" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Street"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha, Zero One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "EarthToon.hlsl"

            // what a shape is made of (Core/RoadPaint.cs has the same numbers)
            #define K_FLAT 0
            #define K_ASPHALT 1
            #define K_SLABS 2
            #define K_COBBLES 3
            #define K_EARTH 4
            #define K_GRAVEL 5
            #define K_STEPS 6
            #define K_KERB 7
            #define K_LINE 8
            #define K_TRACK 9
            #define K_VERGE 10
            #define K_SLEEPERS 11
            #define K_STEEL 12

            float4 _GroundOffset;       // set by EarthGround.Shift: keeps world noise in place when the world re-centres

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                nointerpolation float4 uv1 : TEXCOORD1;
                nointerpolation float4 uv2 : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float3 normalWS : TEXCOORD4;
                float fog : TEXCOORD5;
            };

            uint SHash(uint2 v)
            {
                uint h = v.x * 0x8da6b343u ^ v.y * 0xd8163841u;
                h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
                return h;
            }

            float SRand(float2 c)
            {
                int2 ic = int2(floor(c));
                return float(SHash(uint2(ic + int2(32768, 32768))) & 0xffffu) / 65535.0;
            }

            float2 SRand2(float2 c)
            {
                int2 ic = int2(floor(c));
                uint h = SHash(uint2(ic + int2(32768, 32768)));
                return float2(float(h & 0xffffu), float(h >> 16)) / 65535.0;
            }

            float SNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = p - i;
                f = f * f * (3.0 - 2.0 * f);
                float a = SRand(i), b = SRand(i + float2(1.0, 0.0)), c = SRand(i + float2(0.0, 1.0)), d = SRand(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float SFbm(float2 p)
            {
                return SNoise(p) * 0.55 + SNoise(p * 2.07 + float2(17.1, 5.3)) * 0.3 + SNoise(p * 4.3 + float2(3.7, 11.9)) * 0.15;
            }

            // loosely packed stones: x = how far the pixel is from the edge of its stone (0 at the gap), y = a number per stone
            float2 SStones(float2 p)
            {
                float2 cell = floor(p), f = p - cell;
                float d1 = 9.0, d2 = 9.0, id = 0.0;
                for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                    {
                        float2 o = float2(i, j);
                        float2 r = o + 0.15 + 0.7 * SRand2(cell + o) - f;
                        float d = dot(r, r);
                        if (d < d1) { d2 = d1; d1 = d; id = SRand(cell + o + 91.0); }
                        else if (d < d2) d2 = d;
                    }
                return float2(sqrt(d2) - sqrt(d1), id);
            }

            float3 SLin(float3 c)
            {
            #if defined(UNITY_COLORSPACE_GAMMA)
                return c;
            #else
                return c * c * (c * 0.2848 + 0.7152);       // close to the sRGB curve
            #endif
            }

            float3 SColour(float packed)
            {
                float r = floor(packed / 65536.0);
                float g = floor((packed - r * 65536.0) / 256.0);
                float b = packed - r * 65536.0 - g * 256.0;
                return SLin(float3(r, g, b) / 255.0);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv0 = v.uv0; o.uv1 = v.uv1; o.uv2 = v.uv2;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 size = i.uv1.xy;
                float corner = i.uv1.z, soft = i.uv1.w;
                int kind = int(i.uv2.y + 0.5);
                float seed = i.uv2.z, extra = i.uv2.w;

                // ---- the outline: a box with rounded corners (a long band, a dash, a disc are all cases of it)
                float2 q = abs(i.uv0) - (size - corner);
                float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - corner;      // below zero inside
                // a negative roundness turns the shape inside out: everything outside a circle (a rounded street corner)
                if (corner < 0.0) sd = -corner - length(i.uv0);
                float aa = max(fwidth(sd), 1e-4);
                float cover = soft > 0.0 ? smoothstep(0.0, 1.0, saturate(-sd / max(soft, aa))) : saturate(-sd / aa);
                clip(cover - 0.004);

                float across = sd + size.x;        // metres from the middle line of a band (or the middle of a disc)
                float along = i.uv0.y;
                float2 world = i.positionWS.xz + _GroundOffset.xy;
                // detail too fine for the pixels it lands on is left out, so nothing shimmers far away
                float px = max(length(fwidth(world)), 1e-5);
                float fine = saturate(0.03 / px - 0.25), mid = saturate(0.12 / px - 0.25);

                float3 col = SColour(i.uv2.x);
                float big = SFbm(world * 0.11 + 31.7), patch = SFbm(world * 0.6 + 7.3);
                float alpha = 1.0;

                if (kind == K_ASPHALT)
                {
                    // broad lighter and darker stretches, a paler worn edge, and a little grain up close
                    col *= lerp(0.9, 1.1, smoothstep(0.25, 0.75, big));
                    col *= lerp(0.96, 1.04, smoothstep(0.35, 0.65, patch));
                    col *= 1.0 + 0.1 * smoothstep(size.x - 0.3, size.x, across) * smoothstep(0.3, 0.6, SNoise(world * 1.9 + 3.0));
                    col *= 1.0 + (SNoise(world * 19.0) - 0.5) * 0.07 * fine;
                }
                else if (kind == K_SLABS)
                {
                    // paving slabs in rows beside the kerb, each row shifted by its own amount, each slab its own shade
                    float t = max(across - extra, 0.0) / 0.43;
                    float row = floor(t);
                    float u = along / 0.62 + SRand(float2(row, floor(seed * 977.0))) ;
                    float slab = SRand(float2(floor(u), row * 7.0 + floor(seed * 977.0)));
                    float jt = min(abs(frac(t) - 0.5), abs(frac(u) - 0.5));        // 0.5 at a joint
                    float joint = smoothstep(0.455, 0.49, jt) * mid;
                    col *= lerp(0.95, 1.05, slab * mid + 0.5 * (1.0 - mid));
                    col *= 1.0 - 0.2 * joint;
                    col *= lerp(0.96, 1.04, smoothstep(0.3, 0.7, big));
                }
                else if (kind == K_COBBLES)
                {
                    float2 st = SStones(world / 0.26);
                    col *= lerp(1.0, lerp(0.86, 1.1, st.y), mid);
                    col *= 1.0 - 0.3 * (1.0 - smoothstep(0.03, 0.12, st.x)) * mid;
                    col *= lerp(0.95, 1.05, smoothstep(0.3, 0.7, big));
                }
                else if (kind == K_EARTH || kind == K_GRAVEL || kind == K_TRACK)
                {
                    col *= lerp(0.86, 1.1, smoothstep(0.2, 0.8, patch)) * lerp(0.94, 1.06, big);
                    float stones = SNoise(world * 9.0);
                    float grit = kind == K_EARTH ? 0.5 : 1.0;
                    col *= 1.0 + (smoothstep(0.62, 0.8, stones) * 0.16 - smoothstep(0.38, 0.2, stones) * 0.12) * grit * mid;
                    if (kind == K_TRACK)
                    {
                        // two wheel ruts pressed darker, and a strip the wheels never touch where weeds come up
                        float rut = 1.0 - smoothstep(0.1, 0.24, abs(across / max(size.x, 1e-3) - 0.52));
                        col *= 1.0 - 0.14 * rut;
                        float weeds = (1.0 - smoothstep(0.1, 0.26, across / max(size.x, 1e-3))) * smoothstep(0.35, 0.6, SNoise(world * 2.3 + 9.0) * 0.6 + SNoise(world * 7.0) * 0.4);
                        col = lerp(col, SLin(float3(0.48, 0.62, 0.3)), weeds * 0.75);
                    }
                    // a rough edge, not a ruled one
                    alpha *= saturate(cover * (0.7 + 0.9 * SNoise(world * 3.7 + 5.0)));
                }
                else if (kind == K_STEPS)
                {
                    float f = frac(along / 0.3);
                    col *= 1.0 - 0.28 * (1.0 - smoothstep(0.1, 0.2, f)) * mid;
                    col *= 1.0 + 0.06 * smoothstep(0.75, 0.95, f) * mid;
                }
                else if (kind == K_KERB)
                {
                    float f = abs(frac(along / 0.9 + seed * 7.0) - 0.5);
                    col *= 1.0 - 0.14 * smoothstep(0.47, 0.495, f) * mid;
                }
                else if (kind == K_LINE)
                {
                    // paint that has seen some traffic: thinner in places, never a pattern
                    float wear = smoothstep(0.5, 0.8, SNoise(world * 2.3 + seed * 37.0)) * smoothstep(0.35, 0.7, SNoise(world * 13.0));
                    alpha *= 1.0 - 0.22 * wear * mid;
                    col *= lerp(0.94, 1.0, patch);
                }
                else if (kind == K_VERGE)
                {
                    // a worn, dusty edge beside a country road, nibbled by the grass here and there
                    float n = SNoise(world * 1.7 + 2.0) * 0.6 + SNoise(world * 5.3) * 0.4;
                    alpha *= 0.9 * smoothstep(0.2, 0.45, n + 0.25);
                    col *= lerp(0.9, 1.08, patch);
                }
                else if (kind == K_SLEEPERS)
                {
                    // a bed of stones, with a sleeper every half metre
                    float stones = SNoise(world * 11.0);
                    col *= 1.0 + (smoothstep(0.6, 0.8, stones) * 0.2 - smoothstep(0.4, 0.2, stones) * 0.16) * mid;
                    col *= lerp(0.92, 1.06, patch);
                    float u = along / 0.5;
                    float tie = (1.0 - smoothstep(0.17, 0.21, abs(frac(u) - 0.5))) * (1.0 - smoothstep(0.52, 0.56, abs(i.uv0.x)));
                    float3 wood = SLin(float3(0.43, 0.33, 0.25)) * lerp(0.85, 1.12, SRand(float2(floor(u), floor(seed * 977.0))));
                    col = lerp(col, wood, tie * saturate(mid * 2.0));
                }
                else if (kind == K_STEEL)
                {
                    col *= lerp(0.92, 1.05, SNoise(world * 0.9 + 4.0));
                }
                else
                {
                    col *= lerp(0.95, 1.05, smoothstep(0.3, 0.7, patch)) * lerp(0.97, 1.03, big);
                }

                alpha *= cover;
                clip(alpha - 0.004);

                float3 n = normalize(i.normalWS);
                float4 sc = TransformWorldToShadowCoord(i.positionWS);
                Light sun = GetMainLight(sc);
                half shadow = ToonShadow(sun.shadowAttenuation, i.positionWS);
                half ndl = dot(n, sun.direction) + 0.12;
                half3 sky = SampleSH(n);
                half3 lit = ToonLight(ToonPop(col), ndl, shadow, sun.color, sky, 1.0);
                if (kind == K_STEEL)
                {
                    half3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                    lit += sun.color * ToonSpec(dot(n, normalize(sun.direction + V)), 0.75) * shadow;
                }
                lit = MixFog(lit, i.fog);
                return half4(lit, saturate(alpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
