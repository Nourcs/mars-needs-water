// Everything in the voxel look that is not the ground (Core/Voxel.cs): buildings, trees, landmarks, bridges,
// the jeep, the player. Faces carry their place in the block grid; each block gets its own random shade.
// uv0: block coordinates across the face. uv1: colour, second colour, kind (0 plain, 1 wall with windows, 2 leaves,
// 3 glass wall), seed. uv2: walls: blocks per floor, blocks per bay, rows in all, shift along the wall.
Shader "MarsNeedsWater/EarthVoxel"
{
    Properties
    {
        _Rim ("Edge light", Range(0, 1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half _Rim;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "EarthToon.hlsl"
            #include "EarthVoxel.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 a : TEXCOORD1; float4 b : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2;
                float fog : TEXCOORD3; nointerpolation float4 a : TEXCOORD4; nointerpolation float4 b : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.a = v.a; o.b = v.b;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float far = saturate(max(fwidth(i.uv.x), fwidth(i.uv.y)) * 1.5 - 0.5);
                float2 cell = floor(i.uv);
                float2 inside = frac(i.uv);
                half3 col = VUnpack(i.a.x);
                float kind = i.a.z, seed = i.a.w;
                half amount = 0.09;
                half shine = 0.0;
                if (kind > 0.5 && kind < 1.5 || kind > 2.5)
                {
                    float F = max(i.b.x, 2.0), P = max(i.b.y, 2.0), rows = i.b.z;
                    float r = fmod(cell.y, F), c = fmod(cell.x + i.b.w + 4096.0, P);
                    bool glass;
                    if (kind > 2.5) glass = r < F - 1.0 || F < 2.5;                      // glass towers: bands of glass, a line of wall at each floor
                    else
                    {
                        bool winRow = F < 2.5 ? r > 0.5 : (r > 0.5 && r < F - 0.5);
                        glass = winRow && c > 0.5 && cell.y < rows - 0.5;
                    }
                    if (cell.y < 0.0 || cell.y > rows - 0.5) glass = false;
                    if (glass)
                    {
                        half3 pane = VUnpack(i.a.y);
                        float h = VHash(float3(cell, seed + 3.0));
                        // most panes dark, some catching the sky, a few lit from inside
                        col = pane * (0.85 + 0.4 * h);
                        if (h > 0.93) col = lerp(col, half3(0.55, 0.72, 0.9), 0.5);
                        if (h < 0.018) col = half3(1.0, 0.78, 0.36);
                        amount = 0.03;
                        shine = 0.5;
                    }
                }
                else if (kind > 1.5) amount = 0.2;
                col = ToonPop(col);
                col = VShade(col, float3(cell, 0.0), inside, seed, amount, far);

                float3 n = normalize(i.normalWS);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 sky = SampleSH(n);
                half shadow = ToonShadow(sun.shadowAttenuation, i.positionWS);
                half3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                half3 H = normalize(sun.direction + V);
                half3 lit = ToonLight(col, dot(n, sun.direction), shadow, sun.color, sky, 1.0);
                lit += sun.color * ToonSpec(dot(n, H), shine) * shadow;
                lit += ToonRim(n, V, sun.direction, sky, _Rim) * col;
                return half4(MixFog(lit, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vo { float4 positionCS : SV_POSITION; };
            Vo vert(A v)
            {
                Vo o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 pw = TransformObjectToWorld(v.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pw, nw, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }
            half4 frag(Vo i) : SV_Target { return 0; }
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
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vo { float4 positionCS : SV_POSITION; };
            Vo vert(A v) { Vo o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
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
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vo { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Vo vert(A v) { Vo o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag(Vo i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
