// Recolours a garment's base texture region by region. Used by the editor only (preview + bake).
Shader "Hidden/RosDen/WardrobeBake" {
    Properties {
        _MainTex ("Base", 2D) = "white" {}
    }
    SubShader {
        Cull Off ZWrite Off ZTest Always
        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _RegionTex;      // R8, point filtered: region slot per texel (0 = untouched)
            sampler2D _GradTex;        // R16: position along the gradient, 0..1
            sampler2D _GradientAtlas;  // one gradient per row, row = region slot
            float4 _RegionColor[32];
            float _RegionMode[32];     // 0 keep, 1 solid, 2 gradient
            float _BaseLevel[32];      // mean luminance of the base texture inside each region
            float _Detail;

            float4 frag (v2f_img i) : SV_Target {
                float4 base = tex2D(_MainTex, i.uv);
                int r = (int)round(tex2D(_RegionTex, i.uv).r * 255.0);
                if (r <= 0 || r >= 32) return base;
                float mode = _RegionMode[r];
                if (mode < 0.5) return base;

                float3 col = _RegionColor[r].rgb;
                if (mode > 1.5) {
                    float t = saturate(tex2D(_GradTex, i.uv).r);
                    col = tex2D(_GradientAtlas, float2((t * 255.0 + 0.5) / 256.0, (r + 0.5) / 32.0)).rgb;
                }

                // Keep the fabric's shading/AO: scale the new colour by how much brighter or darker
                // this texel is than the region's average.
                float lum = dot(base.rgb, float3(0.2126, 0.7152, 0.0722));
                float detail = lerp(1.0, lum / max(_BaseLevel[r], 1e-4), _Detail);
                return float4(col * detail, base.a);
            }
            ENDCG
        }
    }
}
