using System.Collections.Generic;
using DBZ.World;
using UnityEngine;

namespace DBZ.Items
{
    // Pièce de zénis lâchée par un ennemi ou le punching-ball (ZeniDrop) : elle tourne sur place (4 images) jusqu'à ce que
    // Goku arrive sur sa case, ou disparaît au bout de lifetime secondes (en clignotant pendant les blinkDuration dernières).
    // Léger, pour en avoir beaucoup : ni Animator ni collider. Les pièces sont rangées par case (dictionnaire) et Goku ramasse
    // celles de la case où il arrive (CollectAt, une seule recherche par pas). Les pièces sont réutilisées (pool) au lieu
    // d'être détruites puis recréées.
    [RequireComponent(typeof(SpriteRenderer))]
    public class ZeniCoin : MonoBehaviour
    {
        [System.Serializable]
        private struct CoinType
        {
            public int value;
            [Tooltip("Images de l'animation, dans l'ordre")]
            public Sprite[] frames;
        }

        [Tooltip("Une entrée par sorte de pièce : valeur en zénis et images")]
        [SerializeField] private CoinType[] types;
        [SerializeField] private float framesPerSecond = 5f;
        [Tooltip("Durée de vie maximale d'une pièce non ramassée, en secondes")]
        [SerializeField] private float lifetime = 10f;
        [Tooltip("La pièce clignote pendant ces dernières secondes avant de disparaître")]
        [SerializeField] private float blinkDuration = 3f;
        [SerializeField] private float blinkInterval = 0.1f;

        [Header("Chute depuis l'ennemi")]
        [Tooltip("Durée du saut en cloche, du centre de l'ennemi jusqu'à sa place, en secondes (0 = posée directement)")]
        [SerializeField] private float flightDuration = 0.35f;
        [Tooltip("Hauteur du haut de la cloche au-dessus de la ligne de départ → arrivée, en pixels")]
        [SerializeField] private float arcHeightPixels = 12f;
        [Tooltip("Hauteur du petit rebond à l'arrivée, en pixels (0 = pas de rebond)")]
        [SerializeField] private float bounceHeightPixels = 3f;
        [SerializeField] private float bounceDuration = 0.1f;
        [Tooltip("Pendant le saut : au-dessus des personnages (calque player, rang des impacts)")]
        [SerializeField] private int flightSortingRank = 2;

        [Header("Affichage")]
        // Au sol (niveau 0) : sous les personnages, au-dessus du décor ; plus haut : rang 0 de la tranche du niveau
        [SerializeField] private string groundSortingLayer = "background";
        [SerializeField] private int groundSortingOrder = 10;
        [SerializeField] private string upperSortingLayer = "player";

        public int Value { get; private set; }

        // Pièces posées, par case (x, y, niveau de hauteur)
        private static readonly Dictionary<Vector3Int, List<ZeniCoin>> coinsByCell = new Dictionary<Vector3Int, List<ZeniCoin>>();
        // Pièces rangées en attente de réutilisation (désactivées)
        private static readonly Stack<ZeniCoin> pool = new Stack<ZeniCoin>();

        private SpriteRenderer spriteRenderer;
        private Sprite[] frames;
        private Vector3Int cellKey;
        private float age;
        // Saut en cloche : départ, arrivée, temps écoulé ; en l'air = pas encore rangée dans sa case (pas ramassable)
        private Vector3 flightStart;
        private Vector3 flightEnd;
        private float flightTime;
        private bool isFlying;
        private bool isBouncing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            coinsByCell.Clear();
            pool.Clear();
        }

        // Valeurs disponibles (sortes de pièces de ce prefab), pour composer une somme
        public int[] GetValues()
        {
            var values = new int[types.Length];
            for (int i = 0; i < types.Length; i++)
                values[i] = types[i].value;
            return values;
        }

        // Pose une pièce de cette valeur à cette position, rangée dans la case cellKey (x, y, niveau).
        // from donné : la pièce y apparaît et saute en cloche jusqu'à position (ramassable seulement une fois posée)
        public static ZeniCoin Spawn(ZeniCoin prefab, int value, Vector3 position, Vector3Int cellKey, Vector3? from = null)
        {
            ZeniCoin coin = null;
            // Une pièce du pool peut avoir été détruite avec son ancienne scène : on la saute
            while (coin == null && pool.Count > 0)
                coin = pool.Pop();
            if (coin == null)
                coin = Instantiate(prefab);

            coin.transform.position = position;
            coin.Setup(value, cellKey, from);
            coin.gameObject.SetActive(true);
            return coin;
        }

        // Goku arrive sur cette case : il ramasse toutes les pièces qui y sont, à ce niveau
        public static void CollectAt(Vector3Int cellKey)
        {
            if (!coinsByCell.TryGetValue(cellKey, out var coins) || coins.Count == 0)
                return;

            int total = 0;
            // Release retire la pièce de la liste : on part de la fin
            for (int i = coins.Count - 1; i >= 0; i--)
            {
                total += coins[i].Value;
                coins[i].Release();
            }
            ZeniWallet.Add(total);
        }

        private void Setup(int value, Vector3Int key, Vector3? from)
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            Value = value;
            frames = null;
            foreach (var type in types)
            {
                if (type.value == value)
                    frames = type.frames;
            }

            cellKey = key;
            age = 0f;
            spriteRenderer.enabled = true;
            if (frames != null && frames.Length > 0)
                spriteRenderer.sprite = frames[0];

            isBouncing = false;
            isFlying = from.HasValue && flightDuration > 0f;
            if (isFlying)
            {
                flightStart = from.Value;
                flightEnd = transform.position;
                flightTime = 0f;
                transform.position = flightStart;
                spriteRenderer.sortingLayerName = upperSortingLayer;
                spriteRenderer.sortingOrder = key.z * LevelMap.SortingOrdersPerLevel + flightSortingRank;
                return;
            }

            Land();
        }

        // Posée : ordre d'affichage du sol, rangée dans sa case (ramassable), compte à rebours de disparition lancé
        private void Land()
        {
            age = 0f;
            int level = cellKey.z;
            if (level == 0)
            {
                spriteRenderer.sortingLayerName = groundSortingLayer;
                spriteRenderer.sortingOrder = groundSortingOrder;
            }
            else
            {
                spriteRenderer.sortingLayerName = upperSortingLayer;
                spriteRenderer.sortingOrder = level * LevelMap.SortingOrdersPerLevel;
            }

            if (!coinsByCell.TryGetValue(cellKey, out var list))
            {
                list = new List<ZeniCoin>(3);
                coinsByCell[cellKey] = list;
            }
            list.Add(this);

            // Goku déjà arrêté sur cette case (et pas en train d'en partir) : il la ramasse tout de suite
            var playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null && playerObject.TryGetComponent(out PlayerController player) && player.Level == cellKey.z)
            {
                Vector3 occupied = player.OccupiedCell;
                if (Mathf.FloorToInt(occupied.x) == cellKey.x && Mathf.FloorToInt(occupied.y) == cellKey.y)
                    CollectAt(cellKey);
            }
        }

        // Saut en cloche (puis petit rebond) ; positions arrondies au pixel
        private void UpdateFlight()
        {
            flightTime += Time.deltaTime;
            if (isFlying)
            {
                float t = Mathf.Clamp01(flightTime / flightDuration);
                Vector3 position = Vector3.Lerp(flightStart, flightEnd, t);
                position.y += 4f * arcHeightPixels * t * (1f - t) / 32f;
                transform.position = SnapToPixel(position);
                if (t < 1f)
                    return;

                isFlying = false;
                transform.position = flightEnd;
                flightTime = 0f;
                isBouncing = bounceHeightPixels > 0f && bounceDuration > 0f;
                Land();
                return;
            }

            float u = Mathf.Clamp01(flightTime / bounceDuration);
            Vector3 bounce = flightEnd;
            bounce.y += 4f * bounceHeightPixels * u * (1f - u) / 32f;
            transform.position = SnapToPixel(bounce);
            if (u >= 1f)
            {
                isBouncing = false;
                transform.position = flightEnd;
            }
        }

        private static Vector3 SnapToPixel(Vector3 position)
        {
            position.x = Mathf.Round(position.x * 32f) / 32f;
            position.y = Mathf.Round(position.y * 32f) / 32f;
            return position;
        }

        private void Update()
        {
            if (isFlying || isBouncing)
            {
                UpdateFlight();
                // En l'air : la pièce tourne déjà, sans compter dans sa durée de vie
                if (isFlying)
                {
                    if (frames != null && frames.Length > 0)
                        spriteRenderer.sprite = frames[(int)(flightTime * framesPerSecond) % frames.Length];
                    return;
                }
            }

            age += Time.deltaTime;
            if (age >= lifetime)
            {
                Release();
                return;
            }

            if (frames != null && frames.Length > 0)
                spriteRenderer.sprite = frames[(int)(age * framesPerSecond) % frames.Length];

            // Clignotement pendant les dernières secondes
            float blinkStart = lifetime - blinkDuration;
            spriteRenderer.enabled = age < blinkStart || (int)((age - blinkStart) / blinkInterval) % 2 == 0;
        }

        // Ramassée ou expirée : retirée de sa case, désactivée et rangée pour resservir
        private void Release()
        {
            Unregister();
            gameObject.SetActive(false);
            pool.Push(this);
        }

        private void Unregister()
        {
            if (coinsByCell.TryGetValue(cellKey, out var list))
            {
                list.Remove(this);
                if (list.Count == 0)
                    coinsByCell.Remove(cellKey);
            }
        }

        // Détruite avec sa scène (changement de scène) alors qu'elle était posée
        private void OnDestroy()
        {
            if (gameObject.activeSelf)
                Unregister();
        }
    }
}
