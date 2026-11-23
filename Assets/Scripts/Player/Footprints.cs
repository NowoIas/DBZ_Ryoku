using System.Collections.Generic;
using DBZ.World;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

namespace DBZ.Player
{
    // Traces de pas (neige, sable…) : quand le joueur marche (au sol) sur une tuile de ces sols, une empreinte est
    // posée tous les quelques pixels, pied gauche puis pied droit, avec un peu de hasard (écart, décalage, forme)
    // pour ne pas faire un motif régulier. Elle reste un moment puis s'efface par grains (images de la planche).
    // Sol = tuile marquée dans son fichier : alpha de Tile.color = 255 − marque (10 neige = 245, 11 sable clair = 244,
    // 12 sable foncé = 243 ; le shader des tilemaps le remet à 1), ou tuile listée dans le GroundMaskSet (bords,
    // plages animées : pixels de sol au pixel près, sans toucher à leur marque d'eau).
    [RequireComponent(typeof(PlayerController))]
    public class Footprints : MonoBehaviour
    {
        // Rangées d'une planche d'empreintes pour une direction : 8 empreintes (0-3 pied gauche, 4-7 pied droit)
        // x étapes d'effacement (0 = entière).
        private const int FootVariants = 4;

        [System.Serializable]
        private class GroundType
        {
            public string name = "Neige";
            [Tooltip("Marque des tuiles de ce sol (alpha de leur couleur = 255 − marque)")]
            public int mark = 10;
            [Tooltip("Images de la planche (8 empreintes x étapes, dans l'ordre de la planche), par direction de marche")]
            public Sprite[] upSprites;
            public Sprite[] downSprites;
            public Sprite[] leftSprites;
            public Sprite[] rightSprites;
        }

        [Tooltip("Le joueur laisse des traces de pas sur les sols marqués")]
        [SerializeField] private bool leavesFootprints = true;
        [Tooltip("Calques de tilemap lus pour trouver le sol (du plus haut affiché au plus bas, tri auto)")]
        [FormerlySerializedAs("snowLayers")]
        [SerializeField] private string[] groundLayers = { "Background", "Decor 1", "Decor 2", "Decor 3" };
        [Tooltip("Pixels de sol des tuiles qui n'en sont qu'en partie (bords de neige, plages) ; tuile marquée absente = sol partout")]
        [FormerlySerializedAs("snowMasks")]
        [SerializeField] private GroundMaskSet groundMasks;
        [Tooltip("Sols qui gardent les traces, chacun avec sa marque et ses images")]
        [SerializeField] private GroundType[] groundTypes;
        [Tooltip("Nombre d'étapes par empreinte (entière + effacements)")]
        [SerializeField] private int stageCount = 6;

        [Header("Pas")]
        [Tooltip("Distance entre deux empreintes (pixels), tirée au hasard entre ces deux valeurs")]
        [SerializeField] private int minStepPixels = 7;
        [SerializeField] private int maxStepPixels = 9;
        [Tooltip("Écart de chaque pied par rapport à la ligne de marche (pixels)")]
        [SerializeField] private int footSpacingPixels = 3;
        [Tooltip("Décalage au hasard de chaque empreinte, sur le côté et dans le sens de la marche (pixels)")]
        [SerializeField] private int jitterPixels = 1;
        [Tooltip("Hauteur des pieds sous le centre de la case (pixels)")]
        [SerializeField] private int feetBelowCenterPixels = 13;
        [Tooltip("Marche vers le haut / bas : traces remontées de tant de pixels")]
        [SerializeField] private int verticalRaisePixels = 3;
        [Tooltip("Distance du centre de l'empreinte à sa pointe / son talon (pixels), vérifiés aussi")]
        [SerializeField] private int footEndPixels = 3;

        [Header("Durée")]
        [Tooltip("Temps avant l'effacement (secondes), tiré au hasard entre ces deux valeurs")]
        [SerializeField] private float minLifetime = 1f;
        [SerializeField] private float maxLifetime = 1.5f;
        [Tooltip("Durée de l'effacement par grains (secondes)")]
        [SerializeField] private float fadeDuration = 0.5f;
        [Tooltip("Nombre maximal d'empreintes affichées (les plus anciennes sont reprises)")]
        [SerializeField] private int maxFootprints = 100;

        [Header("Affichage")]
        [SerializeField] private string sortingLayer = "background";
        [Tooltip("Au-dessus du sol, des Decor 1-3 et des Destructibles (4), sous les zénis (10)")]
        [SerializeField] private int sortingOrder = 5;

        private class Footprint
        {
            public SpriteRenderer renderer;
            public Sprite[] sprites;
            public int firstIndex;
            public float fadeStart;
        }

        private PlayerController controller;
        private readonly List<Footprint> active = new List<Footprint>();
        private readonly Stack<Footprint> pool = new Stack<Footprint>();
        private readonly List<Tilemap> groundMaps = new List<Tilemap>();
        private readonly Dictionary<TileBase, int> tileMarks = new Dictionary<TileBase, int>();
        private bool hasSearchedMaps;
        private Transform root;
        private Vector2 lastPosition;
        private float distancePixels;
        private float nextStepPixels;
        private bool isRightFoot;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
        }

        private void OnEnable()
        {
            lastPosition = transform.position;
            distancePixels = 0f;
        }

        private void OnDestroy()
        {
            if (root != null)
                Destroy(root.gameObject);
        }

        // Après le déplacement du joueur (Update)
        private void LateUpdate()
        {
            Vector2 position = transform.position;
            Vector2 delta = position - lastPosition;
            lastPosition = position;

            UpdateFootprints();

            // Plus d'une case d'un coup = porte / réapparition, pas un pas. Niveau 0 seulement (pas sur les ponts).
            bool isWalking = leavesFootprints && !controller.IsFlying && !controller.IsGhost && controller.Level == 0
                && delta.sqrMagnitude > 0.000001f && delta.sqrMagnitude < 1f;
            if (!isWalking)
                return;

            distancePixels += (Mathf.Abs(delta.x) + Mathf.Abs(delta.y)) * 32f;
            if (distancePixels < nextStepPixels)
                return;
            distancePixels = 0f;
            nextStepPixels = Random.Range(minStepPixels, maxStepPixels + 1);

            bool isHorizontal = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y);
            Vector2 direction = isHorizontal ? new Vector2(Mathf.Sign(delta.x), 0f) : new Vector2(0f, Mathf.Sign(delta.y));
            PlaceFootprint(position, direction);
        }

        private void PlaceFootprint(Vector2 position, Vector2 direction)
        {
            bool rightFoot = isRightFoot;
            isRightFoot = !isRightFoot;

            // Pieds : sous le centre de la case ; côté gauche / droit du corps selon le sens de marche
            float feetY = -feetBelowCenterPixels + (direction.y != 0f ? verticalRaisePixels : 0);
            Vector2 side = new Vector2(direction.y, -direction.x);   // droite du personnage
            float lateral = (rightFoot ? footSpacingPixels : -footSpacingPixels) + Random.Range(-jitterPixels, jitterPixels + 1);
            float along = Random.Range(-jitterPixels, jitterPixels + 1);
            Vector2 offsetPixels = new Vector2(0f, feetY) + side * lateral + direction * along;

            // Au pixel près (le centre de l'image 8x8 tombe sur un coin de pixel)
            Vector2 world = position + offsetPixels / 32f;
            world = new Vector2(Mathf.Round(world.x * 32f) / 32f, Mathf.Round(world.y * 32f) / 32f);
            GroundType ground = FindGroundType(GroundMarkAt(world));
            if (ground == null)
                return;
            // Pointe et talon aussi sur un sol à traces : pas d'empreinte qui déborde sur l'herbe, un rocher…
            Vector2 tip = direction * (footEndPixels / 32f);
            if (GroundMarkAt(world + tip) == 0 || GroundMarkAt(world - tip) == 0)
                return;

            Sprite[] sprites = direction.x > 0f ? ground.rightSprites : direction.x < 0f ? ground.leftSprites
                : direction.y > 0f ? ground.upSprites : ground.downSprites;
            if (sprites == null || sprites.Length < FootVariants * 2 * stageCount)
                return;

            int variant = (rightFoot ? FootVariants : 0) + Random.Range(0, FootVariants);
            Footprint footprint = Take();
            footprint.sprites = sprites;
            footprint.firstIndex = variant * stageCount;
            footprint.fadeStart = Time.time + Random.Range(minLifetime, maxLifetime);
            footprint.renderer.sprite = sprites[footprint.firstIndex];
            footprint.renderer.transform.position = world;
            footprint.renderer.gameObject.SetActive(true);
            active.Add(footprint);
        }

        private void UpdateFootprints()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Footprint footprint = active[i];
                float t = Time.time - footprint.fadeStart;
                if (t < 0f)
                    continue;
                int stage = 1 + Mathf.FloorToInt(t / fadeDuration * (stageCount - 1));
                if (stage >= stageCount)
                {
                    footprint.renderer.gameObject.SetActive(false);
                    active.RemoveAt(i);
                    pool.Push(footprint);
                    continue;
                }
                footprint.renderer.sprite = footprint.sprites[footprint.firstIndex + stage];
            }
        }

        private Footprint Take()
        {
            if (active.Count >= maxFootprints)
            {
                Footprint oldest = active[0];
                active.RemoveAt(0);
                return oldest;
            }
            if (pool.Count > 0)
                return pool.Pop();

            if (root == null)
                root = new GameObject("Footprints").transform;   // dans la scène : effacées au changement de scène
            var go = new GameObject("Footprint");
            go.transform.SetParent(root, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingLayerName = sortingLayer;
            renderer.sortingOrder = sortingOrder;
            return new Footprint { renderer = renderer };
        }

        private GroundType FindGroundType(int mark)
        {
            if (mark == 0 || groundTypes == null)
                return null;
            foreach (GroundType ground in groundTypes)
                if (ground.mark == mark)
                    return ground;
            return null;
        }

        // Marque du sol sous ce point (0 = aucun sol à traces)
        private int GroundMarkAt(Vector2 world)
        {
            // Cherchés une fois (le joueur est propre à chaque scène)
            if (!hasSearchedMaps || (groundMaps.Count > 0 && groundMaps[0] == null))
                FindGroundMaps();
            // Du calque le plus haut au plus bas : le premier pixel visible décide. Pixel vide d'une tuile à masque
            // = on regarde le calque du dessous ; tuile sans marque ni masque = ignorée (calque du dessous).
            foreach (Tilemap map in groundMaps)
            {
                if (map == null)
                    continue;
                Vector3Int cell = map.WorldToCell(world);
                TileBase tile = map.GetTile(cell);
                if (tile == null)
                    continue;
                if (groundMasks != null && groundMasks.TryGetEntry(tile, out GroundMaskSet.Entry entry))
                {
                    Vector2 local = world - (Vector2)map.CellToWorld(cell);
                    int x = Mathf.FloorToInt(local.x * 32f);
                    int y = 31 - Mathf.FloorToInt(local.y * 32f);
                    GroundMaskSet.Pixel pixel = GroundMaskSet.GetPixel(entry, x, y);
                    if (pixel == GroundMaskSet.Pixel.Ground)
                        return entry.mark;
                    if (pixel == GroundMaskSet.Pixel.Solid)
                        return 0;
                    continue;
                }
                int mark = TileMark(tile);
                if (mark != 0)
                    return mark;
            }
            return 0;
        }

        private int TileMark(TileBase tile)
        {
            if (!tileMarks.TryGetValue(tile, out int mark))
            {
                mark = tile is Tile t ? Mathf.RoundToInt((1f - t.color.a) * 255f) : 0;
                if (FindGroundType(mark) == null)
                    mark = 0;
                tileMarks[tile] = mark;
            }
            return mark;
        }

        private void FindGroundMaps()
        {
            hasSearchedMaps = true;
            groundMaps.Clear();
            foreach (Tilemap map in FindObjectsByType<Tilemap>())
                if (System.Array.IndexOf(groundLayers, map.name) >= 0)
                    groundMaps.Add(map);
            // Le plus haut affiché d'abord
            groundMaps.Sort((a, b) => SortKey(b).CompareTo(SortKey(a)));
        }

        private static long SortKey(Tilemap map)
        {
            var renderer = map.GetComponent<TilemapRenderer>();
            if (renderer == null)
                return long.MinValue;
            return (long)SortingLayer.GetLayerValueFromID(renderer.sortingLayerID) * 100000L + renderer.sortingOrder;
        }
    }
}
