using System.Collections.Generic;
using DBZ.Combat;
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

            List<Vector2Int> chosen = SpawnCells.Pick(transform.position, controller.Level, spawnDistance, minionCount, solidObjectsLayer, transform);

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
    }
}
