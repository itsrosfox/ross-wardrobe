// Unity's Standard (metallic) lighting with both sides of the cloth drawn, for PBR.rmat: the Standard shader always
// culls back faces, so the inside of a sleeve or collar would be see-through. Same property names as Standard, so
// materials can switch between the two. Back faces are lit with the normal turned around.
Shader "Ros' Wardrobe/Standard Double Sided" {
    Properties {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0, 1)) = 0.5
        _GlossMapScale ("Smoothness Scale", Range(0, 1)) = 1
        [Gamma] _Metallic ("Metallic", Range(0, 1)) = 0
        _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "white" {}
        _BumpScale ("Normal Scale", Float) = 1
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1
        _OcclusionMap ("Occlusion (G)", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        // where the albedo's alpha is below this the cloth isn't drawn: cut-out parts such as round buttons
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }
    SubShader {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        LOD 300
        Cull [_Cull]

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        #pragma shader_feature_local _NORMALMAP
        #pragma shader_feature_local _METALLICGLOSSMAP

        sampler2D _MainTex, _MetallicGlossMap, _BumpMap, _OcclusionMap;
        fixed4 _Color;
        half _Glossiness, _GlossMapScale, _Metallic, _BumpScale, _OcclusionStrength, _Cutoff;

        struct Input {
            float2 uv_MainTex;
            fixed facing : VFACE;
        };

        void surf(Input IN, inout SurfaceOutputStandard o) {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            clip(c.a - _Cutoff);   // also in the shadow pass (addshadow)
            o.Alpha = c.a;
#if _METALLICGLOSSMAP
            half4 mg = tex2D(_MetallicGlossMap, IN.uv_MainTex);
            o.Metallic = mg.r;
            o.Smoothness = mg.a * _GlossMapScale;
#else
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
#endif
            o.Occlusion = lerp(1, tex2D(_OcclusionMap, IN.uv_MainTex).g, _OcclusionStrength);
            float3 n = float3(0, 0, 1);
#if _NORMALMAP
            n = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            n.xy *= _BumpScale;
            n = normalize(n);
#endif
            // the inside of the cloth faces the other way
            if (IN.facing < 0) n.z = -n.z;
            o.Normal = n;
        }
        ENDCG
    }
    FallBack "Standard"
}
