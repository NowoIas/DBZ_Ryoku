// Flamme de rage (C17 / C18 enragés), derrière le boss et derrière l'aura fine violette (RageAura).
// Demande util. (2026-11-26) : la grande aura rouge doit bouger entièrement comme une flamme et suivre les mouvements
// du sprite. Remplace l'aura dessinée animée par script (rage_aura.png + DrawnAuraAnimator, gardés mais plus posés).
// Entièrement calculée, au pixel : le shader lit l'image affichée du boss (sa transparence) et en tire une flamme :
// silhouette épaissie, étirée vers le haut, dont le bord est découpé par un bruit qui monte (langues de feu qui
// naissent, s'étirent, se détachent et disparaissent en haut) et qui se balance de gauche à droite.
// Couleurs = celles du dessin de l'util. : 1 px de bordeaux dehors, liseré cramoisi, intérieur bordeaux ; opaques
// (projet en espace linéaire : les couleurs semi-transparentes viraient au beige sur l'herbe).
// Bas coupé au bas de l'ombre du boss. Posée sur un carré centré sur le boss par DBZ.Enemies.BossRage.
// 2026-11-26 (2e version, choix util. option 1) : l'aura dessinée par l'util. (_AuraTex = rage_aura.png) redevient la
// base, par-dessus la flamme calculée : ses rangées ondulent (vagues qui montent, plus fortes en haut), son bord est
// lu un peu plus bas qu'il n'est dessiné (le liseré semble monter), des trous qui montent détachent des morceaux de
// la pointe et des cornes. La flamme calculée ne se voit que là où le boss sort du dessin (coups…).
Shader "DBZ/Rage Flame"
{
    Properties
    {
        _InnerColor("Intérieur", Color) = (0.439, 0.039, 0.086, 1)
        _RimColor("Liseré", Color) = (0.839, 0.086, 0.149, 1)
        _OutlineColor("Contour (1 px dehors)", Color) = (0.439, 0.039, 0.086, 1)
        _Radius("Épaisseur moyenne sur les côtés (pixels)", Range(1, 8)) = 4
        _RimWidth("Épaisseur du liseré (pixels)", Range(0.5, 4)) = 1.5
        _UpStretch("Étirement vers le haut (1 = comme les côtés)", Range(1, 4)) = 2.4
        _DownSquash("Écrasement sous les pieds (1 = comme les côtés)", Range(1, 6)) = 3
        _Tongue("Force des langues de feu (0-1,5)", Range(0, 1.5)) = 1
        _TongueSize("Taille des langues (pixels, largeur / hauteur)", Vector) = (4, 7, 0, 0)
        _RiseSpeed("Montée des flammes (pixels par seconde)", Float) = 28
        _Sway("Balancement gauche / droite (pixels)", Range(0, 4)) = 1.5
        _SwaySize("Hauteur d'une vague de balancement (pixels)", Range(2, 20)) = 9
        _Fps("Changements par seconde", Float) = 12
        _BottomPixels("Bas de la flamme sous le centre (pixels, bas de l'ombre)", Float) = 16

        [NoScaleOffset] _AuraTex("Aura dessinée (bas calé sur le bas de l'ombre, vide = flamme seule)", 2D) = "black" {}
        _ImageSway("Aura dessinée : ondulation gauche / droite (pixels)", Range(0, 6)) = 3
        _ImageSwaySize("Aura dessinée : hauteur d'une vague (pixels)", Range(2, 20)) = 8
        _ImageRise("Aura dessinée : montée du bord (pixels)", Range(0, 8)) = 4
        _ImageRiseSpeed("Aura dessinée : vitesse de montée (pixels par seconde)", Float) = 24
        _ImageErode("Aura dessinée : morceaux détachés en haut (0-1)", Range(0, 1)) = 0.5
        _ImageErodeHeight("Aura dessinée : hauteur de la zone qui se détache (pixels depuis le haut)", Float) = 12

        [HideInInspector] [NoScaleOffset] _MainTex("Image du boss", 2D) = "black" {}
        [HideInInspector] _SpriteRect("Case de l'image dans la planche (pixels)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SpritePivot("Pivot de l'image dans la planche (pixels)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SizePixels("Taille du carre (pixels)", Float) = 96
        [HideInInspector] _Intensity("Intensite", Float) = 1
        [HideInInspector] _Flip("Image retournee", Float) = 0
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

            // Fenêtre de recherche du corps (rayon maxi ≈ 8 × 2,5 de langue ; vers le haut × étirement)
            #define SEARCH_SIDE 12
            #define SEARCH_UP 22
            #define SEARCH_DOWN 4

            TEXTURE2D(_MainTex);
            TEXTURE2D(_AuraTex);
            float4 _AuraTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                half4 _InnerColor;
                half4 _RimColor;
                half4 _OutlineColor;
                float _Radius;
                float _RimWidth;
                float _UpStretch;
                float _DownSquash;
                float _Tongue;
                float4 _TongueSize;
                float _RiseSpeed;
                float _Sway;
                float _SwaySize;
                float _Fps;
                float _BottomPixels;
                float _ImageSway;
                float _ImageSwaySize;
                float _ImageRise;
                float _ImageRiseSpeed;
                float _ImageErode;
                float _ImageErodeHeight;
                float4 _SpriteRect;
                float4 _SpritePivot;
                float _SizePixels;
                float _Intensity;
                float _Flip;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 value)
            {
                return frac(sin(dot(value, float2(12.9898, 78.233))) * 43758.5453);
            }

            float Noise(float2 value)
            {
                float2 cell = floor(value), f = frac(value);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash(cell), b = Hash(cell + float2(1, 0)), c = Hash(cell + float2(0, 1)), d = Hash(cell + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 1 si le boss est dessiné sur ce pixel (compté depuis son pivot), 0 si c'est du vide
            float Body(float2 pixel)
            {
                if (_Flip > 0.5)
                    pixel.x = -1.0 - pixel.x;
                float2 texel = floor(_SpritePivot.xy + pixel);
                if (texel.x < _SpriteRect.x || texel.y < _SpriteRect.y || texel.x >= _SpriteRect.z || texel.y >= _SpriteRect.w)
                    return 0.0;
                return step(0.5, LOAD_TEXTURE2D(_MainTex, int2(texel)).a);
            }

            // Pixel de l'aura dessinée (bas centré sur le bas de l'ombre), transparent hors de l'image
            half4 AuraPixel(float2 pixel)
            {
                float2 texel = float2(pixel.x + floor(_AuraTex_TexelSize.z * 0.5), pixel.y + _BottomPixels);
                if (texel.x < 0 || texel.y < 0 || texel.x >= _AuraTex_TexelSize.z || texel.y >= _AuraTex_TexelSize.w)
                    return half4(0, 0, 0, 0);
                return LOAD_TEXTURE2D(_AuraTex, int2(texel));
            }

            // Aura dessinée qui ondule comme une flamme
            half4 DrawnAura(float2 pixel, float time)
            {
                if (_AuraTex_TexelSize.z < 2.0)
                    return half4(0, 0, 0, 0);
                float height = pixel.y + _BottomPixels;              // 0 = bas de l'image
                float top = _AuraTex_TexelSize.w;
                float rise = time * _ImageRiseSpeed;
                float weight = saturate(height / top * 2.0);          // bas presque fixe, pleine force dès la moitié

                // Rangées qui glissent à gauche / à droite, vagues qui montent
                float sway = (Noise(float2((pixel.y - rise) / _ImageSwaySize, time * 0.8 + 5.0)) - 0.5) * 2.0;
                float2 at = float2(pixel.x + round(sway * _ImageSway * weight), pixel.y);
                // Bord lu plus bas qu'il n'est dessiné : le contour monte (et dépasse un peu en haut)
                float lift = Noise(float2(at.x / 3.0 + 31.0, (at.y - rise) / 4.0));
                at.y -= floor(lift * _ImageRise * weight + 0.5);

                half4 color = AuraPixel(at);
                if (color.a < 0.5)
                    return half4(0, 0, 0, 0);
                // Trous qui montent près du haut : des morceaux de la pointe / des cornes se détachent
                float topShare = saturate((height - (top - _ImageErodeHeight)) / max(1.0, _ImageErodeHeight));
                float hole = Noise(float2(pixel.x / 2.0 + 71.0, (pixel.y - rise * 1.3) / 2.5));
                if (hole > 1.0 - _ImageErode * topShare)
                    return half4(0, 0, 0, 0);
                return half4(color.rgb, 1.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 pixel = floor(input.uv * _SizePixels) - _SizePixels * 0.5;
                if (pixel.y < -_BottomPixels || Body(pixel) > 0.5)
                    return half4(0, 0, 0, 0);

                // Temps par à-coups (pas de mouvement lisse en pixel art)
                float time = floor(_Time.y * _Fps) / _Fps;
                float rise = time * _RiseSpeed;

                // L'aura dessinée d'abord : la flamme calculée ne se voit que là où le boss en sort
                half4 drawn = DrawnAura(pixel, time);
                if (drawn.a > 0.0)
                {
                    drawn.a *= saturate(_Intensity);
                    return drawn;
                }

                // Balancement : vagues horizontales qui montent, plus fortes en haut (le bas reste collé aux pieds)
                float swayWeight = saturate((pixel.y + _BottomPixels) / 28.0);
                float sway = (Noise(float2((pixel.y - rise) / _SwaySize, time * 0.9)) - 0.5) * 2.0 * _Sway * swayWeight;
                float2 q = float2(pixel.x + round(sway), pixel.y);

                // Distance au corps le plus proche : plus « courte » vers le haut, plus « longue » vers le bas
                float nearest = 99.0;
                float nearestUp = 0.0;
                for (int dy = -SEARCH_UP; dy <= SEARCH_DOWN; dy++)
                {
                    // dy < 0 : le corps est sous ce pixel (pixel au-dessus du corps)
                    float sy = dy < 0 ? dy / _UpStretch : dy * _DownSquash;
                    if (abs(sy) > nearest)
                        continue;
                    for (int dx = -SEARCH_SIDE; dx <= SEARCH_SIDE; dx++)
                    {
                        float d = sqrt(dx * dx + sy * sy);
                        if (d < nearest && Body(q + float2(dx, dy)) > 0.5)
                        {
                            nearest = d;
                            nearestUp = max(0.0, -dy);
                        }
                    }
                }
                if (nearest > 98.0)
                    return half4(0, 0, 0, 0);

                // Langues de feu : bruit qui monte (grosses langues + petits détails plus rapides) ; plus marqué en haut
                float big = Noise(float2(q.x / _TongueSize.x, (q.y - rise) / _TongueSize.y));
                float small = Noise(float2(q.x / (_TongueSize.x * 0.5) + 17.0, (q.y - rise * 1.4) / (_TongueSize.y * 0.5)));
                float n = big * 0.7 + small * 0.3;
                float upShare = saturate(nearestUp / 10.0);
                float tongue = _Tongue * (1.0 + 0.6 * upShare);
                float radius = _Radius * max(0.0, 1.0 + tongue * (n - 0.5) * 2.0);

                float margin = radius - nearest;
                if (margin < 0.0)
                    return half4(0, 0, 0, 0);
                half4 color = margin < 1.0 ? _OutlineColor : (margin < 1.0 + _RimWidth ? _RimColor : _InnerColor);
                color.a *= saturate(_Intensity);
                return color;
            }
            ENDHLSL
        }
    }
}
