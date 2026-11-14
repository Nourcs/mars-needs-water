// The ground of the mini Earth. Each map square has a colour picture whose alpha says what the ground is made of
// at each pixel (grass, sand, rock, snow ... see Core/GroundKind.cs). Up close this draws a detailed texture for
// that kind, sampled with randomly shifted and turned hexagons so it never repeats visibly, blended with its
// neighbours along wobbly borders, and tinted so that it averages to the map colour. Far away the map colour
// alone is drawn, so near and far meet without a seam.
Shader "MarsNeedsWater/EarthGround"
{
    Properties
    {
        _BaseMap ("Map colour (rgb) and ground kind (a, set per square)", 2D) = "white" {}
        _Albedo ("Ground textures: colour (rgb), height (a)", 2DArray) = "" {}
        _Normal ("Ground textures: normal (rg), occlusion (b), smoothness (a)", 2DArray) = "" {}
        _DetailNear ("Full detail within (m)", Float) = 160
        _DetailFar ("No detail beyond (m)", Float) = 700
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D_ARRAY(_Albedo);
        SAMPLER(sampler_Albedo);
        TEXTURE2D_ARRAY(_Normal);
        SAMPLER(sampler_Normal);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseMap_TexelSize;
            float _DetailNear;
            float _DetailFar;
        CBUFFER_END

        // set by EarthWorld (EarthGround.cs)
        float4 _GroundOffset;          // xy: added to world x, z so the textures stay put when the world re-centres
        float4 _GroundMean[10];        // rgb: each kind's average texture colour (linear); a: unused
        float4 _GroundParams[10];      // x: metres per repeat, y: 1 = may turn, or minus the number of rows (no turning, shifts keep rows in line), z: normal strength, w: occlusion strength
        ENDHLSL

        Pass
        {
            Name "Ground"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float fog : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // ---------------------------------------------------------------- noise

            uint Hash(uint2 v)
            {
                uint h = v.x * 0x8da6b343u ^ v.y * 0xd8163841u;
                h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
                return h;
            }

            float Rand(int2 c, uint s) { return (Hash(uint2(c) + uint2(s * 7919u, s * 104729u)) & 0xffffu) / 65535.0; }

            float ValueNoise(float2 p, uint s)
            {
                int2 i = (int2)floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Rand(i, s), b = Rand(i + int2(1, 0), s), c = Rand(i + int2(0, 1), s), d = Rand(i + int2(1, 1), s);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // ---------------------------------------------------------------- one kind, hex-tiled

            // Three overlapping copies of the texture on a hexagon grid, each shifted (and maybe turned) at random,
            // blended by how close the point is to each hexagon's centre. No two hexagons look the same.
            void Hex(int kind, float2 world, float2 wdx, float2 wdy, out half4 alb, out half4 nrm)
            {
                float4 prm = _GroundParams[kind];
                float2 st = world / prm.x;
                float2 dx = wdx / prm.x, dy = wdy / prm.x;

                float2 q = st * 3.4641016;                        // 2 * sqrt(3): hexagons about one repeat across
                float2 sk = float2(q.x - 0.57735027 * q.y, 1.15470054 * q.y);
                int2 base = (int2)floor(sk);
                float3 t = float3(frac(sk), 0);
                t.z = 1.0 - t.x - t.y;
                float s = step(0.0, -t.z);
                float s2 = 2.0 * s - 1.0;
                float3 w = float3(-t.z * s2, s - t.y * s2, s - t.x * s2);
                int2 v1 = base + int2(s, s), v2 = base + int2(s, 1 - s), v3 = base + int2(1 - s, s);

                half4 a[3]; half4 n[3];
                int2 vs[3] = { v1, v2, v3 };
                [unroll] for (int i = 0; i < 3; i++)
                {
                    uint h = Hash(uint2(vs[i]) + uint2(kind * 131u, kind * 977u));
                    float2 off = float2((h & 0xffffu), (h >> 16)) / 65535.0;
                    // rows (furrows, ripples) stay unbroken from one hexagon to the next
                    if (prm.y < -0.5) off.y = floor(off.y * -prm.y) / -prm.y;
                    float ang = prm.y > 0.5 ? (Hash(uint2(h, 77u)) & 0xffffu) / 65535.0 * 6.2831853 : 0.0;
                    float c = cos(ang), sn = sin(ang);
                    float2x2 r = float2x2(c, -sn, sn, c);
                    float2 uv = mul(r, st) + off;
                    a[i] = SAMPLE_TEXTURE2D_ARRAY_GRAD(_Albedo, sampler_Albedo, uv, kind, mul(r, dx), mul(r, dy));
                    half4 nn = SAMPLE_TEXTURE2D_ARRAY_GRAD(_Normal, sampler_Normal, uv, kind, mul(r, dx), mul(r, dy));
                    // the normal was turned with the texture: turn it back
                    float2 nxy = nn.xy * 2.0 - 1.0;
                    nxy = mul(transpose(r), nxy);
                    n[i] = half4(nxy, nn.z, nn.w);
                }
                // sharpen the blend, favouring the higher bits of each copy, so the texture stays crisp
                float3 g = w * w * w * float3(a[0].a + 0.3, a[1].a + 0.3, a[2].a + 0.3);
                g /= max(g.x + g.y + g.z, 1e-4);
                alb = a[0] * g.x + a[1] * g.y + a[2] * g.z;
                nrm = n[0] * g.x + n[1] * g.y + n[2] * g.z;
            }

            // ---------------------------------------------------------------- main

            half4 frag(Varyings i) : SV_Target
            {
                float3 nGeo = normalize(i.normalWS);
                half3 mapCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                float dist = distance(i.positionWS, _WorldSpaceCameraPos);
                float detail = 1.0 - smoothstep(_DetailNear, _DetailFar, dist);
                float2 world = i.positionWS.xz + _GroundOffset.xy;
                // screen-space rates of change, worked out here where every pixel takes the same path
                float2 wdx = ddx(world), wdy = ddy(world);

                half3 albedo = mapCol;
                float3 n = nGeo;
                half ao = 1.0, smooth = 0.1;


                if (detail > 0.001)
                {
                    // the four map pixels round this point, with a wobble so borders are ragged, not pixel steps
                    float N = _BaseMap_TexelSize.z;
                    float2 wob = float2(ValueNoise(world * 0.9, 11u), ValueNoise(world * 0.9, 13u)) - 0.5;
                    wob += (float2(ValueNoise(world * 3.1, 17u), ValueNoise(world * 3.1, 19u)) - 0.5) * 0.5;
                    // roads, paths and pavements keep clean edges; only natural ground gets ragged borders
                    int2 lim0 = int2(N - 1, N - 1);
                    int kc = (int)round(LOAD_TEXTURE2D(_BaseMap, clamp((int2)floor(i.uv * N), 0, lim0)).a * 255.0);
                    float2 p = i.uv * N - 0.5 + wob * (kc == 7 ? 0.0 : 1.6);
                    int2 b = (int2)floor(p);
                    float2 f = frac(p);
                    int2 lim = int2(N - 1, N - 1);
                    int k00 = (int)round(LOAD_TEXTURE2D(_BaseMap, clamp(b, 0, lim)).a * 255.0);
                    int k10 = (int)round(LOAD_TEXTURE2D(_BaseMap, clamp(b + int2(1, 0), 0, lim)).a * 255.0);
                    int k01 = (int)round(LOAD_TEXTURE2D(_BaseMap, clamp(b + int2(0, 1), 0, lim)).a * 255.0);
                    int k11 = (int)round(LOAD_TEXTURE2D(_BaseMap, clamp(b + int2(1, 1), 0, lim)).a * 255.0);
                    int4 ks = min(int4(k00, k10, k01, k11), 9);
                    float4 ws = float4((1 - f.x) * (1 - f.y), f.x * (1 - f.y), (1 - f.x) * f.y, f.x * f.y);
                    // the same kind twice counts once
                    if (ks.y == ks.x) { ws.x += ws.y; ws.y = 0; }
                    if (ks.z == ks.x) { ws.x += ws.z; ws.z = 0; } else if (ks.z == ks.y) { ws.y += ws.z; ws.z = 0; }
                    if (ks.w == ks.x) { ws.x += ws.w; ws.w = 0; } else if (ks.w == ks.y) { ws.y += ws.w; ws.w = 0; } else if (ks.w == ks.z) { ws.z += ws.w; ws.w = 0; }

                    half3 sumA = 0; float2 sumN = 0; half sumO = 0, sumS = 0; float sumW = 0;
                    float hmax = -1;
                    half4 la[4]; half4 ln[4]; float lw[4];
                    [unroll] for (int j = 0; j < 4; j++)
                    {
                        la[j] = 0; ln[j] = half4(0.5, 0.5, 1, 0); lw[j] = 0;
                        if (ws[j] > 0.001)
                        {
                            Hex(ks[j], world, wdx, wdy, la[j], ln[j]);
                            // height blend: where two kinds meet, the higher bits of either show through
                            lw[j] = la[j].a + ws[j] * 1.6;
                            hmax = max(hmax, lw[j]);
                        }
                    }
                    [unroll] for (int j2 = 0; j2 < 4; j2++)
                    {
                        if (ws[j2] <= 0.001) continue;
                        float bw = max(lw[j2] - hmax + 0.35, 0.0);
                        int kk = ks[j2];
                        // carry the map colour into the texture: the texture's own pattern, the map's average
                        half3 ratio = mapCol / max(_GroundMean[kk].rgb, 0.003);
                        ratio = clamp(ratio, 0.25, 4.0);
                        half3 c = la[j2].rgb * ratio;
                        // the same kind's texture seen seven times larger shapes its big patches (dunes for sand,
                        // slabs for rock, drifts for snow, clumps for grass), so no two kinds share one pattern
                        float2x2 mr = float2x2(0.8253, -0.5646, 0.5646, 0.8253);
                        float ms = 1.0 / (_GroundParams[kk].x * 7.0);
                        float2 mst = mul(mr, world * ms) + 0.37;
                        half3 macro = SAMPLE_TEXTURE2D_ARRAY_GRAD(_Albedo, sampler_Albedo, mst, kk, mul(mr, wdx * ms), mul(mr, wdy * ms)).rgb;
                        half ml = dot(macro, half3(0.3, 0.5, 0.2)) / max(dot(_GroundMean[kk].rgb, half3(0.3, 0.5, 0.2)), 0.003);
                        c *= lerp(1.0, clamp(ml, 0.5, 1.6), 0.4);
                        sumA += c * bw;
                        sumN += ln[j2].xy * _GroundParams[kk].z * bw;
                        sumO += lerp(1.0, ln[j2].z, _GroundParams[kk].w) * bw;
                        sumS += ln[j2].w * bw;
                        sumW += bw;
                    }
                    sumW = max(sumW, 1e-4);
                    half3 near = sumA / sumW;
                    float2 nt = sumN / sumW;
                    // turn the texture's normal onto the ground's slope (east = world x, north = world z)
                    float3 T = normalize(float3(1, 0, 0) - nGeo * nGeo.x);
                    float3 B = cross(T, nGeo);
                    if (dot(B, float3(0, 0, 1)) < 0) B = -B;
                    float3 nd = normalize(nGeo + (T * nt.x + B * nt.y) * detail);
                    albedo = lerp(mapCol, near, detail);
                    n = nd;
                    ao = lerp(1.0, sumO / sumW, detail);
                    smooth = lerp(0.1, sumS / sumW, detail);
                }

                // light: sun with shadows, sky, a soft highlight on wet and icy ground
                float4 sc = TransformWorldToShadowCoord(i.positionWS);
                Light sun = GetMainLight(sc);
                half shadow = sun.shadowAttenuation;
                half ndl = saturate(dot(n, sun.direction));
                // the big shape of the hill shades a little too, so detail does not flatten it
                ndl *= lerp(1.0, saturate(dot(nGeo, sun.direction) * 1.2 + 0.1), 0.25);
                half3 sky = SampleSH(n);
                half3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                half3 H = normalize(sun.direction + V);
                half spec = pow(saturate(dot(n, H)), exp2(10.0 * smooth + 1.0)) * smooth * 0.35;
                half3 col = albedo * (sun.color * ndl * shadow + sky * ao) + sun.color * spec * shadow;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 positionOS : POSITION; };
            struct Vo { float4 positionCS : SV_POSITION; };
            Vo vert(A v) { Vo o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half frag(Vo i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Vo { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Vo vert(A v) { Vo o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag(Vo i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
