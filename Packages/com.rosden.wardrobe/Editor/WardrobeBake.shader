// Bakes a look onto a garment: every region in a pattern (colour weights in the look's colour slots), plus the
// patterns' relief and surface. The garment's own colours aren't used (only its alpha and untouched texels); its normal,
// occlusion and surface maps are kept under the patterns'. Used by the editor only (preview + bake).
// Pass 0 lays one pattern over the regions in it, pass 1 starts a normal map from the garment's own, pass 2 blends one
// pattern's relief (normal and/or height map) into it (or replaces it), pass 3 writes one pattern's metalness, roughness,
// occlusion and sheen into the surface map (always R metalness, G roughness, B occlusion, A sheen), pass 4 packs channels
// of up to 4 textures into one (a material type's texture from the garment's maps).
// Passes 0, 2 and 3 run once per pattern in a look.
Shader "Hidden/RosDen/WardrobeBake" {
    Properties {
        _MainTex ("Base", 2D) = "white" {}
    }
    SubShader {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        sampler2D _RegionTex;      // R8, point filtered: region slot per texel (0 = untouched)
        sampler2D _PartTex;        // R8, point filtered: ID map part per texel, for its real-world UV scale
        sampler2D _GradTex;        // R16: position along the garment's gradient, 0..1
        sampler2D _SlotAtlas;      // the look's colour slots: row = region slot * 4 + colour slot, x = position along the gradient
        sampler2D _PatternTex;     // colour weights: R, G, B, A = how much of colour slot 1..4
        sampler2D _PatternNormal, _PatternSurface, _PatternOcclusion; // surface: R metal, G rough, A opacity; occlusion: R
        sampler2D _PatternHeight;  // R: relief as height (white = raised)
        float4 _PatternHeight_TexelSize;
        sampler2D _PatternGrad;    // hybrid: the pattern's own gradient map (garment UVs), instead of _GradTex
        sampler2D _AlbedoTex;      // complete: the picture on the garment's UVs, alpha = coverage
        sampler2D _MaskTex;        // complete: R = which texels it's on
        float _PartScale[256];     // metres of garment per UV unit, per part
        float _LayerOn[32];        // 1 where the region is in the pattern bound for this pass
        float4 _Tiling[32];        // x, y: tiles per metre (scale applied), z: 1 = turned 90°
        float _PatternOn;          // the bound pattern has colour weights (else colour slot 1 everywhere)
        float _PatternUV;          // its maps are laid out on the garment's UVs (else they tile)
        float _HasSurface;         // it has a surface map (else the garment's metalness/smoothness stay)
        float _NormalStrength;
        float _HasHeight;          // it has a height map, turned into a normal here
        float _HeightStrength;
        float _ReplaceNormal;      // its relief replaces the garment's normal instead of blending onto it
        float _Sheen;
        float _PatternGradOn;
        float _AlbedoOn;

        static const float AtlasRows = 128.0;

        int Region(float2 uv) { return (int)round(tex2D(_RegionTex, uv).r * 255.0); }

        // Pattern coordinates: the garment's UVs, or garment UV → metres (per part) → tiles, optionally turned 90°.
        float2 PatternUV(int r, float2 uv) {
            if (_PatternUV > 0.5) return uv;
            int part = (int)round(tex2D(_PartTex, uv).r * 255.0);
            float2 m = uv * _PartScale[part];
            if (_Tiling[r].z > 0.5) m = float2(m.y, -m.x);
            return m * _Tiling[r].xy;
        }

        float3 Slot(int r, int s, float t) {
            return tex2D(_SlotAtlas, float2((t * 255.0 + 0.5) / 256.0, (r * 4 + s + 0.5) / AtlasRows)).rgb;
        }

        float3 PatternColour(int r, float2 uv) {
            float t = saturate(_PatternGradOn > 0.5 ? tex2D(_PatternGrad, uv).r : tex2D(_GradTex, uv).r);
            float3 col = Slot(r, 0, t);
            if (_PatternOn > 0.5) {
                float4 w = tex2D(_PatternTex, PatternUV(r, uv));
                w /= max(w.r + w.g + w.b + w.a, 1e-4);
                col = w.r * Slot(r, 0, t) + w.g * Slot(r, 1, t) + w.b * Slot(r, 2, t) + w.a * Slot(r, 3, t);
            }
            if (_AlbedoOn > 0.5) {
                float4 a = tex2D(_AlbedoTex, uv);
                col = lerp(col, a.rgb, saturate(a.a * tex2D(_MaskTex, uv).r));
            }
            return col;
        }

        float3 Unpack(float4 packed, float strength) {
            float3 n = UnpackNormal(packed);
            n.xy *= strength;
            return normalize(n);
        }
        ENDCG

        Pass { // 0: pattern colour
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5

            float4 frag (v2f_img i) : SV_Target {
                float4 previous = tex2D(_MainTex, i.uv);
                int r = Region(i.uv);
                if (r <= 0 || r >= 32 || _LayerOn[r] < 0.5) return previous;
                float3 col = PatternColour(r, i.uv);
                // holes (eyelet, pointelle) read as dark openings; the garment itself stays opaque
                if (_HasSurface > 0.5) col *= 1.0 - 0.7 * (1.0 - tex2D(_PatternSurface, PatternUV(r, i.uv)).a);
                return float4(col, previous.a);
            }
            ENDCG
        }

        Pass { // 1: normal map start (garment's own normal → plain RGB normal)
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5

            float4 frag (v2f_img i) : SV_Target {
                return float4(UnpackNormal(tex2D(_MainTex, i.uv)) * 0.5 + 0.5, 1);
            }
            ENDCG
        }

        Pass { // 2: pattern relief (normal + height map, whiteout blended onto the normal so far or replacing it)
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5

            float4 frag (v2f_img i) : SV_Target {
                float4 previous = tex2D(_MainTex, i.uv);
                int r = Region(i.uv);
                if (r <= 0 || r >= 32 || _LayerOn[r] < 0.5) return previous;
                float3 n = previous.xyz * 2.0 - 1.0;
                float2 p = PatternUV(r, i.uv);
                float3 f = Unpack(tex2D(_PatternNormal, p), _NormalStrength);
                if (_HasHeight > 0.5) {
                    // central differences, one texel apart: the slope in height per texel, so strength 1 tilts a
                    // full black-to-white step over one texel to 45°
                    float2 t = _PatternHeight_TexelSize.xy;
                    float dx = tex2D(_PatternHeight, p + float2(t.x, 0)).r - tex2D(_PatternHeight, p - float2(t.x, 0)).r;
                    float dy = tex2D(_PatternHeight, p + float2(0, t.y)).r - tex2D(_PatternHeight, p - float2(0, t.y)).r;
                    float3 h = normalize(float3(-0.5 * _HeightStrength * float2(dx, dy), 1));
                    f = normalize(float3(f.xy + h.xy, f.z * h.z));
                }
                if (_PatternUV < 0.5 && _Tiling[r].z > 0.5) f.xy = float2(-f.y, f.x);
                if (_ReplaceNormal > 0.5) return float4(f * 0.5 + 0.5, 1);
                return float4(normalize(float3(n.xy + f.xy, n.z * f.z)) * 0.5 + 0.5, 1);
            }
            ENDCG
        }

        Pass { // 3: pattern surface (metalness + roughness + sheen, occlusion multiplied into the garment's)
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5

            float4 frag (v2f_img i) : SV_Target {
                float4 previous = tex2D(_MainTex, i.uv);
                int r = Region(i.uv);
                if (r <= 0 || r >= 32 || _LayerOn[r] < 0.5) return previous;
                float2 puv = PatternUV(r, i.uv);
                float4 f = tex2D(_PatternSurface, puv);
                float4 o = _HasSurface > 0.5 ? float4(f.r, f.g, previous.b, _Sheen) : previous;
                o.b *= tex2D(_PatternOcclusion, puv).r;
                return o;
            }
            ENDCG
        }

        Pass { // 4: pack: each channel from a channel of its own texture (or a value), optionally inverted
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5

            sampler2D _Pack0, _Pack1, _Pack2, _Pack3;
            float4 _PackChannel;   // per output channel: 0..3 = R..A of its texture, -1 = _PackValue
            float4 _PackInvert;
            float4 _PackValue;

            float Pick(sampler2D t, float c, float invert, float value, float2 uv) {
                float4 s = tex2D(t, uv);
                float v = c < -0.5 ? value : c < 0.5 ? s.r : c < 1.5 ? s.g : c < 2.5 ? s.b : s.a;
                return invert > 0.5 ? 1.0 - v : v;
            }

            float4 frag (v2f_img i) : SV_Target {
                return float4(
                    Pick(_Pack0, _PackChannel.x, _PackInvert.x, _PackValue.x, i.uv),
                    Pick(_Pack1, _PackChannel.y, _PackInvert.y, _PackValue.y, i.uv),
                    Pick(_Pack2, _PackChannel.z, _PackInvert.z, _PackValue.z, i.uv),
                    Pick(_Pack3, _PackChannel.w, _PackInvert.w, _PackValue.w, i.uv));
            }
            ENDCG
        }
    }
}
