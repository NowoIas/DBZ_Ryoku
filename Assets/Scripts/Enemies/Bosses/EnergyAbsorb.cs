using System.Collections;
using System.Collections.Generic;
using DBZ.Combat;
using DBZ.NPC;
using DBZ.World;
using UnityEngine;

namespace DBZ.Enemies
{
    // Attaque spéciale de Cell imparfait (2026-11-26, demande util.) : pendant le combat, il va vers le NPC libre le plus
    // proche (à Range cases au plus), plante sa queue (planche s1 : image 0 puis 1, puis 1 ↔ 2) et l'aspire pendant
    // absorbDuration : le NPC joue ses images d'absorption, des boules d'énergie remontent de lui vers Cell.
    // Pendant l'aspiration, Cell regagne healRatio de ses PV max par petits morceaux (healTicks, un chiffre vert chacun ;
    // interrompu : il garde ce qu'il a déjà pris, choix util.). Fini : le NPC laisse son t-shirt et disparaît ; Cell prend un bonus de dégâts
    // et de vitesse qui se cumule. Un coup reçu pendant l'absorption l'interrompt : NPC relâché, Cell ne gagne rien.
    // L'Animator est coupé pendant l'absorption (images posées par ce script), rendu à la fin.
    public class EnergyAbsorb : EnemySpecialAttack
    {
        [Header("Approche")]
        [Tooltip("Vitesse de marche vers le NPC, en cases par seconde (× bonus de vitesse)")]
        [SerializeField] private float approachSpeed = 3.5f;
        [Tooltip("Temps maximal pour atteindre le NPC, en secondes ; au-delà, il renonce")]
        [SerializeField] private float maxApproachTime = 4f;

        [Header("Absorption")]
        [Tooltip("Images de la planche s1 par direction : 0 queue levée, 1 coup de queue, 2 queue tendue")]
        [SerializeField] private Sprite[] spritesUp = new Sprite[3];
        [SerializeField] private Sprite[] spritesDown = new Sprite[3];
        [SerializeField] private Sprite[] spritesLeft = new Sprite[3];
        [SerializeField] private Sprite[] spritesRight = new Sprite[3];
        [Tooltip("Durée de l'image 0 (queue levée), en secondes")]
        [SerializeField] private float windupDuration = 0.15f;
        [Tooltip("Images 1 ↔ 2 par seconde pendant l'aspiration")]
        [SerializeField] private float absorbFramesPerSecond = 8f;
        [Tooltip("Durée de l'aspiration, en secondes : le temps qu'a Goku pour l'interrompre")]
        [SerializeField] private float absorbDuration = 2f;

        [Header("Gain par NPC aspiré")]
        [Tooltip("PV rendus au total, en part des PV max, répartis sur toute l'aspiration")]
        [SerializeField, Range(0f, 1f)] private float healRatio = 0.2f;
        [Tooltip("Nombre de morceaux de soin pendant l'aspiration (un chiffre vert chacun)")]
        [SerializeField] private int healTicks = 8;
        [Tooltip("Bonus de dégâts ajouté à chaque NPC (0,15 = +15 %)")]
        [SerializeField] private float damageBonusPerNpc = 0.15f;
        [Tooltip("Bonus de vitesse ajouté à chaque NPC (0,1 = +10 %)")]
        [SerializeField] private float speedBonusPerNpc = 0.1f;
        [Tooltip("Éclat de couleur sur Cell à chaque NPC aspiré")]
        [SerializeField] private Color gainFlashColor = new Color(0.6f, 1f, 0.6f, 1f);
        [SerializeField] private float gainFlashDuration = 0.3f;

        [Header("Énergie aspirée")]
        [Tooltip("Boule d'énergie qui va du NPC à Cell (vide = aucune)")]
        [SerializeField] private Sprite energySprite;
        [Tooltip("Une boule toutes les ... secondes")]
        [SerializeField] private float energyInterval = 0.06f;
        [Tooltip("Durée du trajet d'une boule, en secondes")]
        [SerializeField] private float energyTravelTime = 0.35f;
        [Tooltip("Hauteur de la courbe du trajet, en pixels")]
        [SerializeField] private float energyArcPixels = 10f;
        [Tooltip("Départ : corps du NPC (pixels au-dessus du centre de sa case, ± écart au hasard)")]
        [SerializeField] private Vector2 energyStartPixels = new Vector2(0f, 2f);
        [SerializeField] private float energyStartSpread = 5f;
        [Tooltip("Arrivée : corps de Cell, en pixels au-dessus du centre de sa case")]
        [SerializeField] private Vector2 energyEndPixels = new Vector2(0f, 18f);

        private const float PixelsPerUnit = 32f;
        private static readonly int IdleState = Animator.StringToHash("Idle");
        private static readonly int MoveX = Animator.StringToHash("moveX");
        private static readonly int MoveY = Animator.StringToHash("moveY");
        private static readonly int IsMoving = Animator.StringToHash("isMoving");
        private static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        private EnemyController controller;
        private Health health;
        private SpriteRenderer spriteRenderer;
        private Animator animator;
        private int absorbedCount;
        private bool running;
        private bool interrupted;
        private Npc target;
        private Vector2 lastDirection;
        private int lastTickFrame;
        private readonly List<Vector2> path = new List<Vector2>();
        private readonly List<Vector2Int> avoid = new List<Vector2Int>();

        // Nombre de NPC aspirés (bonus cumulés)
        public int AbsorbedCount => absorbedCount;

        private void Awake()
        {
            controller = GetComponent<EnemyController>();
            health = GetComponent<Health>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            animator = GetComponent<Animator>();
        }

        // Un NPC libre à portée, au même niveau (Goku n'entre pas en compte)
        public override bool CanUse(Vector2Int delta, bool sameLevel)
        {
            return FindNearest() != null;
        }

        private Npc FindNearest()
        {
            Npc best = null;
            int bestDistance = int.MaxValue;
            Vector2Int myCell = CellOf(transform.position);
            foreach (var npc in Npc.All)
            {
                if (npc == null || !npc.IsAvailable || npc.Level != controller.Level)
                    continue;
                int d = Manhattan(CellOf(npc.TargetCell), myCell);
                if (d <= range && d < bestDistance)
                {
                    best = npc;
                    bestDistance = d;
                }
            }
            return best;
        }

        protected override IEnumerator Perform(EnemyController owner)
        {
            target = FindNearest();
            if (target == null)
                yield break;
            running = true;
            interrupted = false;
            target.Hold();
            Tick();

            // 1) Approche : case voisine du NPC (chemin recalculé si une case est prise)
            float start = Time.time;
            avoid.Clear();
            while (true)
            {
                if (IsDead()) { Abort(); yield break; }
                if (target == null) { Cleanup(true); yield break; }
                Vector2Int myCell = CellOf(transform.position);
                Vector2Int npcCell = CellOf(target.TargetCell);
                if (Manhattan(myCell, npcCell) == 1 && target.IsSettled
                    && CollisionMap.CanMoveOnLevel(transform.position, (Vector2)(npcCell - myCell), owner.Level))
                    break;
                if (Time.time - start > maxApproachTime)
                {
                    ReleaseTarget();
                    Cleanup(true);
                    yield break;
                }
                GridPathfinder.FindPathToNeighbor(myCell, owner.Level, npcCell, target.Level, 15, 1500, path, avoid);
                if (path.Count == 0)
                {
                    SetWalking(false);
                    Tick();
                    yield return null;
                    continue;
                }
                Vector2 step = path[0];
                FaceAnimator(step);
                if (!owner.CanDashStep(transform.position, step, owner.Level) || owner.IsPlayerAt(transform.position + (Vector3)step, owner.Level))
                {
                    Vector2Int blocked = myCell + Vector2Int.RoundToInt(step);
                    if (!avoid.Contains(blocked))
                        avoid.Add(blocked);
                    SetWalking(false);
                    Tick();
                    yield return null;
                    continue;
                }
                Vector3 next = transform.position + (Vector3)step;
                owner.MoveDuringSpecial(transform.position, next);
                while (transform.position != next)
                {
                    if (IsDead()) { Abort(); yield break; }
                    Vector3 position = Vector3.MoveTowards(transform.position, next, approachSpeed * owner.SpeedMultiplier * Time.deltaTime);
                    owner.MoveDuringSpecial(position, next);
                    SetWalking(true);
                    Tick();
                    yield return null;
                }
                owner.SnapToTargetCell();
            }
            SetWalking(false);

            // 2) Absorption : queue plantée (0, puis 1 ↔ 2), le NPC joue ses images d'absorption
            Vector2 direction = (Vector2)(CellOf(target.TargetCell) - CellOf(transform.position));
            lastDirection = direction;
            FaceAnimator(direction);
            animator.enabled = false;
            Sprite[] sprites = SpritesFor(direction);
            target.BeginAbsorb(-direction);
            health.Damaged += OnDamaged;
            Vector3 npcPosition = target.TargetCell;
            float nextEnergy = 0f;
            int ticksDone = 0;
            int ticks = Mathf.Max(1, healTicks);
            float t = 0f;
            float total = windupDuration + absorbDuration;
            while (t < total)
            {
                if (IsDead()) { health.Damaged -= OnDamaged; Abort(); yield break; }
                if (interrupted || target == null)
                    break;
                if (t < windupDuration)
                    SetSprite(sprites, 0);
                else
                {
                    int frame = Mathf.FloorToInt((t - windupDuration) * absorbFramesPerSecond);
                    SetSprite(sprites, frame == 0 ? 1 : 1 + frame % 2);
                    // Soin progressif : un morceau à chaque fin de tranche de l'aspiration
                    while (ticksDone < ticks && t - windupDuration >= absorbDuration * (ticksDone + 1) / ticks)
                    {
                        health.Heal(health.Max * healRatio / ticks);
                        ticksDone++;
                    }
                    if (energySprite != null && t >= nextEnergy)
                    {
                        nextEnergy = t + energyInterval;
                        StartCoroutine(Energy(npcPosition));
                    }
                }
                Tick();
                t += Time.deltaTime;
                yield return null;
            }
            health.Damaged -= OnDamaged;

            if (interrupted || target == null)
            {
                ReleaseTarget();
                Cleanup(true);
                yield break;
            }

            // 3) Gain
            target.FinishAbsorb();
            target = null;
            absorbedCount++;
            // Morceaux restants (dernière image un peu courte)
            for (; ticksDone < ticks; ticksDone++)
                health.Heal(health.Max * healRatio / ticks);
            owner.DamageMultiplier = 1f + damageBonusPerNpc * absorbedCount;
            owner.SpeedMultiplier = 1f + speedBonusPerNpc * absorbedCount;
            StartCoroutine(GainFlash());
            Cleanup(true);
        }

        private void OnDamaged(float amount, bool isCritical)
        {
            if (amount > 0f)
                interrupted = true;
        }

        // Boule d'énergie : du corps du NPC jusqu'à Cell, en courbe, accélère en arrivant
        private IEnumerator Energy(Vector3 npcCell)
        {
            var go = new GameObject("AbsorbEnergy");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = energySprite;
            sr.sortingLayerName = spriteRenderer.sortingLayerName;
            sr.sortingOrder = spriteRenderer.sortingOrder + 2;
            Vector3 from = npcCell + (Vector3)((energyStartPixels + Random.insideUnitCircle * energyStartSpread) / PixelsPerUnit);
            float arc = energyArcPixels * Random.Range(0.6f, 1.2f) / PixelsPerUnit;
            for (float t = 0f; t < energyTravelTime; t += Time.deltaTime)
            {
                if (this == null)
                    break;
                Vector3 to = transform.position + (Vector3)(energyEndPixels / PixelsPerUnit);
                float k = t / energyTravelTime;
                k *= k;
                Vector3 p = Vector3.Lerp(from, to, k) + Vector3.up * arc * Mathf.Sin(k * Mathf.PI);
                go.transform.position = new Vector3(Mathf.Round(p.x * PixelsPerUnit) / PixelsPerUnit, Mathf.Round(p.y * PixelsPerUnit) / PixelsPerUnit, 0f);
                yield return null;
            }
            Destroy(go);
        }

        private IEnumerator GainFlash()
        {
            for (float t = 0f; t < gainFlashDuration; t += Time.deltaTime)
            {
                spriteRenderer.color = Color.Lerp(gainFlashColor, Color.white, t / gainFlashDuration);
                yield return null;
            }
            spriteRenderer.color = Color.white;
        }

        private void ReleaseTarget()
        {
            if (target != null)
                target.Release();
            target = null;
        }

        private void Abort()
        {
            ReleaseTarget();
            Cleanup(false);
        }

        // Coroutine coupée de l'extérieur (ballon, étourdissement) : tout est remis en ordre à l'image suivante
        private void LateUpdate()
        {
            if (running && lastTickFrame < Time.frameCount - 1)
            {
                health.Damaged -= OnDamaged;
                controller.SnapToTargetCell();
                ReleaseTarget();
                Cleanup(!IsDead());
            }
        }

        private void OnDisable()
        {
            if (running)
            {
                health.Damaged -= OnDamaged;
                ReleaseTarget();
                Cleanup(false);
            }
        }

        private void Tick() => lastTickFrame = Time.frameCount;

        private bool IsDead() => health == null || health.IsDead;

        private void Cleanup(bool restoreAnimator)
        {
            running = false;
            if (restoreAnimator && animator != null)
            {
                animator.enabled = true;
                if (lastDirection != Vector2.zero)
                    FaceAnimator(lastDirection);
                animator.SetBool(IsMoving, false);
                animator.Play(IdleState, 0, 0f);
            }
        }

        private void SetWalking(bool walking)
        {
            if (animator.enabled)
                animator.SetBool(IsMoving, walking);
        }

        private void FaceAnimator(Vector2 direction)
        {
            lastDirection = direction;
            animator.SetFloat(MoveX, direction.x);
            animator.SetFloat(MoveY, direction.y);
        }

        private Sprite[] SpritesFor(Vector2 direction)
        {
            if (direction.y > 0.5f) return spritesUp;
            if (direction.y < -0.5f) return spritesDown;
            return direction.x < 0f ? spritesLeft : spritesRight;
        }

        private void SetSprite(Sprite[] sprites, int index)
        {
            if (sprites != null && index < sprites.Length && sprites[index] != null)
                spriteRenderer.sprite = sprites[index];
        }

        private static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        private static Vector2Int CellOf(Vector3 world) => new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));
    }
}
