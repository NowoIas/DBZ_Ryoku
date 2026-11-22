using System.Collections.Generic;
using DBZ.Combat;
using DBZ.World;
using UnityEngine;

namespace DBZ.Enemies
{
    // Stase d'un boss (Cell) : à chaque seuil de PV (50 %, 25 %), il s'arrête, ne peut ni attaquer ni prendre de coups,
    // devient translucide et pulse en vert dans une bulle, et invoque des sbires un peu plus loin.
    // La stase finit quand tous les sbires sont K.O. Les PV s'arrêtent pile au seuil (un gros coup ne saute pas une phase)
    [RequireComponent(typeof(Health), typeof(EnemyController))]
    public class BossStasis : MonoBehaviour
    {
        [Header("Seuils")]
        [Tooltip("Parts des PV max qui déclenchent une stase, de la plus haute à la plus basse")]
        [SerializeField] private float[] healthThresholds = { 0.5f, 0.25f };

        [Header("Sbires")]
        [SerializeField] private GameObject minionPrefab;
        [SerializeField] private int minionCount = 5;
        [Tooltip("Distance d'apparition, en pas depuis la case du boss (de ... à ...) : cases atteignables à pied, libres")]
        [SerializeField] private Vector2Int spawnDistance = new Vector2Int(2, 4);
        [Tooltip("Écart entre les sorties de terre de deux sbires, en secondes")]
        [SerializeField] private float minionStagger = 0.15f;
        [Tooltip("Layer(s) des objets qui bloquent une case (cibles, ennemis) : pas d'apparition dessus")]
        [SerializeField] private LayerMask solidObjectsLayer;

        [Header("Apparence pendant la stase")]
        [Tooltip("Animation jouée en boucle pendant la stase (planche stase, de face ; vide = animation normale)")]
        [SerializeField] private Sprite[] stasisFrames;
        [SerializeField] private float stasisFramesPerSecond = 6f;
        [Tooltip("Opacité du boss (1 = opaque)")]
        [Range(0f, 1f)]
        [SerializeField] private float alpha = 0.6f;
        [Tooltip("Teinte atteinte au plus fort de la pulsation")]
        [SerializeField] private Color pulseColor = new Color(0.55f, 1f, 0.55f, 1f);
        [Tooltip("Pulsations par seconde")]
        [SerializeField] private float pulseSpeed = 1.5f;
        [Tooltip("Images de la bulle (vide = pas de bulle)")]
        [SerializeField] private Sprite[] bubbleSprites;
        [SerializeField] private float bubbleFramesPerSecond = 4f;
        [Tooltip("Position du centre de la bulle depuis le pied du boss, en unités (1 = 1 case)")]
        [SerializeField] private Vector2 bubbleOffset = new Vector2(0f, 0.5625f);

        public bool IsInStasis { get; private set; }

        private static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        private Health health;
        private EnemyController controller;
        private SpriteRenderer spriteRenderer;
        private Animator animator;
        private SpriteRenderer bubble;
        private Color normalColor;
        private int nextThreshold;
        private float stasisStart;
        private readonly List<Health> minions = new List<Health>();

        private void Awake()
        {
            health = GetComponent<Health>();
            controller = GetComponent<EnemyController>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            animator = GetComponent<Animator>();
            normalColor = spriteRenderer != null ? spriteRenderer.color : Color.white;
        }

        private void Start()
        {
            // Dans Start : les PV max sont lus par Health.Awake
            SetFloor();
        }

        private void OnEnable()
        {
            health.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            health.Damaged -= OnDamaged;
        }

        private void SetFloor()
        {
            health.MinHealth = nextThreshold < healthThresholds.Length ? healthThresholds[nextThreshold] * health.Max : 0f;
        }

        private void OnDamaged(float amount, bool isCritical)
        {
            if (IsInStasis || health.IsDead || nextThreshold >= healthThresholds.Length)
                return;
            if (health.Current <= healthThresholds[nextThreshold] * health.Max + 0.001f)
                EnterStasis();
        }

        private void EnterStasis()
        {
            IsInStasis = true;
            stasisStart = Time.time;
            nextThreshold++;
            SetFloor();
            health.IsInvulnerable = true;
            controller.IsPaused = true;
            if (HasStasisFrames && animator != null)
                animator.enabled = false;
            ShowBubble(true);
            SpawnMinions();
        }

        private void ExitStasis()
        {
            IsInStasis = false;
            health.IsInvulnerable = false;
            controller.IsPaused = false;
            if (HasStasisFrames && animator != null)
                animator.enabled = true;
            if (spriteRenderer != null)
                spriteRenderer.color = normalColor;
            ShowBubble(false);
        }

        private void Update()
        {
            if (!IsInStasis)
                return;

            // Fin : tous les sbires K.O. (ou disparus)
            bool anyAlive = false;
            foreach (var m in minions)
            {
                if (m != null && !m.IsDead)
                {
                    anyAlive = true;
                    break;
                }
            }
            if (!anyAlive)
            {
                ExitStasis();
                return;
            }

            float t = (Time.time - stasisStart) * pulseSpeed;
            float pulse = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 2f);
            if (spriteRenderer != null)
            {
                Color c = Color.Lerp(normalColor, pulseColor, pulse);
                c.a = alpha;
                spriteRenderer.color = c;
            }

            if (bubble != null)
            {
                bubble.sprite = bubbleSprites[(int)(t / pulseSpeed * bubbleFramesPerSecond) % bubbleSprites.Length];
                bubble.sortingLayerID = spriteRenderer.sortingLayerID;
                bubble.sortingOrder = spriteRenderer.sortingOrder + 1;
            }
        }

        private bool HasStasisFrames => stasisFrames != null && stasisFrames.Length > 0;

        // Après l'Animator (même s'il est réactivé par un autre effet, ballon de Gotenks) : l'image de stase gagne
        private void LateUpdate()
        {
            if (!IsInStasis || !HasStasisFrames || spriteRenderer == null)
                return;
            int frame = (int)((Time.time - stasisStart) * stasisFramesPerSecond) % stasisFrames.Length;
            spriteRenderer.sprite = stasisFrames[frame];
        }

        private void ShowBubble(bool show)
        {
            if (bubbleSprites == null || bubbleSprites.Length == 0)
                return;
            if (bubble == null)
            {
                var go = new GameObject("StasisBubble");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = bubbleOffset;
                bubble = go.AddComponent<SpriteRenderer>();
                bubble.sprite = bubbleSprites[0];
            }
            bubble.gameObject.SetActive(show);
        }

        // Sbires posés sur des cases libres atteignables à pied, à spawnDistance pas du boss, au même niveau.
        // Pas assez de place : cases plus proches puis plus lointaines
        private void SpawnMinions()
        {
            minions.Clear();
            if (minionPrefab == null)
                return;

            int level = controller.Level;
            List<Vector2Int>[] byDistance = FindCellsByDistance(level, spawnDistance.y + 3);
            var chosen = new List<Vector2Int>();
            // Ordre de préférence des distances : la plage voulue, puis plus près, puis plus loin
            var order = new List<int>();
            for (int d = spawnDistance.x; d <= spawnDistance.y; d++) order.Add(d);
            for (int d = spawnDistance.x - 1; d >= 1; d--) order.Add(d);
            for (int d = spawnDistance.y + 1; d < byDistance.Length; d++) order.Add(d);

            // Dans la plage voulue : toutes ses cases mélangées ensemble (sinon tous à 2 cases)
            var inRange = new List<Vector2Int>();
            for (int d = spawnDistance.x; d <= spawnDistance.y && d < byDistance.Length; d++)
                inRange.AddRange(byDistance[d]);
            Shuffle(inRange);
            foreach (var c in inRange)
            {
                if (chosen.Count >= minionCount) break;
                chosen.Add(c);
            }
            foreach (int d in order)
            {
                if (d >= spawnDistance.x && d <= spawnDistance.y) continue;
                if (d >= byDistance.Length) continue;
                Shuffle(byDistance[d]);
                foreach (var c in byDistance[d])
                {
                    if (chosen.Count >= minionCount) break;
                    chosen.Add(c);
                }
            }

            // Sorties de terre décalées, dans un ordre mélangé
            for (int i = 0; i < chosen.Count; i++)
            {
                var cell = chosen[i];
                var go = Instantiate(minionPrefab, new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f), Quaternion.identity);
                if (go.TryGetComponent(out Health h))
                    minions.Add(h);
                if (go.TryGetComponent(out GroundEmergence emergence))
                    emergence.Begin(i * minionStagger);
            }
            if (chosen.Count < minionCount)
                Debug.LogWarning($"{name} : seulement {chosen.Count} sbire(s) sur {minionCount}, pas assez de place autour.");
        }

        // Cases libres atteignables à pied depuis la case du boss, rangées par nombre de pas, au même niveau que lui
        private List<Vector2Int>[] FindCellsByDistance(int startLevel, int maxDistance)
        {
            var result = new List<Vector2Int>[maxDistance + 1];
            for (int i = 0; i <= maxDistance; i++) result[i] = new List<Vector2Int>();

            var start = new Vector3Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y), startLevel);
            var visited = new List<Vector3Int> { start };
            var frontier = new List<Vector3Int> { start };

            Vector2Int playerCell = new Vector2Int(int.MinValue, int.MinValue);
            Vector2Int playerTarget = playerCell;
            var playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null && playerObject.TryGetComponent(out PlayerController player))
            {
                playerCell = Vector2Int.FloorToInt(player.transform.position);
                playerTarget = Vector2Int.FloorToInt(player.OccupiedCell);
            }

            for (int distance = 1; distance <= maxDistance; distance++)
            {
                var next = new List<Vector3Int>();
                foreach (var node in frontier)
                {
                    foreach (var dir in Directions)
                    {
                        Vector3 from = new Vector3(node.x + 0.5f, node.y + 0.5f, 0f);
                        if (!CollisionMap.CanMoveOnLevel(from, (Vector2)dir, node.z))
                            continue;
                        Vector3 center = from + new Vector3(dir.x, dir.y, 0f);
                        var cell = new Vector3Int(node.x + dir.x, node.y + dir.y, LevelMap.LevelAfterEntering(center, node.z));
                        if (visited.Contains(cell))
                            continue;
                        visited.Add(cell);
                        // Un objet qui bloque (punching-ball, autre ennemi) : ni apparition ni passage
                        if (IsBlocked(center, cell.z))
                            continue;
                        next.Add(cell);
                        var flat = new Vector2Int(cell.x, cell.y);
                        if (cell.z == startLevel && flat != playerCell && flat != playerTarget)
                            result[distance].Add(flat);
                    }
                }
                frontier = next;
            }
            return result;
        }

        private bool IsBlocked(Vector3 center, int level)
        {
            foreach (var hit in Physics2D.OverlapCircleAll(center, 0.3f, solidObjectsLayer))
            {
                if (!hit.transform.IsChildOf(transform) && HeightLevel.IsOnLevel(hit, level))
                    return true;
            }
            return false;
        }

        private static void Shuffle(List<Vector2Int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
