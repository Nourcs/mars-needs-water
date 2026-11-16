// Tyre tracks left by the jeep (the ribbons made by Runtime/JeepTrails.cs), drawn over the ground and the roads.
// No textures: the pressed band, the tread blocks, the raised edges of a rut and the ragged outline all come from
// numbers on each vertex and noise in world position, so no two stretches look the same.
//   uv0  x: metres along the track, y: across (-1 .. 1), z: when it was made (s), w: how long it lasts (s)
//   uv1  x: rut (0 a flat mark, 1 a deep rut with raised edges), y: how much tread shows, z: random seed, w: half width (m)
//   uv2  rgb: the colour of the pressed ground (linear), a: strength
Shader "MarsNeedsWater/EarthTrail"
{
    Properties
    {
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Geometry+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Trail"
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

            float4 _GroundOffset;       // set by EarthGround.Shift: keeps world noise in place when the world re-centres
            float _TrailNow;            // the clock the tracks' birth times are measured on

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float3 normalWS : TEXCOORD4;
                float fog : TEXCOORD5;
            };

            uint THash(uint2 v)
            {
                uint h = v.x * 0x8da6b343u ^ v.y * 0xd8163841u;
                h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
                return h;
            }

            float TRand(float2 c)
            {
                int2 ic = int2(floor(c));
                return float(THash(uint2(ic + int2(32768, 32768))) & 0xffffu) / 65535.0;
            }

            float TNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = p - i;
                f = f * f * (3.0 - 2.0 * f);
                float a = TRand(i), b = TRand(i + float2(1.0, 0.0)), c = TRand(i + float2(0.0, 1.0)), d = TRand(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
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
                float u = i.uv0.x, v = i.uv0.y;
                float life = max(i.uv0.w, 0.01);
                float fade = saturate((life - (_TrailNow - i.uv0.z)) / (life * 0.4));
                float rut = saturate(i.uv1.x), tread = saturate(i.uv1.y), seed = i.uv1.z;
                clip(i.uv2.a * fade - 0.004);

                float2 world = i.positionWS.xz + _GroundOffset.xy;
                float n1 = TNoise(world * 5.0 + seed * 37.0), n2 = TNoise(world * 17.0 + 5.1), big = TNoise(world * 0.7 + 11.3);

                // across the track: the pressed band in the middle, and round a rut the ground pushed up beside it.
                // The outline wanders with the noise so the edges are ragged, never ruled lines.
                float av = abs(v) + (n1 - 0.5) * 0.28 + (n2 - 0.5) * 0.12;
                float aa = max(fwidth(v) * 1.5, 0.02);
                float innerEdge = lerp(0.9, 0.64, rut);
                float inside = saturate((innerEdge - av) / aa + 0.5);
                float outer = smoothstep(1.0, 0.86, av);
                float berm = saturate(outer - inside) * rut;

                // the tread: chevron blocks with a groove down each shoulder, shown less where mud or snow has filled it in,
                // and fading out where the pixels are too big to draw it
                float tu = u / 0.27 + abs(v) * 0.5;
                float f = tu - floor(tu);
                float block = smoothstep(0.0, 0.1, f) * smoothstep(0.64, 0.54, f);
                float groove = smoothstep(0.0, 0.07, abs(abs(v) - 0.36));
                float filled = smoothstep(0.2, 0.62, TNoise(world * 2.1 + 3.0) * 0.6 + n1 * 0.4);
                float sharp = saturate(1.6 - fwidth(u) * 9.0);
                float pattern = lerp(1.0, lerp(0.4, 1.0, block * groove), tread * sharp * lerp(0.45, 1.0, filled));

                // nothing is pressed evenly: stronger and weaker stretches
                float uneven = lerp(0.72, 1.0, smoothstep(0.2, 0.7, big * 0.6 + n1 * 0.4));

                float a = i.uv2.a * fade * (inside * pattern * uneven + berm * 0.85);
                clip(a - 0.004);

                half3 pressed = i.uv2.rgb * lerp(1.0, 0.8 + 0.2 * smoothstep(innerEdge, innerEdge * 0.45, av), rut);    // darker under the walls of a rut
                half3 raised = saturate(i.uv2.rgb * 1.6 + 0.035);
                half3 albedo = lerp(pressed, raised, saturate(berm / max(inside + berm, 1e-3)));

                float3 n = normalize(i.normalWS);
                float4 sc = TransformWorldToShadowCoord(i.positionWS);
                Light sun = GetMainLight(sc);
                half shadow = ToonShadow(sun.shadowAttenuation, i.positionWS);
                half ndl = dot(n, sun.direction) + 0.12;
                half3 sky = SampleSH(n);
                half3 col = ToonLight(ToonPop(albedo), ndl, shadow, sun.color, sky, 1.0);
                col = MixFog(col, i.fog);
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
