using System.Collections.Generic;
using DBZ.World;
using UnityEngine;

namespace DBZ.Combat
{
    // Poing du dragon de Gogeta (touche R) : le poing part tout droit sur maxDistance cases en laissant une ligne de boules
    // (ballsPerCell par case : une au centre, une de liaison), puis revient vers Gogeta de la même manière, à l'envers.
    // La tête du dragon (image « approche ») mène le poing pendant tout le trajet, les boules la suivent à headClearanceCells ;
    // au bout, elle mord (image « impact ») et frappe la case. Arrêté plus tôt par un ennemi / une cible (impact sur sa case) ou par un mur (impact sur la case d'avant).
    // Images par direction, non tournées
    public class DragonFist : MonoBehaviour
    {
        [Header("Boules du poing, une par direction")]
        [SerializeField] private Sprite ballUp;
        [SerializeField] private Sprite ballDown;
        [SerializeField] private Sprite ballLeft;
        [SerializeField] private Sprite ballRight;

        [Header("Tête du dragon : approche (avant le bout), puis impact")]
        [SerializeField] private Sprite approachUp;
        [SerializeField] private Sprite approachDown;
        [SerializeField] private Sprite approachLeft;
        [SerializeField] private Sprite approachRight;
        [SerializeField] private Sprite impactUp;
        [SerializeField] private Sprite impactDown;
        [SerializeField] private Sprite impactLeft;
        [SerializeField] private Sprite impactRight;

        [Header("Décalages en pixels (x vers la droite, y vers le haut), relus à chaque image")]
        [Tooltip("Décalage de toutes les boules, selon la direction du tir")]
        [SerializeField] private Vector2Int ballUpOffsetPixels = Vector2Int.zero;
        [SerializeField] private Vector2Int ballDownOffsetPixels = Vector2Int.zero;
        [SerializeField] private Vector2Int ballLeftOffsetPixels = Vector2Int.zero;
        [SerializeField] private Vector2Int ballRightOffsetPixels = Vector2Int.zero;
        [Tooltip("Décalage de la tête du dragon depuis le bout du poing, selon la direction du tir")]
        [SerializeField] private Vector2Int headUpOffsetPixels = Vector2Int.zero;
        [SerializeField] private Vector2Int headDownOffsetPixels = Vector2Int.zero;
        [SerializeField] private Vector2Int headLeftOffsetPixels = Vector2Int.zero;
        [SerializeField] private Vector2Int headRightOffsetPixels = Vector2Int.zero;

        [Header("Trajet")]
        [Tooltip("Distance parcourue, en cases")]
        [Min(1)]
        [SerializeField] private int maxDistance = 5;
        [Tooltip("Vitesse à l'aller et au retour (cases par seconde)")]
        [Min(0.1f)]
        [SerializeField] private float cellsPerSecond = 10f;
        [Tooltip("Boules par case : 2 = une au centre de la case, une de liaison entre deux cases")]
        [Min(1)]
        [SerializeField] private int ballsPerCell = 2;
        [Tooltip("Distance de la 1re boule depuis Gogeta, en cases (0,5 = au bord de sa case : elle le recouvre en partie)")]
        [Min(0f)]
        [SerializeField] private float firstBallDistance = 1f;
        [Tooltip("La tête (image d'approche) n'est affichée qu'à partir de cette distance de Gogeta, en cases (plus près, elle le recouvrirait)")]
        [Min(0f)]
        [SerializeField] private float headMinDistance = 1f;
        [Tooltip("Écart entre la tête et la dernière boule, en cases : aucune boule sous la tête (1 = au bout, boules sur 4 cases et tête sur la 5e)")]
        [Min(0f)]
        [SerializeField] private float headClearanceCells = 1f;
        [Tooltip("Durée de l'image d'impact au bout, avant le retour (secondes)")]
        [Min(0f)]
        [SerializeField] private float impactDuration = 0.3f;

        [Header("Dégâts")]
        [SerializeField] private float damage = 40f;
        // Layers qui arrêtent le poing : obstacles et cibles (SolidObjects)
        [SerializeField] private LayerMask hitLayers;
        [Tooltip("Rayon de la zone testée au centre de chaque case (en cases)")]
        [SerializeField] private float hitRadius = 0.3f;
        [SerializeField] private bool canDestroyObstacles = true;

        [Header("Coût et pause")]
        [Min(0f)]
        [SerializeField] private float kiCost = 30f;
        [Tooltip("Pause après le retour du poing, avant de pouvoir le relancer (secondes)")]
        [Min(0f)]
        [SerializeField] private float cooldown = 1.5f;
        [Tooltip("Moment de l'animation de Gogeta (planche s3, 0 = début, 1 = fin) où le poing part")]
        [Range(0f, 1f)]
        [SerializeField] private float launchAnimationTime = 0.5f;

        [Header("Affichage")]
        [SerializeField] private string sortingLayerName = "player";
        [Tooltip("Rang des boules dans la tranche de leur niveau (1 = techniques) ; la tête est un rang au-dessus")]
        [SerializeField] private int sortingOrder = 1;

        public float KiCost => kiCost;
        public float Cooldown => cooldown;
        public float LaunchAnimationTime => launchAnimationTime;
        // Le poing est revenu : la technique est finie (l'objet est détruit juste après)
        public bool IsFinished { get; private set; }

        private enum Phase { Out, Impact, Back }
        private Phase phase;
        private Vector2 origin;
        private Vector2 direction;
        // Distance actuelle du bout du poing depuis Gogeta, et distance où il s'arrête (en cases)
        private float distance;
        private float endDistance;
        // Cases libres de murs devant Gogeta (calculées au lancement), et niveau de chaque case du trajet
        private int freeCells;
        private int[] cellLevels;
        private float impactEndTime;
        private readonly List<SpriteRenderer> balls = new List<SpriteRenderer>();
        private SpriteRenderer head;

        public void Launch(Vector2 dir, int level)
        {
            origin = transform.position;
            direction = dir;
            cellLevels = new int[maxDistance + 1];
            cellLevels[0] = level;

            // Murs, sens uniques et cases pleines ne bougent pas : le bout du trajet est connu dès le lancement,
            // pour que la tête ait le temps d'apparaître avant l'impact. Contre un mur : impact sur la case d'avant
            // (à la limite de la case de Gogeta s'il y est collé)
            freeCells = maxDistance;
            int pathLevel = level;
            for (int k = 1; k <= maxDistance; k++)
            {
                if (!CollisionMap.CanTechniquePass(CellCenter(k - 1), direction, pathLevel))
                {
                    freeCells = k - 1;
                    break;
                }
                pathLevel = LevelMap.LevelAfterEntering(CellCenter(k), pathLevel);
                cellLevels[k] = pathLevel;
            }
            endDistance = freeCells > 0 ? freeCells : 0.5f;

            for (int i = 0; i < maxDistance * ballsPerCell; i++)
            {
                var sr = new GameObject($"Ball{i}").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(transform, false);
                sr.sortingLayerName = sortingLayerName;
                sr.enabled = false;
                balls.Add(sr);
            }
            head = new GameObject("Head").AddComponent<SpriteRenderer>();
            head.transform.SetParent(transform, false);
            head.sortingLayerName = sortingLayerName;
            head.enabled = false;
            phase = Phase.Out;
        }

        // Mort de Gogeta pendant la technique : le poing disparaît
        public void Cancel()
        {
            Destroy(gameObject);
        }

        private void Update()
        {
            if (cellLevels == null || IsFinished)
                return;

            switch (phase)
            {
                case Phase.Out:
                    distance += cellsPerSecond * Time.deltaTime;
                    CheckPath();
                    if (distance >= endDistance)
                    {
                        distance = endDistance;
                        phase = Phase.Impact;
                        impactEndTime = Time.time + impactDuration;
                        HitEnd();
                    }
                    break;
                case Phase.Impact:
                    if (Time.time >= impactEndTime)
                        phase = Phase.Back;
                    break;
                case Phase.Back:
                    distance -= cellsPerSecond * Time.deltaTime;
                    if (distance <= 0f)
                    {
                        IsFinished = true;
                        Destroy(gameObject);
                        return;
                    }
                    break;
            }
            UpdateSprites();
        }

        // Ennemis, cibles et obstacles cassables (ils bougent) : cherchés à chaque image sur les cases devant le bout du poing,
        // jusqu'à une case plus loin ; le premier trouvé arrête le poing, qui le mord sur sa case
        private void CheckPath()
        {
            int from = Mathf.Max(1, Mathf.CeilToInt(distance - 0.5f));
            int to = Mathf.Min(Mathf.Min(freeCells, Mathf.FloorToInt(endDistance)), Mathf.FloorToInt(distance + 1f));
            for (int k = from; k <= to; k++)
            {
                if (HeightLevel.FirstOnLevel(Physics2D.OverlapCircleAll(CellCenter(k), hitRadius, hitLayers), cellLevels[k]) != null)
                {
                    endDistance = k;
                    break;
                }
            }
            distance = Mathf.Min(distance, endDistance);
        }

        // Au bout : tout ce qui est sur la case, à son niveau, est frappé une fois (rien contre un mur)
        private void HitEnd()
        {
            int cell = Mathf.RoundToInt(endDistance);
            if (!Mathf.Approximately(endDistance, cell) || cell < 1)
                return;
            Vector2 center = CellCenter(cell);
            var hitTargets = new List<Health>();
            foreach (var hit in Physics2D.OverlapCircleAll(center, hitRadius, hitLayers))
            {
                if (!HeightLevel.IsOnLevel(hit, cellLevels[cell]))
                    continue;
                var health = hit.GetComponentInParent<Health>();
                if (health != null && !health.IsDead && !hitTargets.Contains(health))
                {
                    hitTargets.Add(health);
                    PlayerCombat.Hit(health, damage);
                }
                if (canDestroyObstacles && hit.TryGetComponent(out DestructibleTilemap destructibles))
                    destructibles.TakeDamage(center, damage);
            }
        }

        private void UpdateSprites()
        {
            Sprite ball = ByDirection(ballUp, ballDown, ballLeft, ballRight);
            Vector2 ballOffset = (Vector2)ByDirection(ballUpOffsetPixels, ballDownOffsetPixels, ballLeftOffsetPixels, ballRightOffsetPixels) / 32f;
            for (int i = 0; i < balls.Count; i++)
            {
                // Boule i à firstBallDistance + i / ballsPerCell cases de Gogeta : affichée quand la tête est passée
                // headClearanceCells plus loin (aucune boule sous la tête)
                float at = firstBallDistance + (float)i / ballsPerCell;
                var sr = balls[i];
                sr.enabled = at <= distance - headClearanceCells + 0.001f;
                if (!sr.enabled)
                    continue;
                sr.sprite = ball;
                sr.transform.position = (Vector3)(origin + direction * at + ballOffset);
                sr.sortingOrder = sortingOrder + LevelAt(at) * LevelMap.SortingOrdersPerLevel;
            }

            // Tête : approche au bout du poing pendant tout le trajet (à l'aller comme au retour, dès headMinDistance), impact au bout
            Sprite headSprite = null;
            if (phase == Phase.Impact)
                headSprite = ByDirection(impactUp, impactDown, impactLeft, impactRight);
            else if (distance >= headMinDistance)
                headSprite = ByDirection(approachUp, approachDown, approachLeft, approachRight);
            head.enabled = headSprite != null;
            if (head.enabled)
            {
                Vector2 headOffset = (Vector2)ByDirection(headUpOffsetPixels, headDownOffsetPixels, headLeftOffsetPixels, headRightOffsetPixels) / 32f;
                head.sprite = headSprite;
                head.transform.position = (Vector3)(origin + direction * distance + headOffset);
                head.sortingOrder = sortingOrder + 1 + LevelAt(distance) * LevelMap.SortingOrdersPerLevel;
            }
        }

        private int LevelAt(float at)
        {
            return cellLevels[Mathf.Clamp(Mathf.RoundToInt(at), 0, cellLevels.Length - 1)];
        }

        private Vector2 CellCenter(int index)
        {
            return origin + direction * index;
        }

        private T ByDirection<T>(T up, T down, T left, T right)
        {
            if (direction == Vector2.up) return up;
            if (direction == Vector2.down) return down;
            if (direction == Vector2.left) return left;
            return right;
        }
    }
}
