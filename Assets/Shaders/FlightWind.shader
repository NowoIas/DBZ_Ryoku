// Sillage d'air du joueur qui avance en vol : des traits clairs d'1 px glissent le long des côtés de sa silhouette
// et s'effilent derrière lui. Rien devant lui ni loin sur les côtés. Entièrement calculé, au pixel : le shader lit
// l'image affichée du joueur (sa transparence) pour suivre sa vraie silhouette.
// Posé sur un carré centré sur le joueur par DBZ.Player.FlightWindEffect, qui envoie l'image du joueur, la direction,
// l'intensité et la taille du carré.
// Versions refusées par l'util. : 1) traits de vent répartis dans tout le carré (pas assez concentrés sur le perso) ;
// 2) cône de vitesse (arc devant le joueur + traits à ses extrémités).
Shader "DBZ/Flight Wind"
{
    Properties
    {
        _Color("Couleur des traits", Color) = (1, 1, 1, 0.75)
        _LaneShare("Part des lignes qui portent des traits (0-1)", Range(0, 1)) = 0.5
        _TrailLength("Longueur du sillage derriere le corps (pixels, 32 max)", Range(1, 32)) = 24
        _DashLength("Longueur d'un trait (pixels)", Float) = 6
        _DashPeriod("Ecart entre deux traits d'une meme ligne (pixels)", Float) = 11
        _Speed("Vitesse des traits (pixels par seconde)", Float) = 120

        [HideInInspector] [NoScaleOffset] _MainTex("Image du joueur", 2D) = "black" {}
        [HideInInspector] _SpriteRect("Case de l'image dans la planche (pixels)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SpritePivot("Pivot de l'image dans la planche (pixels)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _Direction("Direction du vol", Vector) = (1, 0, 0, 0)
        [HideInInspector] _Intensity("Intensite", Float) = 1
        [HideInInspector] _SizePixels("Taille du carre (pixels)", Float) = 96
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

            // Nombre de pixels regardés vers l'avant (borne de _TrailLength)
            #define MAX_TRAIL 32

            TEXTURE2D(_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _LaneShare;
                float _TrailLength;
                float _DashLength;
                float _DashPeriod;
                float _Speed;
                float4 _SpriteRect;
                float4 _SpritePivot;
                float4 _Direction;
                float _Intensity;
                float _SizePixels;
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

            // 1 si le joueur est dessiné sur ce pixel (compté depuis son pivot), 0 si c'est du vide
            float Body(float2 pixel)
            {
                float2 texel = floor(_SpritePivot.xy + pixel);
                // Hors de sa case : ce serait une autre image de la planche
                if (texel.x < _SpriteRect.x || texel.y < _SpriteRect.y || texel.x >= _SpriteRect.z || texel.y >= _SpriteRect.w)
                    return 0.0;
                return step(0.5, LOAD_TEXTURE2D(_MainTex, int2(texel)).a);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Pixel du carré, compté depuis son centre (le pivot du joueur)
                float2 pixel = floor(input.uv * _SizePixels) + 0.5 - _SizePixels * 0.5;
                float2 direction = _Direction.xy;
                float2 across = float2(-direction.y, direction.x);

                // Jamais par-dessus le joueur
                if (Body(pixel) > 0.5)
                    return half4(0, 0, 0, 0);

                // On remonte la ligne vers l'avant : le premier pixel qui longe le corps (vide, avec le corps juste à
                // côté) ou qui est le corps lui-même donne la distance « derrière le corps ». Rien trouvé : pas de trait
                // (pixel devant le joueur, ou trop loin de lui).
                float behind = -1.0;
                for (int probeIndex = 0; probeIndex < MAX_TRAIL; probeIndex++)
                {
                    float2 probe = pixel + direction * probeIndex;
                    float touches = max(Body(probe), max(Body(probe + across), Body(probe - across)));
                    if (behind < 0.0 && touches > 0.5 && probeIndex < _TrailLength)
                        behind = probeIndex;
                }
                if (behind < 0.0)
                    return half4(0, 0, 0, 0);

                // Une ligne d'1 px parallèle au vol : seules certaines portent des traits, chacune avec sa longueur
                float lane = floor(dot(pixel, across));
                float isLane = step(Hash(float2(lane, 7.3)), _LaneShare);
                float trail = max(_TrailLength * lerp(0.5, 1.0, Hash(float2(lane, 41.0))), 1.0);

                // Les traits reculent, au pixel
                float along = floor(dot(pixel, direction));
                float slide = along + floor(_Time.y * _Speed) + floor(Hash(float2(lane, 3.7)) * _DashPeriod);
                float local = slide - floor(slide / _DashPeriod) * _DashPeriod;
                float isDash = step(local, _DashLength - 0.5);

                // Le sillage s'efface vers l'arrière, en 3 paliers (pas de dégradé lisse en pixel art)
                float fade = ceil(saturate(1.0 - behind / trail) * 3.0) / 3.0;

                half4 color = _Color;
                color.a *= isLane * isDash * fade * saturate(_Intensity);
                return color;
            }
            ENDHLSL
        }
    }
}
