// Aura de rage (C17 / C18 enragés), style C choisi par l'util. (2026-11-25) parmi 3 essais faits avec pixelfx
// (effet status/ki-aura, C:\Users\datat\pixelfx) : aura fine collée à la silhouette (rouge clair contre le corps,
// rouge vif, bord rouge sombre) dont le bord ondule et monte, + traits de Ki blancs / rouges qui montent vite le long
// du corps, + quelques étincelles. Entièrement calculé, au pixel : le shader lit l'image affichée du boss (sa
// transparence), donc l'aura suit chaque image (marche, coups). Jamais dessinée sur le boss (posée derrière lui).
// Posée sur un carré centré sur le boss par DBZ.Enemies.BossRage, qui envoie l'image, la taille et l'intensité.
// Essais refusés : 1) liseré fin + petites flammes au-dessus du corps ; 2) grande flamme en bandes ; 3) la même,
// mieux collée à la silhouette.
Shader "DBZ/Rage Aura"
{
    Properties
    {
        _InnerColor("Rouge clair (contre le corps)", Color) = (1, 0.612, 0.549, 1)
        _MidColor("Rouge vif", Color) = (1, 0.29, 0.227, 1)
        _EdgeColor("Bord sombre", Color) = (0.627, 0.063, 0.118, 1)
        _StreakColor("Traits de Ki", Color) = (1, 0.612, 0.549, 1)
        _HotColor("Tête des traits, étincelles", Color) = (1, 1, 1, 1)
        _Radius("Épaisseur de l'aura (pixels)", Range(1, 6)) = 4
        _Wobble("Ondulation du bord (0-1)", Range(0, 1)) = 0.4
        _UpStretch("Étirement vers le haut (1 = comme les côtés)", Range(1, 2.5)) = 1.7
        _DownSquash("Écrasement sous les pieds (1 = comme les côtés)", Range(1, 6)) = 3
        _NoiseSize("Taille des ondulations (pixels)", Range(2, 12)) = 5
        _RiseSpeed("Montée des ondulations (pixels par seconde)", Float) = 20
        _Fps("Changements par seconde", Float) = 12
        _StreakZone("Distance des traits au bord de l'aura (pixels)", Range(1, 6)) = 4
        _StreakShare("Part des colonnes qui portent un trait (0-1)", Range(0, 1)) = 0.3
        _StreakLength("Longueur des traits (pixels, mini / maxi)", Vector) = (3, 6, 0, 0)
        _StreakLife("Durée d'un trait (secondes, mini / maxi)", Vector) = (0.25, 0.45, 0, 0)
        _StreakTravel("Montée d'un trait sur sa vie (pixels)", Float) = 22
        _SparkChance("Étincelles (part des pixels proches, par image)", Range(0, 0.05)) = 0.008

        [HideInInspector] [NoScaleOffset] _MainTex("Image du boss", 2D) = "black" {}
        [HideInInspector] _SpriteRect("Case de l'image dans la planche (pixels)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SpritePivot("Pivot de l'image dans la planche (pixels)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SizePixels("Taille du carre (pixels)", Float) = 64
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

            // Fenêtre de recherche du corps : aura (6 × 1,4 d'ondulation) + zone des traits (6), × étirement vers le haut
            #define SEARCH_SIDE 12
            #define SEARCH_UP 20
            #define SEARCH_DOWN 4

            TEXTURE2D(_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _InnerColor;
                half4 _MidColor;
                half4 _EdgeColor;
                half4 _StreakColor;
                half4 _HotColor;
                float _Radius;
                float _Wobble;
                float _UpStretch;
                float _DownSquash;
                float _NoiseSize;
                float _RiseSpeed;
                float _Fps;
                float _StreakZone;
                float _StreakShare;
                float4 _StreakLength;
                float4 _StreakLife;
                float _StreakTravel;
                float _SparkChance;
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

            // Bruit de valeur lissé (comme tileNoise de pixelfx)
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

            half4 Frag(Varyings input) : SV_Target
            {
                float2 pixel = floor(input.uv * _SizePixels) - _SizePixels * 0.5;
                if (Body(pixel) > 0.5)
                    return half4(0, 0, 0, 0);

                // Distance au corps le plus proche : plus « courte » vers le haut, plus « longue » vers le bas
                float nearest = 99.0;
                for (int dy = -SEARCH_UP; dy <= SEARCH_DOWN; dy++)
                {
                    // dy < 0 : le corps est sous ce pixel (pixel au-dessus du corps)
                    float sy = dy < 0 ? dy / _UpStretch : dy * _DownSquash;
                    if (abs(sy) > nearest)
                        continue;
                    for (int dx = -SEARCH_SIDE; dx <= SEARCH_SIDE; dx++)
                    {
                        float d = sqrt(dx * dx + sy * sy);
                        if (d < nearest && Body(pixel + float2(dx, dy)) > 0.5)
                            nearest = d;
                    }
                }
                if (nearest > 98.0)
                    return half4(0, 0, 0, 0);

                // Temps par à-coups (pas de mouvement lisse en pixel art)
                float time = floor(_Time.y * _Fps) / _Fps;

                // Aura : bord qui ondule, ondulations qui montent
                float n = Noise(float2(pixel.x, pixel.y - time * _RiseSpeed) / _NoiseSize);
                float radius = _Radius * (1.0 - _Wobble * 0.5 + _Wobble * n);
                half4 color = half4(0, 0, 0, 0);
                if (nearest <= radius)
                    color = nearest <= 1.2 ? _InnerColor : (radius - nearest < 1.0 ? _EdgeColor : _MidColor);
                else if (nearest <= radius + _StreakZone)
                {
                    // Traits de Ki : par colonne, un trait qui monte vite puis s'éteint, puis un autre (ou rien)
                    float life = lerp(_StreakLife.x, _StreakLife.y, Hash(float2(pixel.x, 3.1)));
                    float age = time / life + Hash(float2(pixel.x, 7.7));
                    float index = floor(age), u = frac(age);
                    if (Hash(float2(pixel.x, index)) < _StreakShare)
                    {
                        // départ entre les pieds (−14) et le haut du corps (+12), comptés depuis le centre de la case
                        float start = lerp(-14.0, 12.0, Hash(float2(pixel.x, index + 0.5)));
                        float head = start + u * _StreakTravel;
                        float streakLength = floor(lerp(_StreakLength.x, _StreakLength.y + 0.99, Hash(float2(index, pixel.x))));
                        if (pixel.y <= head && pixel.y > head - streakLength)
                            color = u < 0.3 ? _HotColor : _StreakColor;
                    }
                    // Étincelles : un pixel çà et là, une image
                    if (color.a <= 0.0 && Hash(pixel + frac(time * 0.731) * 97.0) < _SparkChance)
                        color = Hash(pixel.yx + time) < 0.5 ? _HotColor : _StreakColor;
                }

                color.a *= saturate(_Intensity);
                return color;
            }
            ENDHLSL
        }
    }
}
