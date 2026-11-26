// Pierre (2026-11-26, Dabura) : le sprite affiché devient de la pierre grise, au pixel.
// Sert à Goku touché par le crachat de Dabura (Petrification) et à la mort de Dabura (DisappearOnDeath).
// Seuls les pixels sous la ligne _StoneLine (hauteur dans le monde, posée par StoneSkin) sont en pierre : la pierre
// monte des pieds à la tête en déplaçant cette ligne ; une rangée plus claire marque le front qui monte.
// Couleur : luminosité de l'image d'origine ramenée à 4 gris de pierre (contours presque noirs gardés sombres),
// grain au hasard par pixel, fissures sombres (bords d'un pavage de Voronoï, quelques-uns seulement).
// Tout est calculé dans le repère du personnage (_Origin) : grain et fissures ne glissent pas avec l'image.
// Projet en espace linéaire : la luminosité est lue en sRGB, les couleurs des réglages sont déjà converties par Unity.
Shader "DBZ/Stone"
{
    Properties
    {
        [HideInInspector] [NoScaleOffset] _MainTex("Image", 2D) = "white" {}
        _OutlineColor("Contours (pixels très sombres)", Color) = (0.12, 0.11, 0.11, 1)
        _DarkColor("Pierre sombre", Color) = (0.31, 0.29, 0.27, 1)
        _MidColor("Pierre moyenne", Color) = (0.49, 0.47, 0.44, 1)
        _LightColor("Pierre claire", Color) = (0.66, 0.64, 0.6, 1)
        _HighColor("Reflets", Color) = (0.81, 0.79, 0.75, 1)
        _CrackColor("Fissures", Color) = (0.17, 0.16, 0.15, 1)
        _FrontColor("Front qui monte", Color) = (0.88, 0.86, 0.82, 1)
        _Grain("Grain (0-0,4)", Range(0, 0.4)) = 0.12
        _CrackCell("Taille des morceaux entre fissures (pixels)", Range(3, 16)) = 7
        _CrackWidth("Épaisseur des fissures (pixels)", Range(0.2, 2)) = 0.7
        _CrackAmount("Part des bords fissurés (0-1)", Range(0, 1)) = 0.45
        _ShadowThreshold("Pixels plus sombres que ça gardés tels quels (ombre noire)", Range(0, 0.2)) = 0.02
        [HideInInspector] _StoneLine("Hauteur de la pierre (monde)", Float) = 100000
        [HideInInspector] _Origin("Origine du grain (monde)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                half4 _DarkColor;
                half4 _MidColor;
                half4 _LightColor;
                half4 _HighColor;
                half4 _CrackColor;
                half4 _FrontColor;
                float _Grain;
                float _CrackCell;
                float _CrackWidth;
                float _CrackAmount;
                float _ShadowThreshold;
                float _StoneLine;
                float4 _Origin;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            float2 Hash2(float2 p)
            {
                return float2(Hash(p), Hash(p + float2(37.1, 17.3)));
            }

            // Fissure sur ce pixel : bord entre deux morceaux (distance aux deux points les plus proches presque égale),
            // gardé seulement pour une part des paires de morceaux
            bool Crack(float2 pixel)
            {
                float2 p = (pixel + 0.5) / _CrackCell;
                float2 cell = floor(p);
                float d1 = 1e5, d2 = 1e5;
                float2 id1 = 0, id2 = 0;
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 c = cell + float2(x, y);
                        float d = distance(p, c + Hash2(c));
                        if (d < d1) { d2 = d1; id2 = id1; d1 = d; id1 = c; }
                        else if (d < d2) { d2 = d; id2 = c; }
                    }
                }
                float edgePixels = (d2 - d1) * _CrackCell;
                return edgePixels < _CrackWidth && Hash(id1 + id2 * 7.13) < _CrackAmount;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                if (color.a <= 0.001 || input.positionWS.y > _StoneLine)
                    return color;

                half3 srgb = LinearToSRGB(color.rgb);
                float luminance = dot(srgb, float3(0.3, 0.59, 0.11));
                // Ombre noire dessinée sous les pieds : reste telle quelle
                if (luminance <= _ShadowThreshold)
                    return color;

                float2 pixel = floor((input.positionWS.xy - _Origin.xy) * 32.0);
                // Front qui monte : la rangée juste sous la ligne
                if (input.positionWS.y > _StoneLine - 1.0 / 32.0)
                    return half4(_FrontColor.rgb * input.color.rgb, color.a);

                if (luminance < 0.16)
                    return half4(_OutlineColor.rgb * input.color.rgb, color.a);
                if (Crack(pixel))
                    return half4(_CrackColor.rgb * input.color.rgb, color.a);

                float l = luminance + (Hash(pixel) - 0.5) * _Grain;
                half3 stone = l < 0.38 ? _DarkColor.rgb : l < 0.58 ? _MidColor.rgb : l < 0.78 ? _LightColor.rgb : _HighColor.rgb;
                // Teinte du SpriteRenderer gardée (clignotement rouge des coups)
                return half4(stone * input.color.rgb, color.a);
            }
            ENDHLSL
        }
    }
}
