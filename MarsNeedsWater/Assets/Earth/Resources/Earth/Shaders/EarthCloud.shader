// Clouds of the mini Earth: chunky solid puffs (meshes from Runtime/EarthClouds.cs), bright on top and where the
// sun strikes, soft lavender underneath, melting into the haze far away. Thousands are drawn at once (instanced).
Shader "MarsNeedsWater/EarthCloud"
{
    Properties
    {
        _Light ("Sunny side", Color) = (1.0, 1.0, 0.98, 1)
        _Shade ("Underside", Color) = (0.66, 0.74, 0.95, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Cloud"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Light, _Shade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; float fog : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light sun = GetMainLight();
                // light from above and from the sun, in three soft steps
                half t = n.y * 0.5 + dot(n, sun.direction) * 0.5;
                half lit = smoothstep(-0.28, -0.05, t) * 0.55 + smoothstep(0.12, 0.34, t) * 0.45;
                half3 col = lerp(_Shade.rgb, _Light.rgb, lit) * (sun.color * 0.6 + 0.5);
                // a glow where the edge turns away from the eye, towards the sun
                half3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                col += half3(1.0, 0.95, 0.85) * pow(1.0 - saturate(dot(n, V)), 3.0) * saturate(dot(n, sun.direction) + 0.3) * 0.12;
                return half4(MixFog(col, i.fog), 1);
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vo { float4 positionCS : SV_POSITION; };
            Vo vert(A v) { Vo o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half frag(Vo i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
