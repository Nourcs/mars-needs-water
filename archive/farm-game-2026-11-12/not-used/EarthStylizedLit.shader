// Stylised lit shader for Mars Needs Water: bright albedo (optionally times vertex colour), a soft cel-shaded
// terminator, main-light shadows, ambient from the sky, a gentle rim light and a small specular highlight.
// Extras: wind sway (vertex colour alpha below 1 marks how much a vertex sways: grass tips, leaves) and painted
// ground detail (a soft world-space noise that breaks up flat colour), and procedural windows on building walls
// (vertex alpha 0.9 marks a wall that gets a grid of glass windows; no textures, no extra geometry). Its own shadow and depth passes use the
// same sway, so swaying grass and leaves cast matching shadows and work with depth priming.
Shader "MarsNeedsWater/StylizedLit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _BaseMap ("Base Map", 2D) = "white" {}
        _EmissionColor ("Emission", Color) = (0,0,0,0)
        _Smoothness ("Smoothness", Range(0,1)) = 0.2
        _ShadeSoftness ("Shade Softness", Range(0.01,1)) = 0.35
        _RimStrength ("Rim Strength", Range(0,1)) = 0.2
        _UseVertexColor ("Use Vertex Color", Float) = 0
        _Wind ("Wind Sway (m)", Float) = 0
        _Detail ("Ground Detail", Range(0,0.5)) = 0
        _Windows ("Windows", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            float4 _EmissionColor;
            float _Smoothness;
            float _ShadeSoftness;
            float _RimStrength;
            float _UseVertexColor;
            float _Wind;
            float _Detail;
            float _Windows;
            float _Cutoff;
            float _Cull;
        CBUFFER_END

        // World to planet space, set every frame by Planet.cs, so wind and ground detail stay glued to the ground
        // when the planet is re-based under the player.
        float4x4 _WorldToPlanet;

        float3 PlanetPos(float3 positionWS) { return mul(_WorldToPlanet, float4(positionWS, 1.0)).xyz; }

        float3 Sway(float3 positionWS, float weight)
        {
            if (_Wind <= 0.0 || weight <= 0.0) return positionWS;
            float3 p = PlanetPos(positionWS);
            float t = _Time.y;
            float gust = 0.65 + 0.35 * sin(t * 0.45 + p.x * 0.013 + p.z * 0.017);
            float a = sin(t * 1.9 + p.x * 0.37 + p.z * 0.23) + 0.45 * sin(t * 3.3 + p.y * 0.51 + p.x * 0.19);
            float b = cos(t * 1.4 + p.z * 0.31 + p.y * 0.27);
            positionWS.xz += float2(a, b * 0.6) * (_Wind * weight * gust);
            return positionWS;
        }

        float Hash3(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float ValueNoise(float3 p)
        {
            float3 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float n000 = Hash3(i), n100 = Hash3(i + float3(1, 0, 0)), n010 = Hash3(i + float3(0, 1, 0)), n110 = Hash3(i + float3(1, 1, 0));
            float n001 = Hash3(i + float3(0, 0, 1)), n101 = Hash3(i + float3(1, 0, 1)), n011 = Hash3(i + float3(0, 1, 1)), n111 = Hash3(i + float3(1, 1, 1));
            return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y), lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 ws = Sway(TransformObjectToWorld(v.positionOS.xyz), saturate(1.0 - v.color.a) * _UseVertexColor);
                o.positionCS = TransformWorldToHClip(ws);
                o.positionWS = ws;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = lerp(float4(1,1,1,1), v.color, _UseVertexColor);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb * i.color.rgb;
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                if (_Cull < 0.5 && dot(n, v) < 0) n = -n; // two-sided (grass, bird wings): light the side we see

                // Painted ground detail: two octaves of soft noise, fading out with distance so it never shimmers.
                if (_Detail > 0.0)
                {
                    float3 p = PlanetPos(i.positionWS);
                    float d = ValueNoise(p * 0.45) * 0.65 + ValueNoise(p * 1.9) * 0.35 - 0.5;
                    float big = ValueNoise(p * 0.06) - 0.5;
                    float fade = saturate(1.0 - length(i.positionWS - GetCameraPositionWS()) / 260.0);
                    albedo *= 1.0 + (d * fade + big * 0.6) * _Detail * 2.0;
                }

                // Windows: a grid of glass panes on walls marked with vertex alpha 0.9, laid out in planet space
                // (rows every 3.2 m of height, columns every 2.6 m along the wall). Far away the grid blends into
                // an average tone so it never shimmers.
                if (_Windows > 0.0 && abs(i.color.a - 0.9) < 0.04)
                {
                    float3 p = PlanetPos(i.positionWS);
                    float3 upP = normalize(p);
                    float3 nP = normalize(mul((float3x3)_WorldToPlanet, n));
                    if (abs(dot(nP, upP)) < 0.35)
                    {
                        float3 t = normalize(cross(upP, nP));
                        float2 uv = float2(dot(p, t) / 2.6, length(p) / 3.2);
                        float2 f = frac(uv);
                        float win = step(0.2, f.x) * step(f.x, 0.8) * step(0.28, f.y) * step(f.y, 0.82);
                        float rnd = Hash3(float3(floor(uv), dot(p, nP) * 0.37));
                        float facing = saturate(dot(n, v));
                        half3 glass = lerp(half3(0.13, 0.2, 0.32), half3(0.5, 0.66, 0.85), saturate(rnd * 0.55 + (1.0 - facing) * 0.6));
                        float dist = length(i.positionWS - GetCameraPositionWS());
                        float near = saturate(1.0 - dist / 500.0);
                        albedo = lerp(albedo, glass, win * near + 0.3 * (1.0 - near));
                    }
                }

                Light l = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float ndl = dot(n, l.direction);
                // Soft two-tone shading: a short, smooth step from shade to light.
                float lit = smoothstep(-0.05, _ShadeSoftness, ndl) * l.shadowAttenuation;
                half3 ambient = SampleSH(n);
                float rim = pow(1.0 - saturate(dot(n, v)), 4.0) * _RimStrength * saturate(ndl * 0.5 + 0.5);
                float3 h = normalize(l.direction + v);
                float spec = pow(saturate(dot(n, h)), exp2(10.0 * _Smoothness + 1.0)) * _Smoothness * lit;
                half3 c = albedo * (ambient + l.color * lit * 0.95) + l.color * (spec + rim * 0.6);
                c += _EmissionColor.rgb;
                c = MixFog(c, i.fogFactor);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 ws = Sway(TransformObjectToWorld(v.positionOS.xyz), saturate(1.0 - v.color.a) * _UseVertexColor);
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - ws);
                #else
                    float3 ld = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(ws, nws, ld)));
                return o;
            }

            half4 frag (Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(Sway(TransformObjectToWorld(v.positionOS.xyz), saturate(1.0 - v.color.a) * _UseVertexColor));
                return o;
            }

            half frag (Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(Sway(TransformObjectToWorld(v.positionOS.xyz), saturate(1.0 - v.color.a) * _UseVertexColor));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5);
                    return half4(PackFloat2To888(oct), 0.0);
                #else
                    return half4(n, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
