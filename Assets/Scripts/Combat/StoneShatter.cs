using System.Collections.Generic;
using UnityEngine;

namespace DBZ.Combat
{
    // Pierre qui éclate (2026-11-26, Dabura) : fin de la pétrification de Goku, mort de Dabura.
    // Éclats (images transfo_cailloux_eclat) jetés sur le corps et petits cailloux (transfo_cailloux_rocher) qui sautent
    // sur le côté, retombent au niveau des pieds puis s'effacent. Tous passés au matériau « pierre » (gris).
    // Objet indépendant : vit seul, même si le personnage est détruit
    public class StoneShatter : MonoBehaviour
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("Images d'un éclat (transfo_cailloux_eclat_0…3)")]
            public Sprite[] shardFrames;
            [Tooltip("Images d'un caillou : 0 en l'air, 1 retombé (transfo_cailloux_rocher_1, _3)")]
            public Sprite[] rockFrames;
            [Tooltip("Matériau « pierre » (Shaders/Stone.mat) : éclats et cailloux en gris")]
            public Material stoneMaterial;
            public int shardCount = 6;
            public float shardFramesPerSecond = 12f;
            [Tooltip("Départs des éclats étalés sur ce temps (secondes)")]
            public float shardSpread = 0.15f;
            public int rockCount = 3;
            [Tooltip("Distance de saut des cailloux, en pixels sur le côté (de ... à ...)")]
            public Vector2 rockDistance = new Vector2(8f, 18f);
            public float rockJumpPixels = 10f;
            public float rockFlightDuration = 0.35f;
            public float rockLandedDuration = 0.4f;
            public float rockFadeDuration = 0.3f;
        }

        private struct Shard { public float start; public SpriteRenderer renderer; }
        private struct Rock { public Vector2 from; public Vector2 to; public SpriteRenderer renderer; }

        private const float PixelsPerUnit = 32f;

        private Settings settings;
        private Material material;
        private float startTime;
        private readonly List<Shard> shards = new List<Shard>();
        private readonly List<Rock> rocks = new List<Rock>();

        // body : zone du corps (monde) où naissent éclats et cailloux ; feetY : sol où retombent les cailloux
        public static void Spawn(Settings settings, Rect body, float feetY, string sortingLayer, int sortingOrder)
        {
            if (settings == null)
                return;
            var go = new GameObject("StoneShatter");
            go.AddComponent<StoneShatter>().Begin(settings, body, feetY, sortingLayer, sortingOrder);
        }

        private void Begin(Settings s, Rect body, float feetY, string sortingLayer, int sortingOrder)
        {
            settings = s;
            startTime = Time.time;
            if (s.stoneMaterial != null)
                material = new Material(s.stoneMaterial);

            if (s.shardFrames != null && s.shardFrames.Length > 0)
            {
                for (int i = 0; i < s.shardCount; i++)
                {
                    Vector2 pos = Snap(new Vector2(Random.Range(body.xMin, body.xMax), Random.Range(body.yMin, body.yMax)));
                    var sr = NewRenderer("Shard", pos, sortingLayer, sortingOrder + 1);
                    sr.enabled = false;
                    shards.Add(new Shard { start = startTime + Random.Range(0f, s.shardSpread), renderer = sr });
                }
            }
            if (s.rockFrames != null && s.rockFrames.Length > 0)
            {
                for (int i = 0; i < s.rockCount; i++)
                {
                    Vector2 from = new Vector2(Random.Range(body.xMin, body.xMax), Random.Range(body.yMin, body.center.y));
                    float side = i % 2 == 0 ? 1f : -1f;
                    float distance = Random.Range(s.rockDistance.x, s.rockDistance.y) / PixelsPerUnit;
                    Vector2 to = new Vector2(from.x + side * distance, feetY + Random.Range(-3f, 1f) / PixelsPerUnit);
                    var sr = NewRenderer("Rock", Snap(from), sortingLayer, sortingOrder + 1);
                    sr.sprite = s.rockFrames[0];
                    rocks.Add(new Rock { from = from, to = to, renderer = sr });
                }
            }
        }

        private SpriteRenderer NewRenderer(string name, Vector2 position, string sortingLayer, int order)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.transform.position = position;
            sr.sortingLayerName = sortingLayer;
            sr.sortingOrder = order;
            if (material != null)
                sr.sharedMaterial = material;
            return sr;
        }

        private void Update()
        {
            float now = Time.time;
            bool alive = false;

            foreach (var shard in shards)
            {
                if (shard.renderer == null)
                    continue;
                int frame = Mathf.FloorToInt((now - shard.start) * settings.shardFramesPerSecond);
                if (frame < 0)
                {
                    alive = true;
                    continue;
                }
                if (frame >= settings.shardFrames.Length)
                {
                    shard.renderer.enabled = false;
                    continue;
                }
                shard.renderer.enabled = true;
                shard.renderer.sprite = settings.shardFrames[frame];
                alive = true;
            }

            float t = now - startTime;
            float flight = Mathf.Max(0.01f, settings.rockFlightDuration);
            float total = flight + settings.rockLandedDuration + settings.rockFadeDuration;
            foreach (var rock in rocks)
            {
                if (t < flight)
                {
                    float k = t / flight;
                    Vector2 pos = Vector2.Lerp(rock.from, rock.to, k);
                    pos.y += 4f * k * (1f - k) * settings.rockJumpPixels / PixelsPerUnit;
                    rock.renderer.transform.position = Snap(pos);
                }
                else
                {
                    rock.renderer.transform.position = Snap(rock.to);
                    if (settings.rockFrames.Length > 1)
                        rock.renderer.sprite = settings.rockFrames[settings.rockFrames.Length - 1];
                    float fade = t - flight - settings.rockLandedDuration;
                    if (fade > 0f && settings.rockFadeDuration > 0f)
                    {
                        Color c = rock.renderer.color;
                        c.a = 1f - Mathf.Clamp01(fade / settings.rockFadeDuration);
                        rock.renderer.color = c;
                    }
                }
            }
            if (rocks.Count > 0 && t < total)
                alive = true;

            if (!alive)
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }

        private static Vector2 Snap(Vector2 p)
        {
            return new Vector2(Mathf.Round(p.x * PixelsPerUnit) / PixelsPerUnit, Mathf.Round(p.y * PixelsPerUnit) / PixelsPerUnit);
        }
    }
}
