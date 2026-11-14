// Copie du shader "Sprite-Lit-Default" d'URP (17.6) pour les tilemaps, avec en plus :
// EAU : les pixels de la couleur exacte de l'eau (_WaterColor) reçoivent le sillage des rayons qui passent au-dessus.
//   L'eau est reconnue par sa couleur : pas de liste de tuiles, le rivage est suivi au pixel près, sur tous les calques.
//   Les rayons sont envoyés par DBZ.World.WaterRipples (variables globales _WaterBeams / _WaterBeamTimes).
// VENT : sur les tuiles marquées, seules les extrémités du feuillage (les pixels à moins de _WindEdge px du bord
//   de la silhouette) glissent d'1 pixel, régulièrement ; l'intérieur de l'arbre ne bouge pas.
//   Marque = alpha de la couleur de la tuile entre 247 et 254 (sur 255).
//   La marque est dans le fichier de la tuile (Tile.color) : l'arbre bouge partout où il est peint. Le shader remet l'alpha à 1.
// Si URP est mis à jour et que ce shader ne compile plus : repartir du nouveau Sprite-Lit-Default et remettre les blocs "EAU" / "VENT".
Shader "DBZ/Tilemap Effects"
{
    Properties
    {
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0

        [Header(Eau)]
        _WaterColor("Couleur de l'eau (exacte, dans la planche)", Color) = (0.345, 0.659, 0.973, 1)
        _CrestColor("Couleur des vaguelettes", Color) = (0.565, 0.784, 0.973, 1)
        _FoamColor("Couleur du bord de vague face a nous (ecume)", Color) = (0.816, 0.910, 0.973, 1)
        _Flatten("Ecrasement du sol vu de trois quarts (hauteur / largeur)", Range(0.3, 1)) = 0.7
        _WaveSpeed("Vitesse des vagues (pixels par seconde)", Float) = 32
        _WaveLength("Ecart entre deux vagues (pixels)", Float) = 12
        _WaveRange("Distance maximale depuis le rayon (pixels)", Float) = 48
        _WaveDash("Longueur des traits d'une vague (pixels)", Float) = 6
        _WaveStart("Distance de la premiere vague (pixels)", Float) = 6

        [Header(Sillage des projectiles)]
        _WakeRange("Ecartement maximal du sillage (pixels)", Float) = 28
        _WakeDuration("Duree de la vague du sillage (secondes)", Float) = 0.6
        _WakeStart("Distance de depart du sillage (pixels)", Float) = 3

        [Header(Ondes circulaires (explosions))]
        _RingSpacing("Ecart entre deux anneaux (pixels)", Float) = 10

        [Header(Vent dans les feuillages (tuiles marquees))]
        _WindPixels("Decalage des extremites (pixels, 0 = pas de vent)", Range(0, 2)) = 1
        _WindEdge("Longueur des pointes qui bougent (pixels depuis le bord gauche ou droit du feuillage)", Range(1, 6)) = 2
        _WindBranch("Hauteur d'une branche (pixels)", Range(2, 16)) = 6
        _WindShare("Part des branches qui bougent", Range(0, 1)) = 0.5
        _WindSpeed("Vitesse du va-et-vient", Float) = 2.5
        _WindAmount("Part du temps ou l'extremite est decalee", Range(0, 1)) = 0.35
        _WindShadowColor("Couleur d'ombre dessinee dans la tuile (comptee comme du vide)", Color) = (0.251, 0.251, 0.251, 1)

        // Legacy properties. They're here so that materials using this shader can gracefully fallback to the legacy sprite shader.
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex LitVertex
            #pragma fragment LitFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4 color        : COLOR;
                // EAU : position dans le monde (en cases), pour situer le pixel par rapport aux rayons
                float2 waterPos    : TEXCOORD4;
                // VENT : marque de la tuile (0 = aucune, 1 à 8 = feuillage)
                half wind          : TEXCOORD5;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _WaterColor;
                half4 _CrestColor;
                half4 _FoamColor;
                float _Flatten;
                float _WaveSpeed;
                float _WaveLength;
                float _WaveRange;
                float _WaveDash;
                float _WaveStart;
                float _WakeRange;
                float _WakeDuration;
                float _WakeStart;
                float _RingSpacing;
                float _WindPixels;
                float _WindEdge;
                float _WindBranch;
                float _WindShare;
                float _WindSpeed;
                float _WindAmount;
                half4 _WindShadowColor;
            CBUFFER_END

            // EAU : rayons, projectiles et explosions en cours ou tout juste finis (WaterRipples.cs)
            // _WaterBeams : xy = début, zw = fin (monde, en cases) ; explosion : xy = centre
            // _WaterBeamTimes : x = instant du départ, z = 0 place libre / 1 rayon / 2 projectile / 3 explosion, w = 1 si fini ;
            //   y = instant de la fin (rayon), ou instant où le projectile était au point de fin
            // _WaterBeamExtras : x = distance de départ (px), y = distance maximale (px), z = durée (s), w = nombre d'anneaux ;
            //   projectile : y = 0 → réglages _Wake* du matériau (Ki Blast)
            #define WATER_BEAM_COUNT 8
            float4 _WaterBeams[WATER_BEAM_COUNT];
            float4 _WaterBeamTimes[WATER_BEAM_COUNT];
            float4 _WaterBeamExtras[WATER_BEAM_COUNT];

            // Une vague vue de trois quarts (comme l'impact du Masenko) : bourrelet épais et clair sur le bord face à nous (bas),
            // fin sur le bord vu de dos (haut), moyen sur les côtés.
            // behind = distance derrière le front de la vague, n = sens où elle avance (les deux dans le repère du sol),
            // fade = 0 (vague neuve) à 1 (éteinte), random = tirage du trait, gapsNew / gapsOld = part de traits manquants.
            // Renvoie 0 = rien, 0.5 = vaguelette pâle, 1 = vaguelette, 2 = écume
            half CrestShade(float behind, float2 n, float fade, float random, float gapsNew, float gapsOld)
            {
                // Distance au sol parcourue par pixel d'écran dans le sens de la vague : l'épaisseur reste comptée en pixels d'écran
                float scale = length(float2(n.x, n.y / _Flatten));
                float facing = -n.y;
                float thickness = facing > 0.5 ? 3.0 : (facing < -0.5 ? 1.0 : 2.0);
                if (fade >= 0.5)
                    thickness = max(thickness - 1.0, 1.0);
                float pixels = behind / scale;
                if (pixels < 0.0 || pixels >= thickness)
                    return 0;
                // Les côtés se défont en pointillés les premiers, le bord face à nous tient le plus longtemps
                float gaps = lerp(gapsNew, gapsOld, fade) * lerp(0.7, 1.3, abs(n.x)) * (facing > 0.5 ? 0.6 : 1.0);
                if (random < gaps)
                    return 0;
                if (facing > 0.5)
                    return fade < 0.7 && pixels < 2.0 ? 2.0 : 1.0;
                return fade < 0.7 ? 1.0 : 0.5;
            }

            // Force de la vaguelette sur ce pixel (valeurs de CrestShade)
            half WaterCrest(float2 positionWS)
            {
                // Tout est calculé au centre du pixel de la planche (32 px par case) : les vagues avancent pixel par pixel.
                // Repère du sol : la hauteur à l'écran est écrasée (_Flatten), on la remet à plat pour mesurer les distances.
                // Un cercle au sol devient ainsi une ellipse à l'écran
                float2 ground = float2(1.0, 1.0 / _Flatten);
                float2 p = (floor(positionWS * 32.0) + 0.5) * ground;
                half best = 0;
                for (int i = 0; i < WATER_BEAM_COUNT; i++)
                {
                    float4 times = _WaterBeamTimes[i];
                    float age = _Time.y - times.x;
                    if (times.z < 0.5 || age < 0.0)
                        continue;

                    float2 a = _WaterBeams[i].xy * 32.0 * ground;
                    float4 extras = _WaterBeamExtras[i];

                    // Explosion : anneaux qui s'élargissent depuis le centre, vite puis de plus en plus lentement
                    if (times.z > 2.5)
                    {
                        float ringU = age / max(extras.z, 0.01);
                        if (ringU >= 1.0)
                            continue;
                        float2 fromCenter = p - a;
                        float ringD = max(length(fromCenter), 0.001);
                        float ringFront = extras.x + (extras.y - extras.x) * (1.0 - (1.0 - ringU) * (1.0 - ringU));
                        float behind = ringFront - ringD;
                        if (behind < 0.0 || ringD < extras.x)
                            continue;
                        float ringIndex = floor(behind / _RingSpacing);
                        if (ringIndex >= extras.w)
                            continue;
                        // En traits fixes autour du centre (tirage par trait et par anneau)
                        float arc = floor(atan2(fromCenter.y, fromCenter.x) * extras.y / (_WaveDash * 1.5));
                        float ringRandom = frac(sin(dot(float2(arc, ringIndex + times.x), float2(12.9898, 78.233))) * 43758.5453);
                        best = max(best, CrestShade(behind - ringIndex * _RingSpacing, fromCenter / ringD, ringU, ringRandom, 0.05, 0.65));
                        continue;
                    }

                    float2 ab = _WaterBeams[i].zw * 32.0 * ground - a;
                    float len = max(length(ab), 0.001);
                    float2 dir = ab / len;
                    // Position le long du rayon (non bornée : sert aux traits, y compris autour des bouts) et distance au rayon
                    float along = dot(p - a, dir);
                    float2 away = p - a - dir * clamp(along, 0.0, len);
                    float d = max(length(away), 0.001);
                    float2 n = away / d;

                    // Projectile : sillage en V. Une seule vague part de chaque point du trajet au passage du projectile,
                    // s'écarte vite puis ralentit, et s'éteint ; la pointe du V suit le projectile
                    if (times.z > 1.5)
                    {
                        if (along < 0.0 || along > len)
                            continue;
                        float passTime = times.x + (along / len) * (times.y - times.x);
                        // Réglages du tir (Big Bang : part du bord de la boule), sinon ceux du matériau (Ki Blast)
                        float wakeStart = extras.y > 0.0 ? extras.x : _WakeStart;
                        float wakeRange = extras.y > 0.0 ? extras.y : _WakeRange;
                        float wakeDuration = extras.y > 0.0 ? extras.z : _WakeDuration;
                        float u = (_Time.y - passTime) / wakeDuration;
                        if (u < 0.0 || u >= 1.0)
                            continue;
                        float crest = wakeStart + (wakeRange - wakeStart) * (1.0 - (1.0 - u) * (1.0 - u));
                        // En traits (tirage fixe par trait et par côté)
                        float wakeSide = dir.x * (p.y - a.y) - dir.y * (p.x - a.x) > 0.0 ? 1.0 : 2.0;
                        float wakeRandom = frac(sin(dot(float2(floor(along / _WaveDash), wakeSide + times.x), float2(12.9898, 78.233))) * 43758.5453);
                        best = max(best, CrestShade(crest - d, n, u, wakeRandom, 0.1, 0.6));
                        continue;
                    }

                    // Rayon : les vagues partent du rayon et s'éloignent ; après la fin du tir, plus de nouvelle vague :
                    // la zone calme s'élargit depuis le rayon jusqu'à tout effacer
                    float front = min(_WaveStart + age * _WaveSpeed, _WaveRange);
                    float calm = times.w > 0.5 ? (_Time.y - times.y) * _WaveSpeed : 0.0;
                    if (d < _WaveStart + calm || d > front)
                        continue;

                    float phase = d - age * _WaveSpeed;
                    float ring = floor(phase / _WaveLength);
                    // Vague en traits (tirage fixe par trait et par vague)
                    float dash = floor(along / _WaveDash);
                    float random = frac(sin(dot(float2(dash, ring), float2(12.9898, 78.233))) * 43758.5453);
                    best = max(best, CrestShade((ring + 1.0) * _WaveLength - phase, n, d / _WaveRange, random, 0.25, 0.6));
                }
                return best;
            }

            // VENT : vrai si ce point de la planche est du vide autour du feuillage (transparent, ou ombre dessinée dans la tuile)
            bool WindIsOutside(float2 uv)
            {
                half4 c = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0);
                half3 gap = abs(c.rgb - _WindShadowColor.rgb);
                return c.a < 0.5 || max(gap.r, max(gap.g, gap.b)) < 0.02;
            }

            // VENT : décalage du pixel, en pixels vers la droite (0 ou _WindPixels).
            // Seules les pointes bougent : les pixels qui ont du vide à moins de _WindEdge px à gauche ou à droite
            // (le dessus et le dessous des branches, et la jonction avec le tronc, restent en place).
            // L'arbre est découpé en « branches » : des tranches de _WindBranch px de haut (par demi-case en largeur).
            // Seule une part d'entre elles (_WindShare, tirage fixe) bouge ; chacune a son va-et-vient régulier, à son propre moment
            float WindOffset(float2 positionWS, float2 uv, float2 uvPerUnitX)
            {
                float2 pixel = floor(positionWS * 32.0);
                float2 branch = float2(floor(pixel.x / 16.0), floor(pixel.y / max(_WindBranch, 1.0)));
                float pick = frac(sin(dot(branch, float2(12.9898, 78.233))) * 43758.5453);
                if (pick >= _WindShare)
                    return 0.0;
                float delay = frac(sin(dot(branch, float2(39.346, 11.135))) * 24634.6345) * 6.2832;
                if (sin(_Time.y * _WindSpeed + delay) < 1.0 - 2.0 * _WindAmount)
                    return 0.0;
                // Pixel de vide (transparent ou ombre) : il ne change que si une pointe de feuillage vient s'y poser.
                // Sans ça, toute l'ombre dessinée autour de l'arbre glisserait avec la branche
                if (WindIsOutside(uv))
                    return WindIsOutside(uv - uvPerUnitX * (_WindPixels / 32.0)) ? 0.0 : _WindPixels;
                float2 edgeX = uvPerUnitX * (_WindEdge / 32.0);
                return WindIsOutside(uv - edgeX) || WindIsOutside(uv + edgeX) ? _WindPixels : 0.0;
            }

            Varyings LitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                // VENT : la marque est dans l'alpha de la couleur de la tuile (254 à 247 sur 255), remis à 1
                half windMark = round((1.0 - input.color.a) * 255.0);
                if (windMark < 0.5 || windMark > 8.5)
                    windMark = 0.0;
                else
                    input.color.a = 1.0;

                Varyings o = CommonLitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                o.waterPos = TransformObjectToWorld(input.positionOS).xy;
                o.wind = windMark;

                return o;
            }

            // Comme CommonLitFragment (Lit2DCommon.hlsl), avec la couleur de l'eau changée avant l'éclairage
            half4 LitFragment(Varyings input) : SV_Target
            {
                // VENT : l'image de la tuile glisse vers la droite (on lit le pixel de gauche dans la planche).
                // Pas de la planche par unité du monde, tiré des dérivées : juste aussi pour une tuile retournée ou tournée
                float worldStepX = ddx(input.waterPos.x);
                float2 uvPerUnitX = abs(worldStepX) > 0.000001 ? ddx(input.uv) / worldStepX : float2(0.0, 0.0);
                float2 uv = input.uv;
                if (input.wind > 0.5)
                    uv -= uvPerUnitX * (WindOffset(input.waterPos, input.uv, uvPerUnitX) / 32.0);
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                // EAU : seulement les pixels de la couleur exacte de l'eau
                half3 gap = abs(tex.rgb - _WaterColor.rgb);
                if (tex.a > 0.5 && max(gap.r, max(gap.g, gap.b)) < 0.02)
                {
                    half crest = WaterCrest(input.waterPos);
                    tex.rgb = crest > 1.5 ? _FoamColor.rgb : lerp(tex.rgb, _CrestColor.rgb, crest);
                }

                const half4 main = input.color * tex;
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                const half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));

                SurfaceData2D surfaceData;
                InputData2D inputData;

                InitializeSurfaceData(main.rgb, main.a, mask, normalTS, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);

            #if defined(DEBUG_DISPLAY)
                SETUP_DEBUG_TEXTURE_DATA_2D_NO_TS(inputData, input.positionWS, input.positionCS, _MainTex);
                surfaceData.normalWS = input.normalWS;
            #endif

                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "NormalsRendering"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_NORMALS_INPUTS
                float4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
                half4   color           : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START( UnityPerMaterial )
                half4 _Color;
                half4 _WaterColor;
                half4 _CrestColor;
                half4 _FoamColor;
                float _Flatten;
                float _WaveSpeed;
                float _WaveLength;
                float _WaveRange;
                float _WaveDash;
                float _WaveStart;
                float _WakeRange;
                float _WakeDuration;
                float _WakeStart;
                float _RingSpacing;
                float _WindPixels;
                float _WindEdge;
                float _WindBranch;
                float _WindShare;
                float _WindSpeed;
                float _WindAmount;
                half4 _WindShadowColor;
            CBUFFER_END

            Varyings NormalsRenderingVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonNormalsVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;

                return o;
            }

            half4 NormalsRenderingFragment(Varyings input) : SV_Target
            {
                // Setup instancing for SpriteFlip is used in NormalsRenderingShared
                SetUpSpriteInstanceProperties();

                return CommonNormalsFragment(input, input.color);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue"="Transparent" "RenderType"="Transparent"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _WaterColor;
                half4 _CrestColor;
                half4 _FoamColor;
                float _Flatten;
                float _WaveSpeed;
                float _WaveLength;
                float _WaveRange;
                float _WaveDash;
                float _WaveStart;
                float _WakeRange;
                float _WakeDuration;
                float _WakeStart;
                float _RingSpacing;
                float _WindPixels;
                float _WindEdge;
                float _WindBranch;
                float _WindShare;
                float _WindSpeed;
                float _WindAmount;
                half4 _WindShadowColor;
            CBUFFER_END

            Varyings UnlitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color *_Color * unity_SpriteColor;
                return o;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                return CommonUnlitFragment(input, input.color);
            }
            ENDHLSL
        }
    }
}
