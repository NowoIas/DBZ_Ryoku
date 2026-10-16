using System.Collections.Generic;
using DBZ.World;
using UnityEngine;

namespace DBZ.Combat
{
    // Genkidama : la boule grossit sur la case au-dessus de Goku (3 images de charge), puis reste prête (4e image).
    // Une fois prête, une cible glisse en allers-retours de minDistance à maxDistance cases devant Goku, au sol.
    // Au relâchement, la boule (4e image) part en cloche vers la cible (flightFrames images) et explose : la case la plus proche
    // de la cible et les cases autour (blastRadius) sont frappées.
    // Relâchée avant d'être prête : la technique est annulée (le Ki payé à l'appui est perdu).
    // La boule et la cible passent au-dessus des obstacles (la boule est lancée d'en haut).
    public class GenkidamaBall : ChargedTechnique
    {
        [Header("Images")]
        [Tooltip("Images de la charge (genkidama), jouées une fois : la boule est prête à la fin de la dernière")]
        [SerializeField] private Sprite[] ballFrames = new Sprite[4];
        [Tooltip("Vitesse de la charge, en images par seconde (comme la charge du Kamehameha)")]
        [SerializeField] private float chargeFramesPerSecond = 6f;
        [Tooltip("Images de la boule prête (genkidama_charge), en boucle pendant la visée ; la dernière reste pendant le vol")]
        [SerializeField] private Sprite[] readyFrames = new Sprite[4];
        [Tooltip("Vitesse de la boucle de la boule prête, en images par seconde")]
        [Min(0.1f)]
        [SerializeField] private float readyFramesPerSecond = 8f;
        [Tooltip("Rendu de la cible (objet enfant)")]
        [SerializeField] private SpriteRenderer target;

        [Header("Visée")]
        [SerializeField] private int minDistance = 1;
        [SerializeField] private int maxDistance = 5;
        [Tooltip("Vitesse de la cible (glissement continu), en cases par seconde")]
        [Min(0.1f)]
        [SerializeField] private float targetCellsPerSecond = 3f;
        [Tooltip("Décalage de la cible vers la gauche ou la droite, en pixels : en bas de la case, aux pieds des personnages")]
        [SerializeField] private Vector2Int horizontalTargetOffsetPixels = new Vector2Int(0, -8);

        [Header("Lancer (en cloche)")]
        [Tooltip("Durée du vol jusqu'à la cible, en images à 60 images/s (22 = 0,37 s), quelle que soit la distance")]
        [Min(1)]
        [SerializeField] private int flightFrames = 22;
        [Tooltip("Gravité, en pixels par image² : la vitesse de montée est calculée pour arriver au sol à la dernière image")]
        [Min(0f)]
        [SerializeField] private float gravityPixels = 0.5f;
        [SerializeField] private float damage = 50f;
        [Tooltip("Rayon de l'explosion en cases autour de la case visée : 1 = zone de 3×3 cases, 0 = case visée seule")]
        [Min(0)]
        [SerializeField] private int blastRadius = 1;
        [Tooltip("Layer(s) touchés dans la zone : cibles, ennemis, Destructibles")]
        [SerializeField] private LayerMask hitLayers;
        [SerializeField] private float hitRadius = 0.3f;
        [SerializeField] private bool canDestroyObstacles = true;
        [Tooltip("PV rendus au joueur et aux alliés (Health.isAlly) dans la zone, à son niveau (Masenko) ; 0 = pas de soin (Genkidama)")]
        [Min(0f)]
        [SerializeField] private float healAmount;
        [Tooltip("Layer(s) des alliés à soigner en plus de hitLayers : le joueur (layer Player)")]
        [SerializeField] private LayerMask healLayers;
        [Tooltip("Effet joué à l'impact (facultatif)")]
        [SerializeField] private OneShotEffect impactPrefab;

        [Header("Affichage")]
        // La boule est en l'air : au-dessus de tout (sous Goku en vol, ordre 50)
        [SerializeField] private string ballSortingLayer = "foreground";
        [SerializeField] private int ballSortingOrder = 40;
        // La cible est au sol : rang des impacts dans la tranche du niveau (LevelMap.SortingOrdersPerLevel)
        [SerializeField] private string targetSortingLayer = "player";
        [SerializeField] private int targetSortingRank = 2;
        // L'explosion au niveau 0 : sur le calque des tuiles, au-dessus du décor (1) et des zénis (10), sous les personnages
        [SerializeField] private string groundImpactSortingLayer = "background";
        [SerializeField] private int groundImpactSortingOrder = 20;

        public bool IsReady { get; private set; }

        private SpriteRenderer ballRenderer;
        private Vector2 direction;
        // Case de Goku au lancement, et niveau de hauteur de chaque case visable (index = distance)
        private Vector2 origin;
        private int[] cellLevels;
        private float age;
        private float readyTime;
        private float targetTime;
        // Distance de la cible (continue) et case la plus proche (celle qui sera frappée)
        private float targetDistance;
        private int targetCell;
        private bool isThrown;
        private float throwTime;
        // Trajet au sol (case de Goku → cible) et hauteur au-dessus en pixels : départ et vitesse de montée
        private Vector3 throwTarget;
        private float startHeight;
        private float riseSpeed;
        private Vector3 impactCell;
        private int throwLevel;

        // À appeler juste après l'apparition : playerCellCenter = centre de la case de Goku,
        // dir = sens de la visée, level = niveau de hauteur de Goku. La boule se place sur la case au-dessus de lui
        public override void Begin(Vector2 playerCellCenter, Vector2 dir, int level)
        {
            transform.position = (Vector3)playerCellCenter + Vector3.up;
            ballRenderer = GetComponent<SpriteRenderer>();
            ballRenderer.sortingLayerName = ballSortingLayer;
            ballRenderer.sortingOrder = ballSortingOrder;
            ballRenderer.sprite = ballFrames[0];

            direction = dir;
            origin = playerCellCenter;

            // Niveau de chaque case de la ligne de visée, en suivant les tuiles Niveaux comme un tir (sans collisions)
            cellLevels = new int[maxDistance + 1];
            cellLevels[0] = level;
            for (int d = 1; d <= maxDistance; d++)
                cellLevels[d] = LevelMap.LevelAfterEntering(CellCenter(d), cellLevels[d - 1]);

            if (target != null)
            {
                target.enabled = false;
                target.sortingLayerName = targetSortingLayer;
            }
        }

        // Relâchement : lancée si elle est prête (vers la case de la cible), sinon annulée
        public override void Release()
        {
            if (!IsReady)
            {
                Destroy(gameObject);
                return;
            }

            isThrown = true;
            throwTime = 0f;
            // En vol : la dernière image de la boule prête, fixe
            if (readyFrames.Length > 0 && readyFrames[readyFrames.Length - 1] != null)
                ballRenderer.sprite = readyFrames[readyFrames.Length - 1];
            // La boule tombe là où est la cible (sur la ligne de visée), et frappe la case la plus proche
            throwTarget = Snap(origin + direction * targetDistance);
            // Hauteur de départ = là où la boule est tenue, au-dessus de la case de Goku.
            // Hauteur(t) = départ + montée·t − gravité/2·t² : la montée est choisie pour que la hauteur soit 0 à la dernière image
            startHeight = (transform.position.y - origin.y) * 32f;
            riseSpeed = (gravityPixels / 2f * flightFrames * flightFrames - startHeight) / flightFrames;
            impactCell = CellCenter(targetCell);
            throwLevel = cellLevels[targetCell];
            if (target != null)
                target.enabled = false;
        }

        // Goku K.O. pendant la charge ou la visée : la boule disparaît
        public override void Cancel()
        {
            Destroy(gameObject);
        }

        private void Update()
        {
            if (isThrown)
            {
                // Temps compté en images à 60 images/s, mais tiré du temps réel : même vol quelle que soit la vitesse du PC
                throwTime += Time.deltaTime;
                float frame = Mathf.Min(throwTime * 60f, flightFrames);
                // Au sol : vitesse constante (distance ÷ durée) ; en hauteur : la cloche
                Vector3 ground = Vector3.Lerp(origin, throwTarget, frame / flightFrames);
                float height = startHeight + riseSpeed * frame - gravityPixels / 2f * frame * frame;
                transform.position = Snap(ground + Vector3.up * (height / 32f));
                if (frame >= flightFrames)
                    Impact();
                return;
            }

            age += Time.deltaTime;
            if (!IsReady)
            {
                int frame = (int)(age * chargeFramesPerSecond);
                if (frame < ballFrames.Length)
                {
                    ballRenderer.sprite = ballFrames[frame];
                    return;
                }

                // Boule prête : la boucle commence, la cible apparaît sur la première case
                IsReady = true;
                readyTime = 0f;
                targetTime = 0f;
                if (target != null)
                    target.enabled = true;
            }

            AnimateReady();
            UpdateTarget();
        }

        // Boule prête, pendant la visée : genkidama_charge en boucle (vitesse relue à chaque image : réglable en jeu).
        // Image manquante : on garde celle affichée (la boule ne disparaît pas)
        private void AnimateReady()
        {
            if (readyFrames.Length == 0)
                return;
            readyTime += Time.deltaTime;
            var sprite = readyFrames[(int)(readyTime * readyFramesPerSecond) % readyFrames.Length];
            if (sprite != null)
                ballRenderer.sprite = sprite;
        }

        // Aller-retour en glissant : min → max → min..., à targetCellsPerSecond (relu à chaque image : réglable en jeu)
        private void UpdateTarget()
        {
            targetTime += Time.deltaTime * targetCellsPerSecond;
            targetDistance = minDistance + Mathf.PingPong(targetTime, maxDistance - minDistance);
            targetCell = Mathf.Clamp(Mathf.RoundToInt(targetDistance), minDistance, maxDistance);

            if (target != null)
            {
                Vector2 position = origin + direction * targetDistance;
                // Vers la gauche ou la droite : en bas de la case, aux pieds des personnages
                if (direction.x != 0f)
                    position += (Vector2)horizontalTargetOffsetPixels / 32f;
                target.transform.position = Snap(position);
                target.sortingOrder = targetSortingRank + cellLevels[targetCell] * LevelMap.SortingOrdersPerLevel;
            }
        }

        // Arrondi au pixel (32 par case) : la cible et la boule restent nettes avec la Pixel Perfect Camera
        private static Vector3 Snap(Vector3 position)
        {
            return new Vector3(Mathf.Round(position.x * 32f) / 32f, Mathf.Round(position.y * 32f) / 32f, position.z);
        }

        // Frappe tout ce qui est dans la zone (case visée + blastRadius cases autour), au niveau de hauteur de la case visée.
        // Lancée d'en haut : les murs et cases pleines n'arrêtent pas l'explosion
        private void Impact()
        {
            // Une seule fois par cible, même si son collider déborde sur deux cases de la zone
            var hitTargets = new HashSet<Health>();
            LayerMask layers = healAmount > 0f ? hitLayers | healLayers : hitLayers;
            for (int dx = -blastRadius; dx <= blastRadius; dx++)
            {
                for (int dy = -blastRadius; dy <= blastRadius; dy++)
                {
                    Vector3 cell = impactCell + new Vector3(dx, dy, 0f);
                    foreach (var hit in Physics2D.OverlapCircleAll(cell, hitRadius, layers))
                    {
                        if (!HeightLevel.IsOnLevel(hit, throwLevel))
                            continue;

                        var health = hit.GetComponentInParent<Health>();
                        if (health != null && hitTargets.Add(health))
                        {
                            // Joueur et alliés : soignés (PlayerCombat.Hit ne leur fait rien) ; les autres : frappés
                            if (health.IsAlly || health.CompareTag("Player"))
                                health.Heal(healAmount);
                            else
                                PlayerCombat.Hit(health, damage);
                        }

                        if (canDestroyObstacles && hit.TryGetComponent(out DestructibleTilemap destructibles))
                            destructibles.TakeDamage(cell, damage);
                    }
                }
            }

            if (impactPrefab != null)
            {
                var impact = Instantiate(impactPrefab, impactCell, Quaternion.identity);
                if (impact.TryGetComponent(out SpriteRenderer impactRenderer))
                {
                    // L'onde se propage au sol, toujours sous les personnages. Niveau 0 : calque des tuiles, au-dessus du décor
                    // et des zénis ; plus haut : dernier ordre de la tranche d'en dessous (comme le Pont), juste sous ce niveau
                    if (throwLevel == 0)
                    {
                        impactRenderer.sortingLayerName = groundImpactSortingLayer;
                        impactRenderer.sortingOrder = groundImpactSortingOrder;
                    }
                    else
                    {
                        impactRenderer.sortingLayerName = "player";
                        impactRenderer.sortingOrder = throwLevel * LevelMap.SortingOrdersPerLevel - 1;
                    }
                }
            }
            Destroy(gameObject);
        }

        private Vector3 CellCenter(int distance)
        {
            return origin + direction * distance;
        }
    }
}
