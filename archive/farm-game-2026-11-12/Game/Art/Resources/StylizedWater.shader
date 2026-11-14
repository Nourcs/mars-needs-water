// Stylised water for Mars Needs Water's seas and lakes. Colour comes from the mesh (turquoise shallows to deep
// blue); vertex alpha is how deep the water is (0 at the shore, 1 from 8 m down). On top: moving ripples
// (procedural normals, no textures), the sky reflected at grazing angles, crisp cartoon sun glints, and a band of
// white foam along every shoreline that laps in and out. See-through: clear in the shallows so the sand, rocks
// and coral show, deeper water and grazing angles turn opaque. Both sides are drawn, so from under water you see
// the surface shimmering above you.
Shader "MarsNeedsWater/StylizedWater"
{
    Properties
    {
        _SkyColor ("Sky Reflection", Color) = (0.62, 0.8, 1, 1)
        _FoamColor ("Foam", Color) = (1, 1, 1, 1)
        _FoamWidth ("Foam Width", Range(0, 1)) = 0.1
        _Clarity ("Clarity", Range(0, 1)) = 0.7
        _Ripple ("Ripple Strength", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _SkyColor;
            float4 _FoamColor;
            float _FoamWidth;
            float _Ripple;
            float _Clarity;
        CBUFFER_END
        float4x4 _WorldToPlanet;
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
                float fogFactor : TEXCOORD2;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // Slope of a few travelling waves at planet-space point p (x and z only: water is flat near the player).
            float2 WaveSlope(float3 p, float t)
            {
                float2 s = 0;
                s += float2(0.8, 0.6) * cos(dot(p.xz, float2(0.8, 0.6)) * 0.9 + t * 1.6) * 0.9;
                s += float2(-0.5, 0.85) * cos(dot(p.xz, float2(-0.5, 0.85)) * 1.7 + t * 2.3) * 0.6;
                s += float2(0.95, -0.3) * cos(dot(p.zy, float2(0.95, -0.3)) * 3.1 + t * 3.1) * 0.35;
                s += float2(-0.2, -0.98) * cos(dot(p.xy, float2(-0.2, -0.98)) * 5.3 + t * 4.0) * 0.2;
                return s;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float t = _Time.y;
                float3 p = mul(_WorldToPlanet, float4(i.positionWS, 1.0)).xyz;
                float3 n0 = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float dist = length(i.positionWS - GetCameraPositionWS());
                float depth01 = i.color.a;

                if (dot(n0, v) < 0.0)
                {
                    // Seen from below: a bright, rippling ceiling, lighter where the sun comes through.
                    float2 sb = WaveSlope(p, t) * _Ripple;
                    Light lb = GetMainLight();
                    float sun = smoothstep(0.75, 1.0, dot(-v, lb.direction) + (sb.x - sb.y) * 0.08);
                    half3 under = lerp(i.color.rgb * 1.3, _SkyColor.rgb, 0.45 + 0.15 * (sb.x + sb.y)) + lb.color * sun * 0.6;
                    under = MixFog(under, i.fogFactor);
                    return half4(under, 0.85);
                }

                // Ripples fade into a calm surface far away (less sparkle noise, cheaper look).
                float near = saturate(1.0 - dist / 400.0);
                float2 s = WaveSlope(p, t) * _Ripple * (0.25 + 0.75 * near);
                float3 tangentX = normalize(cross(n0, float3(0, 0, 1)) + 1e-4);
                float3 tangentZ = normalize(cross(tangentX, n0));
                float3 n = normalize(n0 - tangentX * s.x * 0.35 - tangentZ * s.y * 0.35);

                Light l = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float shadow = lerp(0.55, 1.0, l.shadowAttenuation);
                half3 baseCol = i.color.rgb;
                half3 ambient = SampleSH(n0);
                half3 c = baseCol * (ambient * 0.9 + l.color * saturate(dot(n0, l.direction)) * 0.7 * shadow);

                // Sky at grazing angles (Fresnel), stepped a little for the cartoon look.
                float fres = pow(1.0 - saturate(dot(n, v)), 4.0);
                c = lerp(c, _SkyColor.rgb * (ambient + 0.4), saturate(fres * 0.9));

                // Crisp sun glints.
                float3 h = normalize(l.direction + v);
                float g = pow(saturate(dot(n, h)), 350.0);
                c += l.color * smoothstep(0.25, 0.6, g) * 1.4 * shadow;

                // Foam: a band along the shore (vertex alpha ~ depth) that laps in and out, broken up by ripples.
                float lap = 0.5 + 0.5 * sin(t * 1.1 + (p.x + p.z) * 0.15);
                float edge = _FoamWidth * (0.7 + 0.3 * lap) + (s.x + s.y) * 0.03;
                float foam = 1.0 - smoothstep(edge * 0.6, edge, depth01);
                // Wave crest flecks out at sea, near the camera only.
                float crest = smoothstep(1.05, 1.25, s.x + s.y) * near * 0.6;
                float white = saturate(foam + crest);
                c = lerp(c, _FoamColor.rgb * (ambient + l.color * 0.8 * shadow), white);

                // See-through: clear shallows, opaque deep water, and the surface hides what is under it at grazing
                // angles and far away (so the seabed's coarse distant ground never shows through).
                float a = lerp(1.0 - _Clarity, 0.97, smoothstep(0.0, 1.0, depth01));
                a = saturate(a + fres * 0.8 + white + g * 0.5);
                a = lerp(a, 1.0, saturate((dist - 150.0) / 450.0));
                c = MixFog(c, i.fogFactor);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
