using System.Collections.Generic;
using DBZ.World;
using UnityEngine;

namespace DBZ.Combat
{
    // Big Bang (Vegeta, touche R) : la boule grossit sur la case devant le joueur (chargeFrames, une fois),
    // puis loopFrames alternent tant que la touche est tenue. Au relâchement, même pendant la charge, elle part tout droit,
    // lentement, en gardant l'image affichée à ce moment-là.
    // Elle est large de 3 cases (sa case et une de chaque côté) : elle explose sur le premier ennemi, cible ou Destructible
    // de cette largeur, ou contre un mur (calques de collision, sur sa ligne centrale), et frappe tout sur les 3 cases.
    // Au bout de sa portée, elle disparaît sans exploser
    [RequireComponent(typeof(SpriteRenderer))]
    public class BigBangBall : ChargedTechnique
    {
        [Header("Images")]
        [Tooltip("Images du chargement, jouées une fois")]
        [SerializeField] private Sprite[] chargeFrames = new Sprite[7];
        [Min(0.1f)]
        [SerializeField] private float chargeFramesPerSecond = 10f;
        [Tooltip("Images de la boule chargée, en boucle tant que la touche est tenue")]
        [SerializeField] private Sprite[] loopFrames = new Sprite[3];
        [Min(0.1f)]
        [SerializeField] private float loopFramesPerSecond = 8f;

        [Header("Trajet")]
        [Tooltip("Vitesse, en cases par seconde")]
        [Min(0.1f)]
        [SerializeField] private float cellsPerSecond = 3f;
        [Tooltip("Portée en cases, depuis la case devant le joueur")]
        [Min(0)]
        [SerializeField] private int maxDistance = 5;

        [Header("Dégâts")]
        [SerializeField] private float damage = 50f;
        [Tooltip("Cases touchées de chaque côté de la ligne du tir : 1 = 3 cases de large")]
        [Min(0)]
        [SerializeField] private int halfWidth = 1;
        [Tooltip("Layer(s) touchés : cibles, ennemis, Destructibles")]
        [SerializeField] private LayerMask hitLayers;
        [SerializeField] private float hitRadius = 0.3f;
        [SerializeField] private bool canDestroyObstacles = true;
        [Tooltip("Effet joué sur chaque case touchée (sur la case centrale contre un mur)")]
        [SerializeField] private OneShotEffect impactPrefab;

        [Header("Affichage")]
        [SerializeField] private string sortingLayerName = "player";
        // Rang des techniques dans la tranche du niveau (LevelMap.SortingOrdersPerLevel)
        [SerializeField] private int sortingRank = 1;

        private SpriteRenderer ballRenderer;
        private Vector2 direction;
        // Centre de la case devant le joueur (rangée 0) et niveau de hauteur de la rangée actuelle
        private Vector2 start;
        private int level;
        private float age;
        private bool isReleased;
        // Distance parcourue depuis la rangée 0, en cases, et dernière rangée vérifiée
        private float traveled;
        private int row;

        public override void Begin(Vector2 playerCellCenter, Vector2 dir, int playerLevel)
        {
            ballRenderer = GetComponent<SpriteRenderer>();
            ballRenderer.sortingLayerName = sortingLayerName;
            direction = dir;
            start = playerCellCenter + dir;
            transform.position = start;
            SetLevel(playerLevel);
            if (chargeFrames.Length > 0 && chargeFrames[0] != null)
                ballRenderer.sprite = chargeFrames[0];
        }

        // Part tout de suite, chargée ou non, avec l'image affichée
        public override void Release()
        {
            isReleased = true;
            // Rangée 0 : un mur juste devant le joueur, ou une cible déjà dans la boule
            Vector2 playerCell = start - direction;
            if (!CollisionMap.CanTechniquePass(playerCell, direction, level))
            {
                Explode(0, true);
                return;
            }
            SetLevel(LevelMap.LevelAfterEntering(start, level));
            if (HasTargetInRow(0))
                Explode(0, false);
        }

        public override void Cancel()
        {
            Destroy(gameObject);
        }

        private void Update()
        {
            if (!isReleased)
            {
                Animate();
                return;
            }

            traveled = Mathf.Min(traveled + cellsPerSecond * Time.deltaTime, maxDistance);
            transform.position = Snap(start + direction * traveled);

            // La boule entre dans une rangée quand son centre en passe la moitié
            int reached = Mathf.Min(Mathf.FloorToInt(traveled + 0.5f), maxDistance);
            while (row < reached)
            {
                if (!CollisionMap.CanTechniquePass(RowCenter(row), direction, level))
                {
                    Explode(row, true);
                    return;
                }
                row++;
                // Rangée quittée revérifiée : un ennemi qui marche vers la boule a pu y entrer pendant qu'elle en sortait
                // (sinon ils se croisent). Même niveau : la boule vient de cette rangée
                if (HasTargetInRow(row - 1))
                {
                    Explode(row - 1, false);
                    return;
                }
                SetLevel(LevelMap.LevelAfterEntering(RowCenter(row), level));
                if (HasTargetInRow(row))
                {
                    Explode(row, false);
                    return;
                }
            }

            // Rangée actuelle vérifiée à chaque image : un ennemi peut y entrer (de face ou par le côté) après la boule
            if (HasTargetInRow(row))
            {
                Explode(row, false);
                return;
            }

            if (traveled >= maxDistance)
                Destroy(gameObject);
        }

        // Chargement une fois, puis boucle ; image manquante : on garde celle affichée
        private void Animate()
        {
            age += Time.deltaTime;
            int frame = (int)(age * chargeFramesPerSecond);
            Sprite sprite = null;
            if (frame < chargeFrames.Length)
                sprite = chargeFrames[frame];
            else if (loopFrames.Length > 0)
            {
                float loopTime = age - chargeFrames.Length / chargeFramesPerSecond;
                sprite = loopFrames[(int)(loopTime * loopFramesPerSecond) % loopFrames.Length];
            }
            if (sprite != null)
                ballRenderer.sprite = sprite;
        }

        // Cases d'une rangée : la case de la ligne du tir et halfWidth cases de chaque côté
        private IEnumerable<Vector2> RowCells(int index)
        {
            Vector2 side = new Vector2(-direction.y, direction.x);
            for (int s = -halfWidth; s <= halfWidth; s++)
                yield return RowCenter(index) + side * s;
        }

        private bool HasTargetInRow(int index)
        {
            foreach (var cell in RowCells(index))
            {
                if (HeightLevel.FirstOnLevel(Physics2D.OverlapCircleAll(cell, hitRadius, hitLayers), level) != null)
                    return true;
            }
            return false;
        }

        // Frappe tout ce qui est sur les cases de la rangée, au niveau de la boule (une fois par cible)
        private void Explode(int index, bool againstWall)
        {
            var hitTargets = new HashSet<Health>();
            bool hitSomething = false;
            foreach (var cell in RowCells(index))
            {
                bool hitCell = false;
                foreach (var hit in Physics2D.OverlapCircleAll(cell, hitRadius, hitLayers))
                {
                    if (!HeightLevel.IsOnLevel(hit, level))
                        continue;
                    hitCell = true;

                    var health = hit.GetComponentInParent<Health>();
                    if (health != null && hitTargets.Add(health))
                        PlayerCombat.Hit(health, damage);

                    if (canDestroyObstacles && hit.TryGetComponent(out DestructibleTilemap destructibles))
                        destructibles.TakeDamage(cell, damage);
                }
                if (hitCell)
                {
                    SpawnImpact(cell);
                    hitSomething = true;
                }
            }

            // Contre un mur, sans rien d'autre de touché : l'effet sur la case centrale
            if (againstWall && !hitSomething)
                SpawnImpact(RowCenter(index));
            Destroy(gameObject);
        }

        private void SpawnImpact(Vector2 cell)
        {
            if (impactPrefab == null)
                return;
            var impact = Instantiate(impactPrefab, cell, Quaternion.identity);
            if (impact.TryGetComponent(out SpriteRenderer impactRenderer))
                impactRenderer.sortingOrder += level * LevelMap.SortingOrdersPerLevel;
        }

        // Au sol, la boule passe sous le pont ; en haut, par-dessus
        private void SetLevel(int newLevel)
        {
            level = newLevel;
            ballRenderer.sortingOrder = sortingRank + level * LevelMap.SortingOrdersPerLevel;
        }

        private Vector2 RowCenter(int index)
        {
            return start + direction * index;
        }

        // Arrondi au pixel (32 par case) : la boule reste nette avec la Pixel Perfect Camera
        private static Vector3 Snap(Vector2 position)
        {
            return new Vector3(Mathf.Round(position.x * 32f) / 32f, Mathf.Round(position.y * 32f) / 32f, 0f);
        }
    }
}
