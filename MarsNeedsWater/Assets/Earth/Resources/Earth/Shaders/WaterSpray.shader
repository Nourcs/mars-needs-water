// Drops and spray thrown up from the water (particles made by EarthWater.cs). Soft round dots, lit by the sun.
Shader "MarsNeedsWater/WaterSpray"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Spray"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; half4 colour : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 colour : COLOR; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.colour = v.colour;
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float d = length(i.uv * 2 - 1);
                half a = pow(saturate(1 - d), 1.5);
                Light sun = GetMainLight();
                half3 lit = sun.color * (0.35 + 0.65 * saturate(sun.direction.y)) + SampleSH(half3(0, 1, 0));
                half3 c = i.colour.rgb * lit * (0.85 + 0.25 * (1 - d));
                return half4(MixFog(c, i.fog), a * i.colour.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
