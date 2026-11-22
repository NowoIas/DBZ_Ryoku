using System.Collections.Generic;
using DBZ.Player;
using UnityEngine;

namespace DBZ.Enemies
{
    // Réglages de la sortie de terre (fissure, éclats, cailloux), portés par GroundEmergence
    [System.Serializable]
    public class GroundBurstSettings
    {
        [Tooltip("Fissure qui s'ouvre, de la plus petite à l'entière (3 images)")]
        public Sprite[] fissureFrames;
        [Tooltip("Éclats (transfo_cailloux_eclat_0…3)")]
        public Sprite[] shardFrames;
        [Tooltip("Cailloux (transfo_cailloux_rocher_0…3 : au sol, en l'air ×2, retombé)")]
        public Sprite[] rockFrames;
        [Tooltip("Décalage de la fissure vers le bas, en pixels (son centre sous les pieds)")]
        public int fissureDropPixels = 8;
        [Tooltip("Durée d'ouverture de la fissure (annonce), en secondes")]
        public float openDuration = 0.4f;
        [Tooltip("Durée pendant laquelle la fissure reste entière après la sortie, en secondes")]
        public float stayDuration = 2.2f;
        [Tooltip("Durée du fondu de la fissure, en secondes")]
        public float fadeDuration = 1f;
        public int rockCount = 3;
        [Tooltip("Distance de chute des cailloux depuis le centre, en pixels (de ... à ...)")]
        public Vector2 rockDistance = new Vector2(12f, 20f);
        public float rockJumpPixels = 14f;
        public float rockFlightDuration = 0.45f;
        [Tooltip("Durée au sol des cailloux retombés, en secondes")]
        public float rockLandedDuration = 0.6f;
        [Tooltip("Éclats de la gerbe à l'éruption (2 petits éclats d'annonce en plus)")]
        public int shardCount = 5;
        public float shardFramesPerSecond = 12f;
        [Tooltip("Aplatissement de la vue (cercle au sol vu en ellipse)")]
        public float flatten = 0.7f;
    }

    // Effet de sol d'une sortie de terre : fissure qui s'ouvre, éclats, cailloux qui sautent et retombent,
    // puis fissure qui s'efface. Objet indépendant du sbire (il peut être détruit avant la fin de l'effet)
    public class GroundBurst : MonoBehaviour
    {
        private const float PixelsPerUnit = 32f;

        private struct Shard { public float start; public Vector2 pixel; public SpriteRenderer renderer; }
        private struct Rock { public float start; public Vector2 from; public Vector2 to; public SpriteRenderer body; public SpriteRenderer shadow; }

        private GroundBurstSettings settings;
        private Vector2Int cell;
        private float startTime;
        private SpriteRenderer fissure;
        private readonly List<Shard> shards = new List<Shard>();
        private readonly List<Rock> rocks = new List<Rock>();

        // Crée l'effet sur une case (coin bas-gauche = cell), au niveau de hauteur donné
        public static GroundBurst Spawn(GroundBurstSettings settings, Vector2Int cell, int level)
        {
            var go = new GameObject("GroundBurst");
            var burst = go.AddComponent<GroundBurst>();
            burst.Init(settings, cell, level);
            return burst;
        }

        private void Init(GroundBurstSettings s, Vector2Int c, int level)
        {
            settings = s;
            cell = c;
            startTime = Time.time;

            // Sol : niveau 0 sous tous les persos (background/20) ; plus haut : juste sous le rang des persos de ce niveau
            string groundLayer = level == 0 ? "background" : "player";
            int groundOrder = level == 0 ? 20 : level * 4 - 1;
            int bodyOrder = level * 4 + 2;

            fissure = CreateRenderer("Fissure", groundLayer, groundOrder);
            fissure.transform.position = WorldOf(new Vector2(16f, 16f + s.fissureDropPixels));

            // 2 petits éclats d'annonce, puis la gerbe à l'éruption
            AddShard(0.08f, new Vector2(Random.Range(-6, 7), Random.Range(-2, 5)), bodyOrder);
            AddShard(0.22f, new Vector2(Random.Range(-6, 7), Random.Range(-2, 5)), bodyOrder);
            for (int i = 0; i < s.shardCount; i++)
                AddShard(s.openDuration + Random.Range(0f, 0.15f), new Vector2(Random.Range(-12, 13), Random.Range(-6, 9)), bodyOrder);

            for (int i = 0; i < s.rockCount; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float distance = Random.Range(s.rockDistance.x, s.rockDistance.y);
                var from = new Vector2(16f, 32f);
                var to = from + new Vector2(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance * s.flatten);
                rocks.Add(new Rock
                {
                    start = s.openDuration + i * 0.05f,
                    from = from,
                    to = to,
                    body = CreateRenderer("Caillou", "player", bodyOrder),
                    shadow = CreateRenderer("Ombre", groundLayer, groundOrder + 1),
                });
            }
            Update();
        }

        private void AddShard(float start, Vector2 offset, int order)
        {
            var r = CreateRenderer("Eclat", "player", order);
            r.transform.position = WorldOf(new Vector2(16f, 30f) + offset);
            shards.Add(new Shard { start = start, pixel = offset, renderer = r });
        }

        private SpriteRenderer CreateRenderer(string objectName, string layer, int order)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sortingLayerName = layer;
            r.sortingOrder = order;
            return r;
        }

        // Pixel dans la case, compté depuis son coin haut-gauche (y vers le bas, comme sur les maquettes) → monde, au pixel
        private Vector3 WorldOf(Vector2 pixel)
        {
            return new Vector3(cell.x + Mathf.Round(pixel.x) / PixelsPerUnit, cell.y + 1f - Mathf.Round(pixel.y) / PixelsPerUnit, 0f);
        }

        private void Update()
        {
            float t = Time.time - startTime;
            var s = settings;

            // Fissure : 3 étapes pendant l'annonce, entière, puis fondu
            if (s.fissureFrames != null && s.fissureFrames.Length > 0)
            {
                int last = s.fissureFrames.Length - 1;
                int frame = t < s.openDuration ? Mathf.Min(last, (int)(t / s.openDuration * s.fissureFrames.Length)) : last;
                fissure.sprite = s.fissureFrames[frame];
                float fadeStart = s.openDuration + GroundEmergence.RiseDelay + GroundEmergence.RiseDuration + s.stayDuration;
                float alpha = t < fadeStart ? 1f : 1f - (t - fadeStart) / Mathf.Max(0.01f, s.fadeDuration);
                fissure.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
                if (alpha <= 0f)
                {
                    Destroy(gameObject);
                    return;
                }
            }

            foreach (var shard in shards)
            {
                int k = Mathf.FloorToInt((t - shard.start) * s.shardFramesPerSecond);
                bool visible = t >= shard.start && s.shardFrames != null && k < s.shardFrames.Length;
                shard.renderer.enabled = visible;
                if (visible)
                    shard.renderer.sprite = s.shardFrames[k];
            }

            foreach (var rock in rocks)
            {
                float tt = t - rock.start;
                bool visible = tt >= 0f && tt < s.rockFlightDuration + s.rockLandedDuration && s.rockFrames != null && s.rockFrames.Length >= 4;
                rock.body.enabled = visible;
                rock.shadow.enabled = visible;
                if (!visible)
                    continue;
                float p = Mathf.Clamp01(tt / s.rockFlightDuration);
                Vector2 ground = Vector2.Lerp(rock.from, rock.to, p);
                float height = tt < s.rockFlightDuration ? 4f * s.rockJumpPixels * p * (1f - p) : 0f;
                Sprite frame = tt < s.rockFlightDuration ? s.rockFrames[(int)(tt * 11f) % 2 == 0 ? 1 : 2] : s.rockFrames[3];
                rock.body.sprite = TransformationRocks.BodyOf(frame);
                rock.shadow.sprite = TransformationRocks.ShadowOf(frame);
                rock.body.transform.position = WorldOf(ground - new Vector2(0f, height));
                rock.shadow.transform.position = WorldOf(ground);
            }
        }
    }
}
