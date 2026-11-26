using System.Collections;
using System.Collections.Generic;
using DBZ.World;
using UnityEngine;

namespace DBZ.NPC
{
    // Habitant (NPC) qui accompagne Cell imparfait (2026-11-26, demande util.) : se déplace case par case comme Goku,
    // reste groupé autour de son meneur tant que celui-ci se promène, et ne bouge plus qu'autour de sa place une fois
    // le combat lancé. Cell imparfait peut le saisir (Hold), l'aspirer (images d'absorption, colonnes 5-6 de la planche),
    // le relâcher (coup de Goku) ou finir de l'aspirer : il laisse alors son t-shirt au sol et disparaît.
    // Images posées par ce script (pas d'Animator) : planche 16×32, colonne 0 attente, 1-4 marche, 5-6 absorption.
    // Bloque sa case (enfant « Cell » sur le layer SolidObjects), sans PV : ni Goku ni les ennemis ne le blessent.
    public class Npc : MonoBehaviour
    {
        [Header("Images (7 par direction : 0 attente, 1-4 marche, 5-6 absorption)")]
        [SerializeField] private Sprite[] spritesUp = new Sprite[7];
        [SerializeField] private Sprite[] spritesDown = new Sprite[7];
        [SerializeField] private Sprite[] spritesLeft = new Sprite[7];
        [SerializeField] private Sprite[] spritesRight = new Sprite[7];
        [Tooltip("Images de marche par seconde")]
        [SerializeField] private float walkFramesPerSecond = 8f;
        [Tooltip("Images d'absorption (5 ↔ 6) par seconde")]
        [SerializeField] private float absorbFramesPerSecond = 8f;

        [Header("Déplacement")]
        [Tooltip("Vitesse de marche, en cases par seconde")]
        [SerializeField] private float walkSpeed = 2f;
        [Tooltip("Rattrape son meneur dès qu'il en est plus loin que ça (en cases, sans diagonale)")]
        [SerializeField] private int followDistance = 3;
        [Tooltip("Promenade : distance maximale autour du meneur (ou de sa place pendant le combat), en cases sur chaque axe")]
        [SerializeField] private int wanderRadius = 2;
        [Tooltip("Pause entre deux pas de promenade, en secondes (au hasard entre les deux)")]
        [SerializeField] private Vector2 wanderPause = new Vector2(0.8f, 2.5f);

        [Header("Fin")]
        [Tooltip("T-shirt laissé au sol quand il est aspiré (vide = rien)")]
        [SerializeField] private Sprite shirtSprite;
        [Tooltip("Temps où le t-shirt reste entier, puis temps de son effacement par grains, en secondes")]
        [SerializeField] private float shirtStayDuration = 3f;
        [SerializeField] private float shirtFadeDuration = 1f;
        [Tooltip("Disparition (meneur K.O.) : fondu en secondes")]
        [SerializeField] private float vanishDuration = 0.5f;

        [Tooltip("Layer(s) des objets qui bloquent : cibles, ennemis, autres NPC")]
        [SerializeField] private LayerMask solidObjectsLayer;
        [Tooltip("Collider qui occupe sa case (objet enfant), déplacé sur la case visée pendant un pas")]
        [SerializeField] private Transform cellCollider;
        // Ordre d'affichage (sorting layer « player ») par niveau, comme les ennemis
        [SerializeField] private int[] sortingOrderPerLevel = { 0, 4 };

        private enum State { Free, Held, Absorbing, Gone }

        // NPC présents (cibles de l'absorption) ; se vide seule (OnDisable)
        public static readonly List<Npc> All = new List<Npc>();
        private static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        public Transform Leader { get; set; }
        public int Level => level;
        public Vector3 TargetCell => moveTarget;
        // Peut être saisi par Cell : libre et pas en train de disparaître
        public bool IsAvailable => state == State.Free;

        private SpriteRenderer spriteRenderer;
        private PlayerController player;
        private State state = State.Free;
        private int level;
        private bool isMoving;
        private Vector3 moveTarget;
        private Vector2 facing = Vector2.down;
        private float nextStepTime;
        private float animTime;
        private Vector2Int fightHome;
        private bool hadFight;
        private readonly List<Vector2> path = new List<Vector2>();

        private void OnEnable() => All.Add(this);

        private void OnDisable() => All.Remove(this);

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            var pos = transform.position;
            transform.position = new Vector3(Mathf.Floor(pos.x) + 0.5f, Mathf.Floor(pos.y) + 0.5f, pos.z);
            moveTarget = transform.position;
        }

        private void Start()
        {
            var playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null)
                player = playerObject.GetComponent<PlayerController>();
            SetLevel(LevelMap.LevelAfterEntering(transform.position, 0));
            nextStepTime = Time.time + Random.Range(wanderPause.x, wanderPause.y);
            ShowFrame(0);
        }

        private void SetLevel(int newLevel)
        {
            level = newLevel;
            if (level < sortingOrderPerLevel.Length)
                spriteRenderer.sortingOrder = sortingOrderPerLevel[level];
        }

        private void Update()
        {
            if (state == State.Gone)
                return;
            animTime += Time.deltaTime;

            if (isMoving)
            {
                transform.position = Vector3.MoveTowards(transform.position, moveTarget, walkSpeed * Time.deltaTime);
                if (transform.position == moveTarget)
                {
                    isMoving = false;
                    int newLevel = LevelMap.LevelAfterEntering(moveTarget, level);
                    if (newLevel != level)
                        SetLevel(newLevel);
                }
            }

            if (state == State.Absorbing)
                ShowFrame(5 + Mathf.FloorToInt(animTime * absorbFramesPerSecond) % 2);
            else if (isMoving)
                ShowFrame(1 + Mathf.FloorToInt(animTime * walkFramesPerSecond) % 4);
            else
                ShowFrame(0);

            if (!isMoving && state == State.Free && !SceneTransition.IsTransitioning)
                Decide();

            if (cellCollider != null)
                cellCollider.position = moveTarget;
        }

        // Meneur qui se promène : suivi de près. Combat lancé (meneur qui poursuit Goku) : il reste autour de sa place
        private void Decide()
        {
            if (Time.time < nextStepTime)
                return;
            Vector2Int myCell = CellOf(transform.position);
            bool fighting = Leader != null && Leader.TryGetComponent(out Enemies.EnemyController leader) && leader.IsChasing;
            if (fighting && !hadFight)
                fightHome = myCell;
            hadFight = fighting;

            Vector2Int center = fighting || Leader == null ? (fighting ? fightHome : myCell) : CellOf(Leader.position);
            if (!fighting && Leader != null && Manhattan(myCell, center) > followDistance)
            {
                // Rattrape le meneur (chemin recalculé à chaque pas : il bouge)
                GridPathfinder.FindPathToNeighbor(myCell, level, center, level, 12, 600, path);
                if (path.Count > 0 && TryStep(path[0]))
                    return;
                nextStepTime = Time.time + 0.3f;
                return;
            }

            // Petit pas au hasard sans sortir de la zone
            Vector2Int dir = Directions[Random.Range(0, 4)];
            Vector2Int next = myCell + dir;
            if (Mathf.Abs(next.x - center.x) <= wanderRadius && Mathf.Abs(next.y - center.y) <= wanderRadius)
                TryStep(dir);
            nextStepTime = Time.time + Random.Range(wanderPause.x, wanderPause.y);
        }

        private bool TryStep(Vector2 direction)
        {
            Face(direction);
            if (!CanMove(direction))
                return false;
            moveTarget = transform.position + (Vector3)direction;
            isMoving = true;
            return true;
        }

        private bool CanMove(Vector2 direction)
        {
            if (!CollisionMap.CanMoveOnLevel(transform.position, direction, level))
                return false;
            Vector3 target = transform.position + (Vector3)direction;
            Vector2Int targetCell = CellOf(target);
            if (player != null && (CellOf(player.OccupiedCell) == targetCell || CellOf(player.transform.position) == targetCell))
                return false;
            foreach (var other in All)
            {
                if (other != this && CellOf(other.moveTarget) == targetCell)
                    return false;
            }
            foreach (var hit in Physics2D.OverlapCircleAll(target, 0.3f, solidObjectsLayer))
            {
                if (!hit.transform.IsChildOf(transform) && HeightLevel.IsOnLevel(hit, level))
                    return false;
            }
            return true;
        }

        // --- Appelés par l'absorption de Cell imparfait ---

        // Saisi : il finit son pas puis ne bouge plus
        public void Hold()
        {
            if (state == State.Free)
                state = State.Held;
        }

        // Relâché (coup de Goku, Cell gêné) : il reprend sa vie
        public void Release()
        {
            if (state == State.Held || state == State.Absorbing)
            {
                state = State.Free;
                nextStepTime = Time.time + 0.3f;
            }
        }

        // Absorption : tourné vers Cell, images 5 ↔ 6
        public void BeginAbsorb(Vector2 towardCell)
        {
            if (state == State.Gone)
                return;
            if (isMoving)
            {
                transform.position = moveTarget;
                isMoving = false;
            }
            Face(towardCell);
            state = State.Absorbing;
            animTime = 0f;
        }

        public bool IsSettled => !isMoving;

        // Aspiré jusqu'au bout : t-shirt au sol, NPC retiré
        public void FinishAbsorb()
        {
            if (state == State.Gone)
                return;
            state = State.Gone;
            if (shirtSprite != null)
                GroundShirt.Spawn(shirtSprite, moveTarget, shirtStayDuration, shirtFadeDuration);
            Destroy(gameObject);
        }

        // Meneur K.O. : fondu puis retiré
        public void Vanish()
        {
            if (state == State.Gone)
                return;
            state = State.Gone;
            if (cellCollider != null)
                cellCollider.gameObject.SetActive(false);
            StartCoroutine(FadeOut());
        }

        private IEnumerator FadeOut()
        {
            Color c = spriteRenderer.color;
            for (float t = 0f; t < vanishDuration; t += Time.deltaTime)
            {
                spriteRenderer.color = new Color(c.r, c.g, c.b, c.a * (1f - t / vanishDuration));
                yield return null;
            }
            Destroy(gameObject);
        }

        private void Face(Vector2 direction)
        {
            if (direction != Vector2.zero)
                facing = direction;
        }

        private void ShowFrame(int index)
        {
            Sprite[] sprites = facing.y > 0.5f ? spritesUp : facing.y < -0.5f ? spritesDown : facing.x < 0f ? spritesLeft : spritesRight;
            if (sprites != null && index < sprites.Length && sprites[index] != null)
                spriteRenderer.sprite = sprites[index];
        }

        private static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        private static Vector2Int CellOf(Vector3 world) => new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));
    }
}
