using System.Collections;
using System.Collections.Generic;
using DBZ.Combat;
using DBZ.World;
using UnityEngine;

namespace DBZ.Enemies
{
    // Ennemi de base : se promène autour de son point de départ, rejoint Goku case par case quand il le repère (calcul de chemin :
    // contourne les murs, prend les escaliers), le frappe quand il est sur la case voisine au même niveau
    // et tire quand il est aligné avec lui à distance.
    // Mêmes règles de déplacement que le joueur (calques de collision, niveaux de hauteur, objets qui bloquent)
    [RequireComponent(typeof(Animator), typeof(Health))]
    public class EnemyController : MonoBehaviour
    {
        [Tooltip("Vitesse de marche, en cases par seconde (Goku : 4)")]
        [SerializeField] private float moveSpeed = 3f;

        [Tooltip("Distance (en cases, sans diagonale) à laquelle l'ennemi repère Goku")]
        [SerializeField] private int detectionRange = 5;
        [Tooltip("Distance (en cases) au-delà de laquelle il abandonne la poursuite")]
        [SerializeField] private int giveUpRange = 10;

        [Header("Promenade (quand Goku n'est pas repéré)")]
        [Tooltip("Distance maximale de promenade autour du point de départ, en cases sur chaque axe")]
        [SerializeField] private int wanderRadius = 3;
        [Tooltip("Vitesse de promenade, en cases par seconde")]
        [SerializeField] private float wanderSpeed = 2f;
        [Tooltip("Pause entre deux petites marches, en secondes (tirée au hasard entre ces deux valeurs)")]
        [SerializeField] private Vector2 wanderPause = new Vector2(1f, 3f);
        [Tooltip("Nombre de pas d'une petite marche (tiré au hasard entre ces deux valeurs)")]
        [SerializeField] private Vector2Int wanderSteps = new Vector2Int(1, 3);

        [Header("Calcul de chemin")]
        [Tooltip("Distance maximale de recherche autour de l'ennemi, en cases sur chaque axe")]
        [SerializeField] private int searchRadius = 15;
        [Tooltip("Nombre maximal de cases explorées par calcul (limite le coût avec beaucoup d'ennemis)")]
        [SerializeField] private int maxSearchNodes = 1500;
        [Tooltip("Délai minimal avant de recalculer un chemin quand aucun n'avance, en secondes")]
        [SerializeField] private float repathInterval = 0.5f;
        [Tooltip("Durée pendant laquelle une case trouvée occupée (punching-ball, autre ennemi, rocher) est contournée, en secondes")]
        [SerializeField] private float avoidDuration = 2f;

        [SerializeField] private float meleeDamage = 5f;
        // Moment du coup dans l'animation d'attaque (0 = début, 1 = fin)
        [Range(0f, 1f)]
        [SerializeField] private float meleeHitTime = 0.5f;
        [Tooltip("Pause entre deux coups, en secondes (après la fin de l'animation)")]
        [SerializeField] private float attackCooldown = 0.6f;

        [Tooltip("Projectile tiré quand Goku est aligné à distance (vide = l'ennemi ne tire pas)")]
        [SerializeField] private Projectile projectilePrefab;
        [Tooltip("Distance maximale de tir, en cases (le tir ne part qu'à partir de 2 cases)")]
        [SerializeField] private int rangedRange = 5;
        [Tooltip("Pause entre deux tirs, en secondes")]
        [SerializeField] private float rangedCooldown = 2f;
        // Moment du tir dans l'animation (0 = début, 1 = fin)
        [Range(0f, 1f)]
        [SerializeField] private float rangedHitTime = 0.5f;
        // Distance (en cases) devant l'ennemi où apparaît le projectile
        [SerializeField] private float projectileSpawnOffset = 0.75f;

        [Tooltip("Layer(s) des objets qui bloquent : cibles, autres ennemis, Destructibles")]
        [SerializeField] private LayerMask solidObjectsLayer;
        // Rayon de la zone testée au centre de la case visée
        [SerializeField] private float collisionCheckRadius = 0.3f;

        [Tooltip("Collider qui occupe la case de l'ennemi (objet enfant). Pendant un pas, il est déjà sur la case visée, " +
                 "pour que Goku ne puisse pas y entrer en même temps")]
        [SerializeField] private Transform cellCollider;

        // Ordre d'affichage (sorting layer "player") par niveau de hauteur, comme le joueur
        [SerializeField] private int[] sortingOrderPerLevel = { 0, 4 };

        private static readonly int MoveX = Animator.StringToHash("moveX");
        private static readonly int MoveY = Animator.StringToHash("moveY");
        private static readonly int IsMoving = Animator.StringToHash("isMoving");
        private static readonly int AttackTrigger = Animator.StringToHash("attack");
        private static readonly int RangedAttackTrigger = Animator.StringToHash("rangedAttack");

        private Animator animator;
        private SpriteRenderer spriteRenderer;
        private Health health;
        // Niveau de hauteur vu par les autres (Goku, ses techniques) : tenu à jour par SetLevel
        private HeightLevel heightLevel;
        private PlayerController player;
        private Health playerHealth;

        private int level;
        private bool isMoving;
        private Vector3 moveTarget;
        private bool isAttacking;
        private bool isChasing;
        private float nextAttackTime;
        private float nextRangedTime;
        private Vector2 facing = Vector2.down;

        // Chemin en cours vers Goku (directions des pas restants) et cible pour laquelle il a été calculé
        private readonly List<Vector2> path = new List<Vector2>();
        private Vector2Int pathTargetCell = new Vector2Int(int.MinValue, int.MinValue);
        private int pathTargetLevel;
        private float nextRepathTime;
        // Cases trouvées occupées par un objet en voulant y entrer, contournées jusqu'à avoidUntil
        private readonly List<Vector2Int> avoidCells = new List<Vector2Int>();
        private readonly List<float> avoidUntil = new List<float>();

        // Promenade : point de départ, direction et pas restants de la petite marche en cours, fin de la pause
        private Vector2Int homeCell;
        private Vector2 wanderDirection;
        private int wanderStepsLeft;
        private float nextWanderTime;
        // Vitesse du pas en cours (poursuite ou promenade)
        private float currentSpeed;
        // Ballon à l'image précédente : l'arrêt (Freeze) n'est fait qu'une fois
        private bool wasLocked;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            health = GetComponent<Health>();
            heightLevel = GetComponent<HeightLevel>();

            // Comme le joueur : recentré sur une case pour que les tests de collision tombent juste
            var pos = transform.position;
            transform.position = new Vector3(Mathf.Floor(pos.x) + 0.5f, Mathf.Floor(pos.y) + 0.5f, pos.z);
            moveTarget = transform.position;
            Face(facing);
        }

        // Dans Start : le calque Niveaux doit s'être inscrit avant qu'on l'interroge
        private void Start()
        {
            var playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null)
            {
                player = playerObject.GetComponent<PlayerController>();
                playerHealth = playerObject.GetComponent<Health>();
            }
            SetLevel(LevelMap.LevelAfterEntering(transform.position, 0));
            homeCell = CellOf(transform.position);
            nextWanderTime = Time.time + Random.Range(wanderPause.x, wanderPause.y);
        }

        private void SetLevel(int newLevel)
        {
            level = newLevel;
            if (heightLevel != null)
                heightLevel.Level = level;
            if (spriteRenderer != null && level < sortingOrderPerLevel.Length)
                spriteRenderer.sortingOrder = sortingOrderPerLevel[level];
        }

        private void Update()
        {
            // Transformé en ballon (Volleyball de Gotenks) : ni déplacement ni attaque tant que ça dure
            if (StatusEffects.IsLockedOn(gameObject))
            {
                if (!wasLocked)
                    Freeze();
                wasLocked = true;
                return;
            }
            wasLocked = false;

            if (isMoving)
                Advance(currentSpeed * Time.deltaTime);

            if (!isMoving && !isAttacking && !health.IsDead && player != null && !SceneTransition.IsTransitioning)
                Decide();

            // Le collider occupe la case visée pendant un pas (ou la case actuelle à l'arrêt)
            if (cellCollider != null)
                cellCollider.position = moveTarget;

            animator.SetBool(IsMoving, isMoving);
        }

        private void Decide()
        {
            Vector2Int myCell = CellOf(transform.position);
            // Case où sera Goku (celle qu'il vise s'il est en train de marcher)
            Vector2Int playerCell = CellOf(player.OccupiedCell);
            Vector2Int delta = playerCell - myCell;
            int distance = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);

            // Repérage à vol d'oiseau, quel que soit le niveau (Goku sur le pont au-dessus de lui est repéré).
            // Goku K.O. ou en vol : pas repéré, la poursuite en cours s'arrête (il reprend sa promenade)
            bool playerHidden = (playerHealth != null && playerHealth.IsDead) || player.IsFlying;
            if (!isChasing && distance <= detectionRange && !playerHidden)
                StartChase();
            else if (isChasing && (distance > giveUpRange || playerHidden))
                StopChase();

            if (!isChasing)
            {
                Wander(myCell);
                return;
            }

            bool sameLevel = player.Level == level;
            Vector2 towardPlayer = new Vector2(System.Math.Sign(delta.x), System.Math.Sign(delta.y));

            // Sur la case voisine, au même niveau, sans mur entre eux : il se tourne vers Goku et frappe
            if (sameLevel && distance == 1 && CollisionMap.CanMoveOnLevel(transform.position, towardPlayer, level))
            {
                Face(towardPlayer);
                if (Time.time >= nextAttackTime)
                    StartCoroutine(MeleeAttack());
                return;
            }

            // Aligné avec Goku au même niveau, à distance, ligne dégagée : il tire (pause entre deux tirs respectée)
            if (projectilePrefab != null && sameLevel && (delta.x == 0 || delta.y == 0) && distance <= rangedRange
                && Time.time >= nextRangedTime && HasClearShot(delta))
            {
                Face(towardPlayer);
                StartCoroutine(RangedAttack());
                return;
            }

            // Chemin recalculé seulement si Goku a changé de case ou de niveau, ou si le chemin est fini
            // (au plus toutes les repathInterval secondes quand aucun chemin n'avance)
            bool targetChanged = playerCell != pathTargetCell || player.Level != pathTargetLevel;
            if (targetChanged || (path.Count == 0 && Time.time >= nextRepathTime))
            {
                ForgetOldAvoidCells();
                GridPathfinder.FindPathToNeighbor(myCell, level, playerCell, player.Level, searchRadius, maxSearchNodes, path, avoidCells);
                pathTargetCell = playerCell;
                pathTargetLevel = player.Level;
                nextRepathTime = Time.time + repathInterval;
            }

            if (path.Count == 0)
            {
                // Aucun pas utile (déjà au plus près) : il attend, tourné vers Goku
                Face(Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? new Vector2(towardPlayer.x, 0f) : new Vector2(0f, towardPlayer.y));
                return;
            }

            Vector2 step = path[0];
            Face(step);
            if (!CanMove(step))
            {
                OnStepBlocked(step);
                return;
            }

            path.RemoveAt(0);
            StartStep(step, moveSpeed);
        }

        // Pas du chemin impossible : le chemin est abandonné et recalculé dès que permis (repathInterval).
        // Mur ou sens unique : le chemin ne tient plus (ex. niveau changé). Objet sur la case (punching-ball, autre ennemi,
        // rocher) : la case est contournée pendant avoidDuration. Goku sur la case : on attend, il va bouger
        private void OnStepBlocked(Vector2 step)
        {
            Vector2Int blockedCell = CellOf(transform.position + (Vector3)step);
            bool wall = !CollisionMap.CanMoveOnLevel(transform.position, step, level);
            bool playerThere = blockedCell == CellOf(player.OccupiedCell) || blockedCell == CellOf(player.transform.position);
            if (!wall && playerThere)
                return;

            if (!wall)
            {
                int index = avoidCells.IndexOf(blockedCell);
                if (index < 0)
                {
                    avoidCells.Add(blockedCell);
                    avoidUntil.Add(Time.time + avoidDuration);
                }
                else
                {
                    avoidUntil[index] = Time.time + avoidDuration;
                }
            }
            path.Clear();
        }

        private void ForgetOldAvoidCells()
        {
            for (int i = avoidCells.Count - 1; i >= 0; i--)
            {
                if (Time.time >= avoidUntil[i])
                {
                    avoidCells.RemoveAt(i);
                    avoidUntil.RemoveAt(i);
                }
            }
        }

        private void StartChase()
        {
            isChasing = true;
            // Le chemin de promenade (retour au départ) ne sert plus : calcul vers Goku dès cette décision
            path.Clear();
            pathTargetCell = new Vector2Int(int.MinValue, int.MinValue);
        }

        private void StopChase()
        {
            isChasing = false;
            path.Clear();
            wanderStepsLeft = 0;
            nextWanderTime = Time.time + Random.Range(wanderPause.x, wanderPause.y);
        }

        // Promenade autour du point de départ : petites marches de quelques pas dans une direction au hasard,
        // entrecoupées de pauses. Loin de chez lui (après une poursuite), il rentre par le plus court chemin.
        // Pas de calcul de chemin pendant la promenade, sauf pour rentrer : presque rien à calculer
        private void Wander(Vector2Int myCell)
        {
            if (Time.time < nextWanderTime)
                return;

            if (!IsNearHome(myCell))
            {
                if (path.Count == 0)
                {
                    ForgetOldAvoidCells();
                    GridPathfinder.FindPathTo(myCell, level, homeCell, searchRadius, maxSearchNodes, path, avoidCells);
                    if (path.Count == 0)
                    {
                        // Pas de chemin pour rentrer : il réessaiera plus tard
                        nextWanderTime = Time.time + repathInterval;
                        return;
                    }
                }

                Vector2 homeStep = path[0];
                Face(homeStep);
                if (!CanMove(homeStep))
                {
                    OnStepBlocked(homeStep);
                    // Nouveau calcul au plus tôt dans repathInterval
                    nextWanderTime = Time.time + repathInterval;
                    return;
                }
                path.RemoveAt(0);
                StartStep(homeStep, wanderSpeed);
                return;
            }
            path.Clear();

            // Fin d'une petite marche : pause, puis nouvelle direction au hasard
            if (wanderStepsLeft <= 0)
            {
                wanderStepsLeft = Random.Range(wanderSteps.x, wanderSteps.y + 1);
                wanderDirection = RandomDirection();
                nextWanderTime = Time.time + Random.Range(wanderPause.x, wanderPause.y);
                return;
            }

            // Pas qui sortirait de la zone de promenade, ou case bloquée : la marche s'arrête là
            Vector2Int next = myCell + Vector2Int.RoundToInt(wanderDirection);
            if (!IsNearHome(next) || !CanMove(wanderDirection))
            {
                wanderStepsLeft = 0;
                return;
            }

            wanderStepsLeft--;
            Face(wanderDirection);
            StartStep(wanderDirection, wanderSpeed);
        }

        private bool IsNearHome(Vector2Int cell)
        {
            return Mathf.Abs(cell.x - homeCell.x) <= wanderRadius && Mathf.Abs(cell.y - homeCell.y) <= wanderRadius;
        }

        private static Vector2 RandomDirection()
        {
            switch (Random.Range(0, 4))
            {
                case 0: return Vector2.up;
                case 1: return Vector2.down;
                case 2: return Vector2.left;
                default: return Vector2.right;
            }
        }

        private void StartStep(Vector2 direction, float speed)
        {
            moveTarget = transform.position + (Vector3)direction;
            currentSpeed = speed;
            isMoving = true;
        }

        // Même vérification que le joueur, plus la case de Goku
        private bool CanMove(Vector2 direction)
        {
            if (!CollisionMap.CanMoveOnLevel(transform.position, direction, level))
                return false;

            // Case de Goku, s'il y est au même niveau (sur le pont au-dessus, on passe dessous)
            Vector3 target = transform.position + (Vector3)direction;
            bool playerThere = CellOf(target) == CellOf(player.OccupiedCell) || CellOf(target) == CellOf(player.transform.position);
            if (playerThere && player.Level == LevelMap.LevelAfterEntering(target, level))
                return false;

            // Les autres objets qui bloquent, à son niveau (sauf son propre collider, déjà sur sa case)
            foreach (var hit in Physics2D.OverlapCircleAll(target, collisionCheckRadius, solidObjectsLayer))
            {
                if (!hit.transform.IsChildOf(transform) && HeightLevel.IsOnLevel(hit, level))
                    return false;
            }
            return true;
        }

        // Attaque en cours coupée ; pas en cours : il finit sur la case visée (son collider y est déjà), pour rester sur une case
        private void Freeze()
        {
            StopAllCoroutines();
            isAttacking = false;
            if (isMoving)
            {
                transform.position = moveTarget;
                isMoving = false;
                int newLevel = LevelMap.LevelAfterEntering(moveTarget, level);
                if (newLevel != level)
                    SetLevel(newLevel);
            }
        }

        private void Advance(float distance)
        {
            transform.position = Vector3.MoveTowards(transform.position, moveTarget, distance);
            if (transform.position != moveTarget)
                return;

            isMoving = false;
            int newLevel = LevelMap.LevelAfterEntering(moveTarget, level);
            if (newLevel != level)
                SetLevel(newLevel);
        }

        // Coup au corps à corps sur la case devant
        private IEnumerator MeleeAttack()
        {
            Vector2 target = (Vector2)transform.position + facing;
            yield return PlayAttack(AttackTrigger, meleeHitTime, () =>
            {
                // Goku n'est touché que s'il est encore sur la case visée : s'éloigner à temps esquive le coup
                if (CellOf(player.transform.position) == CellOf(target) && player.Level == level)
                    playerHealth?.TakeDamage(meleeDamage);
            });
            nextAttackTime = Time.time + attackCooldown;
        }

        // Tir droit devant (le projectile touche Goku par son collider, layer Player)
        private IEnumerator RangedAttack()
        {
            yield return PlayAttack(RangedAttackTrigger, rangedHitTime, () =>
            {
                Vector3 spawnPos = transform.position + (Vector3)(facing * projectileSpawnOffset);
                Projectile projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
                projectile.Launch(facing, level);
            });
            nextRangedTime = Time.time + rangedCooldown;
            // Pas de coup au corps à corps juste après un tir
            nextAttackTime = Mathf.Max(nextAttackTime, Time.time + attackCooldown);
        }

        // Même logique que le joueur : on attend l'entrée dans l'état taggé "Attack", puis sa fin ;
        // action est appelée une fois quand l'animation atteint actionTime
        private IEnumerator PlayAttack(int trigger, float actionTime, System.Action action)
        {
            isAttacking = true;
            animator.SetTrigger(trigger);

            float timeout = 0.5f;
            while (!IsInAttackState() && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            while (IsInAttackState() && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
            {
                if (action != null && animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= actionTime)
                {
                    action();
                    action = null;
                }
                yield return null;
            }

            animator.ResetTrigger(trigger);
            isAttacking = false;
        }

        // Rien entre l'ennemi et Goku (aligné) : ni côté bloqué sur les calques de collision, ni objet qui bloque
        private bool HasClearShot(Vector2Int delta)
        {
            Vector2 direction = new Vector2(System.Math.Sign(delta.x), System.Math.Sign(delta.y));
            int distance = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
            Vector3 cell = transform.position;
            int cellLevel = level;
            for (int i = 0; i < distance; i++)
            {
                if (!CollisionMap.CanTechniquePass(cell, direction, cellLevel))
                    return false;
                cell += (Vector3)direction;
                cellLevel = LevelMap.LevelAfterEntering(cell, cellLevel);
                // Case de Goku atteinte : le reste ne compte pas
                if (i < distance - 1 && HeightLevel.FirstOnLevel(Physics2D.OverlapCircleAll(cell, collisionCheckRadius, solidObjectsLayer), cellLevel) != null)
                    return false;
            }
            return true;
        }

        private bool IsInAttackState()
        {
            return animator.GetCurrentAnimatorStateInfo(0).IsTag("Attack");
        }

        private void Face(Vector2 direction)
        {
            facing = direction;
            animator.SetFloat(MoveX, direction.x);
            animator.SetFloat(MoveY, direction.y);
        }

        private static Vector2Int CellOf(Vector3 world)
        {
            return new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));
        }
    }
}
