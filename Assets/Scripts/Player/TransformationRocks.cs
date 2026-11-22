using System.Collections.Generic;
using DBZ.World;
using UnityEngine;

namespace DBZ.Player
{
    // Cailloux qui se soulèvent autour du personnage pendant sa transformation (planche transfo_cailloux) :
    // ils sortent du sol, flottent, puis retombent en éclats à la fin de la transformation et disparaissent.
    // Plus le palier est élevé, plus il y en a. Lancé et arrêté par PlayerController.Transformation.
    // Chaque caillou = 2 objets : le caillou (devant ou derrière le perso selon sa place) et son ombre,
    // toujours sous le perso pour ne jamais passer devant l'aura de la transformation
    public class TransformationRocks : MonoBehaviour
    {
        [System.Serializable]
        public struct StageRocks
        {
            [Tooltip("Nombre de cailloux")]
            public int count;
            [Tooltip("Distance minimale au centre de l'anneau, en pixels")]
            public float minRadiusPixels;
            [Tooltip("Distance maximale au centre de l'anneau, en pixels")]
            public float maxRadiusPixels;
        }

        [Tooltip("Cailloux par palier atteint (1er = SSJ, 2e = SSJ2 / SSJ3, 3e = SSJ4, 4e = Blue)")]
        [SerializeField] private StageRocks[] stages =
        {
            new StageRocks { count = 3, minRadiusPixels = 16, maxRadiusPixels = 22 },
            new StageRocks { count = 5, minRadiusPixels = 16, maxRadiusPixels = 22 },
            new StageRocks { count = 6, minRadiusPixels = 18, maxRadiusPixels = 26 },
            new StageRocks { count = 8, minRadiusPixels = 18, maxRadiusPixels = 26 },
        };

        public enum Mode
        {
            // Cailloux qui se soulèvent, flottent, puis retombent en éclats à la fin (1re version)
            FloatingRocks,
            // Rien pendant la transformation, une gerbe d'éclats à la fin (option A)
            ShardsAtEnd,
            // Petites gerbes d'éclats à tour de rôle pendant toute la transformation, à une place nouvelle à chaque fois
            ShardsDuring,
        }

        [Tooltip("Floating Rocks : cailloux qui flottent puis retombent ; Shards At End : éclats à la fin seulement ; "
            + "Shards During : gerbes d'éclats en continu pendant la transformation (Count = gerbes à la fois)")]
        [SerializeField] private Mode mode = Mode.ShardsDuring;
        [Tooltip("Shards During : pause entre deux gerbes d'une même place, tirée entre ces 2 valeurs (s)")]
        [SerializeField] private Vector2 burstPause = new Vector2(0.05f, 0.3f);

        [Tooltip("Caillou : 0 au sol, 1 et 2 en l'air (flottement), 3 retombé")]
        [SerializeField] private Sprite[] rockFrames = new Sprite[4];
        [Tooltip("Éclats joués quand le caillou retombe")]
        [SerializeField] private Sprite[] shardFrames = new Sprite[4];

        [Tooltip("Centre de l'anneau sous le centre de la case du perso, en pixels (16 = bas de la case, aux pieds)")]
        [SerializeField] private float ringCenterBelowPixels = 16f;
        [Tooltip("Aplatissement vertical de l'anneau (vue de trois quarts)")]
        [SerializeField] private float flatten = 0.7f;
        [Tooltip("Écart d'angle tiré au hasard autour de la place régulière, en degrés")]
        [SerializeField] private float angleJitterDegrees = 14f;
        [Tooltip("Caillou devant le perso s'il est plus bas que (centre de l'anneau + ceci), en pixels")]
        [SerializeField] private float frontThresholdPixels = 8f;

        [Tooltip("Départs étalés entre 0 et cette durée (s)")]
        [SerializeField] private float startSpread = 0.35f;
        [Tooltip("Durée de l'image 0 (caillou qui sort du sol), s")]
        [SerializeField] private float liftDuration = 0.08f;
        [Tooltip("Durée de chaque image du flottement (1 et 2 alternées), s")]
        [SerializeField] private float floatFrameDuration = 0.18f;
        [Tooltip("Chutes étalées entre 0 et cette durée (s) après la fin de la transformation")]
        [SerializeField] private float endSpread = 0.15f;
        [Tooltip("Durée de l'image 3 (caillou retombé), s")]
        [SerializeField] private float landDuration = 0.1f;
        [Tooltip("Images par seconde des éclats")]
        [SerializeField] private float shardFramesPerSecond = 12f;
        [Tooltip("Sécurité : chute forcée après ce temps en l'air (s), si la fin n'est jamais annoncée")]
        [SerializeField] private float maxFloatTime = 6f;

        private const float PixelsPerUnit = 32f;
        // Couleur exacte de l'ombre dessinée sur la planche
        private static readonly Color32 ShadowColor = new Color32(25, 25, 25, 255);

        private class Rock
        {
            public Transform root;
            public SpriteRenderer body;
            public SpriteRenderer shadow;
            public float startTime;
            public float endTime = -1f;
            public float phase;
            public Mode mode;
            // Lancement (sécurité maxFloatTime)
            public float beginTime;
            // Shards During : secteur de l'anneau de cette place, pour tirer une nouvelle position à chaque gerbe
            public float sectorAngle;
            public float minRadius;
            public float maxRadius;
            public Vector3 ringCenter;
            public int level;
            public int playerOrder;
            public bool placed;
        }

        private readonly List<Rock> rocks = new List<Rock>();
        private SpriteRenderer playerRenderer;

        // Images découpées en caillou seul / ombre seule, faites une fois pour toute la partie
        private static readonly Dictionary<Sprite, Sprite> bodyCache = new Dictionary<Sprite, Sprite>();
        private static readonly Dictionary<Sprite, Sprite> shadowCache = new Dictionary<Sprite, Sprite>();

        // Disable Domain Reload : on repart d'images neuves à chaque Play
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            bodyCache.Clear();
            shadowCache.Clear();
        }

        private void Awake()
        {
            playerRenderer = GetComponent<SpriteRenderer>();
        }

        // Fait sortir les cailloux du palier atteint, autour de la case actuelle, au niveau donné
        public void Begin(int stage, int level)
        {
            Clear();
            int index = Mathf.Min(stage, stages.Length) - 1;
            if (index < 0 || stages[index].count <= 0 || rockFrames == null || rockFrames.Length < 4)
                return;
            StageRocks settings = stages[index];

            Vector3 ringCenter = transform.position + Vector3.down * (ringCenterBelowPixels / PixelsPerUnit);
            float baseAngle = Random.value * Mathf.PI * 2f;
            int order = playerRenderer != null ? playerRenderer.sortingOrder : 0;
            string layer = playerRenderer != null ? playerRenderer.sortingLayerName : "player";

            for (int i = 0; i < settings.count; i++)
            {
                if (mode == Mode.ShardsDuring)
                {
                    // Place réservée sur un secteur de l'anneau ; position tirée à chaque gerbe
                    Rock slot = CreateRock(ringCenter, layer, order + 1, order - 2);
                    slot.sectorAngle = baseAngle + i * Mathf.PI * 2f / settings.count;
                    slot.minRadius = settings.minRadiusPixels;
                    slot.maxRadius = settings.maxRadiusPixels;
                    slot.ringCenter = ringCenter;
                    slot.level = level;
                    slot.playerOrder = order;
                    slot.startTime = Time.time + Random.Range(0f, startSpread);
                    rocks.Add(slot);
                    continue;
                }

                float angle = baseAngle + i * Mathf.PI * 2f / settings.count
                    + Random.Range(-angleJitterDegrees, angleJitterDegrees) * Mathf.Deg2Rad;
                float distance = Random.Range(settings.minRadiusPixels, settings.maxRadiusPixels);
                // Au pixel près, comme tout le reste du jeu
                float dx = Mathf.Round(Mathf.Cos(angle) * distance);
                float dy = Mathf.Round(Mathf.Sin(angle) * distance * flatten);
                Vector3 position = ringCenter + new Vector3(dx, dy, 0f) / PixelsPerUnit;

                // Pas de caillou dans l'eau ni dans une case bloquée (montagne, case pleine)
                if (CollisionMap.BlocksLanding(position, level))
                    continue;

                bool inFront = dy < frontThresholdPixels;
                rocks.Add(CreateRock(position, layer, inFront ? order + 1 : order - 1, order - 2));
            }
        }

        // Fin de la transformation : chaque caillou retombe en éclats (avec un léger décalage) puis disparaît ;
        // Shards During : plus de nouvelle gerbe, celles en cours se finissent
        public void End()
        {
            foreach (var rock in rocks)
                if (rock.endTime < 0f)
                    rock.endTime = rock.mode == Mode.ShardsDuring ? Time.time : Time.time + Random.Range(0f, endSpread);
        }

        private Rock CreateRock(Vector3 position, string layer, int bodyOrder, int shadowOrder)
        {
            var root = new GameObject("TransformationRock").transform;
            root.position = position;
            var rock = new Rock
            {
                root = root,
                body = CreateRenderer(root, "Body", layer, bodyOrder),
                shadow = CreateRenderer(root, "Shadow", layer, shadowOrder),
                startTime = mode == Mode.ShardsAtEnd ? Time.time : Time.time + Random.Range(0f, startSpread),
                beginTime = Time.time,
                phase = Random.value,
                mode = mode,
            };
            return rock;
        }

        // Shards During : nouvelle position au hasard dans le secteur de la place ; faux si elle tombe sur l'eau / un obstacle
        private bool PlaceInSector(Rock rock)
        {
            float angle = rock.sectorAngle + Random.Range(-angleJitterDegrees, angleJitterDegrees) * Mathf.Deg2Rad;
            float distance = Random.Range(rock.minRadius, rock.maxRadius);
            float dx = Mathf.Round(Mathf.Cos(angle) * distance);
            float dy = Mathf.Round(Mathf.Sin(angle) * distance * flatten);
            Vector3 position = rock.ringCenter + new Vector3(dx, dy, 0f) / PixelsPerUnit;
            if (CollisionMap.BlocksLanding(position, rock.level))
                return false;
            rock.root.position = position;
            rock.body.sortingOrder = dy < frontThresholdPixels ? rock.playerOrder + 1 : rock.playerOrder - 1;
            return true;
        }

        private static SpriteRenderer CreateRenderer(Transform parent, string name, string layer, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingLayerName = layer;
            renderer.sortingOrder = order;
            return renderer;
        }

        private void Update()
        {
            for (int i = rocks.Count - 1; i >= 0; i--)
            {
                Rock rock = rocks[i];
                Sprite frame = CurrentFrame(rock, out bool finished);
                if (finished)
                {
                    Destroy(rock.root.gameObject);
                    rocks.RemoveAt(i);
                    continue;
                }
                rock.body.sprite = frame != null ? BodyOf(frame) : null;
                rock.shadow.sprite = frame != null ? ShadowOf(frame) : null;
            }
        }

        private Sprite CurrentFrame(Rock rock, out bool finished)
        {
            finished = false;
            float now = Time.time;
            if (rock.endTime < 0f && now - rock.beginTime > maxFloatTime)
                rock.endTime = now;

            if (rock.mode == Mode.ShardsDuring)
            {
                if (now < rock.startTime)
                {
                    // Fin annoncée pendant la pause : plus de nouvelle gerbe
                    if (rock.endTime >= 0f)
                        finished = true;
                    return null;
                }
                if (!rock.placed)
                {
                    rock.placed = true;
                    if (!PlaceInSector(rock))
                    {
                        // Place sur l'eau / un obstacle : on retentera à la gerbe suivante
                        rock.placed = false;
                        rock.startTime = now + Random.Range(burstPause.x, burstPause.y);
                        return null;
                    }
                }
                int frame = Mathf.FloorToInt((now - rock.startTime) * shardFramesPerSecond);
                if (shardFrames != null && frame < shardFrames.Length)
                    return shardFrames[frame];
                // Gerbe finie : la suivante ailleurs dans le secteur, sauf si la transformation est finie
                if (rock.endTime >= 0f)
                {
                    finished = true;
                    return null;
                }
                rock.placed = false;
                rock.startTime = now + Random.Range(burstPause.x, burstPause.y);
                return null;
            }

            if (rock.mode == Mode.ShardsAtEnd)
            {
                if (rock.endTime < 0f || now < rock.endTime)
                    return null;
                int burst = Mathf.FloorToInt((now - rock.endTime) * shardFramesPerSecond);
                if (shardFrames == null || burst >= shardFrames.Length)
                {
                    finished = true;
                    return null;
                }
                return shardFrames[burst];
            }

            if (rock.endTime >= 0f && now >= rock.endTime)
            {
                // Pas encore sorti du sol à la fin : il ne sort pas du tout
                if (rock.endTime <= rock.startTime)
                {
                    finished = true;
                    return null;
                }
                float sinceEnd = now - rock.endTime;
                if (sinceEnd < landDuration)
                    return rockFrames[3];
                int shard = Mathf.FloorToInt((sinceEnd - landDuration) * shardFramesPerSecond);
                if (shardFrames == null || shard >= shardFrames.Length)
                {
                    finished = true;
                    return null;
                }
                return shardFrames[shard];
            }

            float sinceStart = now - rock.startTime;
            if (sinceStart < 0f)
                return null;
            if (sinceStart < liftDuration)
                return rockFrames[0];
            int step = Mathf.FloorToInt((sinceStart - liftDuration) / floatFrameDuration + rock.phase * 2f);
            return rockFrames[1 + step % 2];
        }

        // Retire tout de suite les cailloux (nouvelle transformation, objet désactivé)
        private void Clear()
        {
            foreach (var rock in rocks)
                if (rock.root != null)
                    Destroy(rock.root.gameObject);
            rocks.Clear();
        }

        private void OnDisable()
        {
            Clear();
        }

        // Public : réutilisé par GroundBurst (sortie de terre des sbires)
        public static Sprite BodyOf(Sprite source)
        {
            Split(source);
            return bodyCache[source];
        }

        public static Sprite ShadowOf(Sprite source)
        {
            Split(source);
            return shadowCache[source];
        }

        // Sépare les pixels d'ombre (25,25,25) du caillou dans deux images de même taille et même pivot.
        // Planche en lecture / écriture (Read/Write coché) ; sinon le caillou garde son ombre, sans séparation
        private static void Split(Sprite source)
        {
            if (bodyCache.ContainsKey(source))
                return;
            Texture2D texture = source.texture;
            if (!texture.isReadable)
            {
                Debug.LogWarning($"TransformationRocks : cocher Read/Write sur {texture.name} pour séparer les ombres.");
                bodyCache[source] = source;
                shadowCache[source] = null;
                return;
            }

            Rect rect = source.rect;
            int width = (int)rect.width;
            int height = (int)rect.height;
            Color32[] pixels = texture.GetPixels32();
            var body = new Color32[width * height];
            var shadow = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color32 p = pixels[((int)rect.y + y) * texture.width + (int)rect.x + x];
                    bool isShadow = p.a > 0 && p.r == ShadowColor.r && p.g == ShadowColor.g && p.b == ShadowColor.b;
                    body[y * width + x] = isShadow ? new Color32(0, 0, 0, 0) : p;
                    shadow[y * width + x] = isShadow ? p : new Color32(0, 0, 0, 0);
                }
            }
            Vector2 pivot = new Vector2(source.pivot.x / width, source.pivot.y / height);
            bodyCache[source] = MakeSprite(body, width, height, pivot, source.name + "_caillou");
            shadowCache[source] = MakeSprite(shadow, width, height, pivot, source.name + "_ombre");
        }

        private static Sprite MakeSprite(Color32[] pixels, int width, int height, Vector2 pivot, string name)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            return sprite;
        }
    }
}
