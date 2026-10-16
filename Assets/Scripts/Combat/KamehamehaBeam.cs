using DBZ.World;
using UnityEngine;

namespace DBZ.Combat
{
    // Rayon du Kamehameha (aussi Final Flash, Makankosappo) : une ligne de cases qui pousse depuis la case devant le joueur
    // (le départ du rayon est dessiné dans les mains du personnage, planche s2).
    // Il s'arrête sur la case avant le premier obstacle ou ennemi (sur sa case si on y est collé),
    // et le frappe à intervalle régulier.
    // Au relâchement, une traînée efface le rayon case par case depuis le départ.
    // Les sprites sont dessinés vers la droite : le rayon entier est tourné selon la direction du tir.
    public class KamehamehaBeam : MonoBehaviour
    {
        [Header("Sprites (dessinés vers la droite ; vide = corps du rayon)")]
        [Tooltip("Départ du rayon, sur la case devant le personnage (vide : le départ est dessiné dans ses mains, Kamehameha)")]
        [SerializeField] private Sprite startSprite;
        [Tooltip("Départ animé, en alternance (Makankosappo) ; s'il est rempli, il remplace Start Sprite")]
        [SerializeField] private Sprite[] startSprites = new Sprite[0];
        // Impact, sur la case juste avant l'obstacle (sur sa case si on y est collé), en alternance
        [SerializeField] private Sprite[] impactSprites = new Sprite[2];
        // Traînée qui efface le rayon après le relâchement
        [SerializeField] private Sprite releaseTrailSprite;
        // Boule en tête du rayon quand il ne touche rien
        [SerializeField] private Sprite headSprite;
        [Tooltip("Bout animé, en alternance (Kamehameha) ; s'il est rempli, il remplace Head Sprite")]
        [SerializeField] private Sprite[] headSprites = new Sprite[0];
        // Corps du rayon, entre le départ et la tête
        [SerializeField] private Sprite bodySprite;
        [Tooltip("Corps animé, en alternance (Makankosappo) ; s'il est rempli, il remplace Body Sprite (et sert aussi de traînée si elle est vide)")]
        [SerializeField] private Sprite[] bodySprites = new Sprite[0];
        [Tooltip("Vitesse d'alternance des sprites d'impact (images par seconde)")]
        [SerializeField] private float animationFramesPerSecond = 8f;

        [Header("Alignement sur les mains (en pixels, x vers la droite, y vers le haut)")]
        // Le corps du rayon est dessiné 1 px sous le centre de sa case : une fois tourné, ce décalage change de côté
        // et ne tombe plus sur les mains du personnage. Ces décalages l'y ramènent, direction par direction
        [SerializeField] private Vector2Int upOffsetPixels = new Vector2Int(-2, 0);
        [SerializeField] private Vector2Int downOffsetPixels = new Vector2Int(2, 0);
        [SerializeField] private Vector2Int leftOffsetPixels = new Vector2Int(0, -2);
        [SerializeField] private Vector2Int rightOffsetPixels = Vector2Int.zero;

        [Header("Portée")]
        [SerializeField] private int maxLength = 10;
        [Tooltip("Vitesse à laquelle le rayon pousse, puis à laquelle la traînée l'efface (cases par seconde)")]
        [SerializeField] private float cellsPerSecond = 30f;
        [Tooltip("Morceaux de rayon par case : 1 = images de 32 px (Kamehameha), 2 = images de 16 px (Makankosappo)")]
        [Min(1)]
        [SerializeField] private int segmentsPerCell = 1;

        [Header("Dégâts")]
        [SerializeField] private float damage = 10f;
        [Tooltip("Temps entre deux coups sur la cible touchée (secondes)")]
        [SerializeField] private float damageInterval = 0.25f;
        // Layers qui arrêtent le rayon : obstacles et cibles (SolidObjects)
        [SerializeField] private LayerMask hitLayers;
        [Tooltip("Rayon de la zone testée au centre de chaque case (en cases)")]
        [SerializeField] private float hitRadius = 0.3f;
        // Peut casser les tuiles du calque Destructibles ; le rayon continue ensuite plus loin
        [SerializeField] private bool canDestroyObstacles = true;
        [Tooltip("Traverse les ennemis et les cibles (Makankosappo) : seuls les murs l'arrêtent, il frappe tout ce qui est sur son trajet")]
        [SerializeField] private bool piercesTargets = false;
        [Tooltip("Impact toujours dessiné au bout du rayon tant que la touche est tenue, même sans rien toucher (Makankosappo)")]
        [SerializeField] private bool impactAtTip = false;
        [Tooltip("Arrêté par un ennemi, une cible ou un obstacle cassable : le bout du rayon avance de ces pixels dans sa case, "
            + "par-dessus lui, pour qu'il ait l'air touché (affichage seul ; 0 = bout sur la case d'avant, au plus un morceau)")]
        [Min(0)]
        [SerializeField] private int targetOverlapPixels = 0;

        [Header("Ouverture / fermeture (0 = aucune, Kamehameha)")]
        [Tooltip("Temps pour qu'une case s'élargisse d'un trait fin à sa taille normale, après son apparition (secondes)")]
        [Min(0f)]
        [SerializeField] private float openDuration = 0f;
        [Tooltip("Au relâchement, tout le rayon s'amincit d'un coup jusqu'à disparaître, en ce temps (secondes). "
            + "0 = traînée qui efface le rayon depuis le départ")]
        [Min(0f)]
        [SerializeField] private float closeDuration = 0f;

        [Header("Pause")]
        [Tooltip("Pause après le relâchement, avant de pouvoir lancer une autre attaque (secondes, 0 = aucune)")]
        [Min(0f)]
        [SerializeField] private float cooldown = 0f;

        [Header("Affichage")]
        [SerializeField] private string sortingLayerName = "player";
        // Ordre au niveau 0 ; chaque case est décalée selon son niveau (LevelMap.SortingOrdersPerLevel)
        [SerializeField] private int sortingOrder = 1;
        [Tooltip("Directions où le départ du rayon passe sous le joueur, qui reste devant "
            + "(Final Flash : haut ; Makankosappo : haut, gauche, droite)")]
        [SerializeField] private CellSides startBehindPlayerDirections = CellSides.None;
        [Tooltip("Directions où le bout avancé dans la case de la cible (Target Overlap Pixels) passe derrière elle "
            + "(bas : le rayon arrive par le haut de sa case, vue de dessus)")]
        [SerializeField] private CellSides tipBehindTargetDirections = CellSides.Down;

        public bool IsReleased { get; private set; }
        public float Cooldown => cooldown;

        private Vector2 direction;
        // Niveau de hauteur du joueur au tir : le rayon suit ensuite les tuiles Niveaux, comme le joueur
        private int launchLevel;
        private SpriteRenderer[] cells;
        // Morceau de corps sous le départ, quand celui-ci est reculé vers les mains (null sinon)
        private SpriteRenderer startFiller;
        // Morceau de corps derrière le bout avancé dans la case de la cible (targetOverlapPixels), bouche le trou laissé
        private SpriteRenderer endFiller;
        // Bout de corps raccourci devant le départ, quand lui seul est affiché devant la cible (Final Flash à une case libre)
        private SpriteRenderer tipExtension;
        // Position locale de chaque morceau sans avancée du bout
        private Vector3[] segmentPositions;
        // Niveau de chaque case du rayon : son ordre d'affichage en dépend (sous le pont au sol, dessus en haut)
        private int[] cellLevels;
        // Longueur atteinte, en cases (fractionnaire pendant la pousse)
        private float grown;
        // Nombre de cases déjà effacées par la traînée
        private float trail;
        private float damageTimer;
        // Moment où chaque case est apparue (début de son ouverture)
        private float[] cellAppearTimes;
        // Cases affichées à la dernière image, figées au relâchement pour la fermeture
        private int lastVisible;
        private int releaseVisible;
        private float releaseTime;

        public void Launch(Vector2 dir, int level)
        {
            direction = dir;
            launchLevel = level;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);

            // Décalage ramené dans le repère tourné du rayon, en pixels entiers pour rester net (32 px par case).
            // La partie perpendiculaire au tir déplace tout le rayon (alignement sur les mains) ; la partie dans le sens
            // du tir ne déplace que le départ, pour que le bout et l'impact restent collés au mur ou à la cible
            Vector2 localPixels = Quaternion.Inverse(transform.rotation) * (Vector2)HandOffsetPixels(dir);
            // Arrondi : la rotation laisse de minuscules erreurs (0,9999 px au lieu de 1)
            Vector2 localOffset = new Vector2(Mathf.Round(localPixels.x), Mathf.Round(localPixels.y)) / 32f;
            Vector3 across = new Vector3(0f, localOffset.y, 0f);
            float along = localOffset.x;

            // Un morceau = un SpriteRenderer enfant, alignés vers la droite en local (segmentsPerCell par case)
            int segments = maxLength * segmentsPerCell;
            cells = new SpriteRenderer[segments];
            cellLevels = new int[maxLength];
            cellAppearTimes = new float[segments];
            segmentPositions = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                var cell = new GameObject($"Cell{i}");
                cell.transform.SetParent(transform, false);
                // Centre du morceau dans sa case : avec 2 morceaux par case, à -0,25 et +0,25 du centre de la case
                float x = (i + 0.5f) / segmentsPerCell - 0.5f;
                segmentPositions[i] = new Vector3(i == 0 ? x + along : x, 0f, 0f) + across;
                cell.transform.localPosition = segmentPositions[i];
                var sr = cell.AddComponent<SpriteRenderer>();
                sr.sortingLayerName = sortingLayerName;
                sr.sortingOrder = sortingOrder;
                sr.enabled = false;
                cells[i] = sr;
            }

            // Départ reculé vers les mains : un morceau de corps collé au bord avant du départ bouche le trou qu'il laisse
            // avant le morceau suivant (couvre un recul d'un morceau au plus : 16 px à 2 morceaux par case).
            // Pas sous le départ : il se verrait dans ses parties transparentes (arrière arrondi du Final Flash).
            // Ce qui dépasse passe sous le morceau suivant, dessiné par-dessus.
            if (along < 0f && segments > 1)
            {
                var filler = new GameObject("StartFiller");
                filler.transform.SetParent(transform, false);
                float segmentLength = 1f / segmentsPerCell;
                filler.transform.localPosition = new Vector3(0.5f * segmentLength - 0.5f + along + segmentLength, 0f, 0f) + across;
                startFiller = filler.AddComponent<SpriteRenderer>();
                startFiller.sortingLayerName = sortingLayerName;
                startFiller.enabled = false;
            }

            if (targetOverlapPixels > 0)
            {
                endFiller = new GameObject("EndFiller").AddComponent<SpriteRenderer>();
                endFiller.transform.SetParent(transform, false);
                endFiller.sortingLayerName = sortingLayerName;
                endFiller.enabled = false;

                tipExtension = new GameObject("TipExtension").AddComponent<SpriteRenderer>();
                tipExtension.transform.SetParent(transform, false);
                tipExtension.sortingLayerName = sortingLayerName;
                tipExtension.enabled = false;
            }

            grown = 1f;
        }

        private Vector2Int HandOffsetPixels(Vector2 dir)
        {
            if (dir == Vector2.up) return upOffsetPixels;
            if (dir == Vector2.down) return downOffsetPixels;
            if (dir == Vector2.left) return leftOffsetPixels;
            return rightOffsetPixels;
        }

        private static CellSides DirectionSide(Vector2 dir)
        {
            if (dir == Vector2.up) return CellSides.Up;
            if (dir == Vector2.down) return CellSides.Down;
            if (dir == Vector2.left) return CellSides.Left;
            return CellSides.Right;
        }

        public void Release()
        {
            IsReleased = true;
            releaseTime = Time.time;
            releaseVisible = lastVisible;
        }

        private void Update()
        {
            if (cells == null)
                return;

            // Fermeture depuis le bout : plus de pousse ni de dégâts, le rayon garde sa longueur du relâchement
            if (IsReleased && closeDuration > 0f)
            {
                UpdateClosing();
                return;
            }

            if (IsReleased)
                trail += cellsPerSecond * Time.deltaTime;
            else
                grown = Mathf.Min(grown + cellsPerSecond * Time.deltaTime, maxLength);

            // Premier obstacle ou ennemi sur la partie du rayon déjà sortie
            int reach = Mathf.CeilToInt(grown);
            int blockedIndex = -1;
            Collider2D target = null;
            // Recalculé depuis le départ à chaque image : reste juste si un obstacle se casse
            int cellLevel = launchLevel;
            for (int i = 0; i < reach; i++)
            {
                // Côté bloqué (mur, sens unique, case pleine) entre la case précédente et celle-ci, sur le calque
                // du niveau de la case de départ ; la première part de la case du joueur
                if (!CollisionMap.CanTechniquePass(CellCenter(i - 1), direction, cellLevel))
                {
                    blockedIndex = i;
                    target = null;
                    // Case affichée seulement si on est collé : au niveau de la case d'avant
                    cellLevels[i] = cellLevel;
                    break;
                }
                cellLevel = LevelMap.LevelAfterEntering(CellCenter(i), cellLevel);
                cellLevels[i] = cellLevel;

                // Seuls les objets au niveau de cette case du rayon l'arrêtent (sauf rayon qui les traverse)
                if (piercesTargets)
                    continue;

                target = HeightLevel.FirstOnLevel(Physics2D.OverlapCircleAll(CellCenter(i), hitRadius, hitLayers), cellLevel);
                if (target != null)
                {
                    blockedIndex = i;
                    break;
                }
            }

            int visible = reach;
            bool isHitting = blockedIndex >= 0;
            if (isHitting)
            {
                // Le rayon s'arrête sur la case avant la cible ; collé à elle (aucune case libre),
                // l'impact est dessiné sur sa case pour rester visible. Il repartira si la cible disparaît
                visible = blockedIndex == 0 ? 1 : blockedIndex;
                grown = Mathf.Min(grown, blockedIndex + 1f);
            }

            if (piercesTargets)
            {
                // Tout ce qui est sur les cases traversées (avant le mur éventuel) est frappé
                if (!IsReleased)
                    DamageAll(isHitting ? blockedIndex : reach);
            }
            else if (target != null && !IsReleased)
                DamageTarget(target, CellCenter(blockedIndex));
            else if (target == null)
            {
                // Rien à frapper (ou arrêté par un mur) : le prochain contact frappe tout de suite
                damageTimer = 0f;
            }

            // Des cases aux morceaux affichés (identiques avec un morceau par case)
            int visibleSegments = Mathf.Min(Mathf.CeilToInt(grown * segmentsPerCell), visible * segmentsPerCell);
            int trailIndex = Mathf.FloorToInt(trail * segmentsPerCell);
            if (IsReleased && trailIndex >= visibleSegments)
            {
                Destroy(gameObject);
                return;
            }

            lastVisible = visibleSegments;
            // Bout avancé dans la case de la cible : arrêté par un objet (pas un mur), pas collé à lui, rayon arrivé jusqu'à lui
            bool reachesTarget = target != null && blockedIndex > 0 && visibleSegments == blockedIndex * segmentsPerCell;
            UpdateSprites(visibleSegments, isHitting, trailIndex);
            UpdateTipOverlap(visibleSegments, reachesTarget && !(IsReleased && trailIndex >= visibleSegments - 1));
        }

        // Toutes les cases se referment en même temps, sur place
        private void UpdateClosing()
        {
            float closing = 1f - Mathf.Clamp01((Time.time - releaseTime) / closeDuration);
            bool anyOpen = false;
            for (int i = 0; i < cells.Length; i++)
            {
                var sr = cells[i];
                if (i >= releaseVisible || !sr.enabled)
                {
                    sr.enabled = false;
                    continue;
                }

                // Une case encore en train de s'ouvrir ne s'élargit plus : elle se referme depuis son épaisseur actuelle
                float thickness = Mathf.Min(OpenFactor(i), closing);
                if (thickness <= 0f)
                {
                    sr.enabled = false;
                    continue;
                }
                SetThickness(sr, thickness);
                anyOpen = true;
            }
            UpdateStartFiller();
            UpdateEndFiller(releaseVisible);
            // Le bout devant le départ se referme avec lui
            if (tipExtension != null && tipExtension.enabled)
            {
                tipExtension.enabled = cells[0].enabled;
                var scale = tipExtension.transform.localScale;
                tipExtension.transform.localScale = new Vector3(scale.x, cells[0].transform.localScale.y, 1f);
            }

            if (!anyOpen)
                Destroy(gameObject);
        }

        private float OpenFactor(int index)
        {
            if (openDuration <= 0f)
                return 1f;
            return Mathf.Clamp01((Time.time - cellAppearTimes[index]) / openDuration);
        }

        // Épaisseur (perpendiculaire au rayon : y local, les sprites étant dessinés vers la droite), arrondie à un nombre pair
        // de pixels pour rester nette et centrée sur l'axe du rayon ; 2 px au minimum pour que le trait du début se voie
        private static void SetThickness(SpriteRenderer sr, float factor)
        {
            if (sr.sprite == null)
                return;
            float height = sr.sprite.rect.height;
            float pixels = factor >= 1f ? height : Mathf.Min(height, Mathf.Max(2f, Mathf.Round(height * factor / 2f) * 2f));
            sr.transform.localScale = new Vector3(1f, pixels / height, 1f);
        }

        private Vector2 CellCenter(int index)
        {
            return (Vector2)transform.position + direction * index;
        }

        private void DamageTarget(Collider2D target, Vector2 cellCenter)
        {
            damageTimer -= Time.deltaTime;
            if (damageTimer > 0f)
                return;
            damageTimer = damageInterval;

            var health = target.GetComponentInParent<Health>();
            if (health != null)
                PlayerCombat.Hit(health, damage);

            if (canDestroyObstacles)
            {
                var destructibles = target.GetComponent<DestructibleTilemap>();
                if (destructibles != null)
                    destructibles.TakeDamage(cellCenter, damage);
            }
        }

        // Rayon qui traverse : frappe une fois par intervalle chaque cible des cases 0 à count - 1 (à leur niveau)
        private void DamageAll(int count)
        {
            var hits = new System.Collections.Generic.List<(Collider2D collider, Vector2 cellCenter)>();
            for (int i = 0; i < count && i < maxLength; i++)
            {
                foreach (var hit in Physics2D.OverlapCircleAll(CellCenter(i), hitRadius, hitLayers))
                {
                    if (HeightLevel.IsOnLevel(hit, cellLevels[i]))
                        hits.Add((hit, CellCenter(i)));
                }
            }

            if (hits.Count == 0)
            {
                // Rien sur le trajet : le prochain contact frappe tout de suite
                damageTimer = 0f;
                return;
            }

            damageTimer -= Time.deltaTime;
            if (damageTimer > 0f)
                return;
            damageTimer = damageInterval;

            // Une même cible peut avoir plusieurs colliders : on ne la frappe qu'une fois
            var hitTargets = new System.Collections.Generic.List<Health>();
            foreach (var (hit, cellCenter) in hits)
            {
                var health = hit.GetComponentInParent<Health>();
                if (health != null && !hitTargets.Contains(health))
                {
                    hitTargets.Add(health);
                    PlayerCombat.Hit(health, damage);
                }
                if (canDestroyObstacles && hit.TryGetComponent(out DestructibleTilemap destructibles))
                    destructibles.TakeDamage(cellCenter, damage);
            }
        }

        // Image manquante (Final Flash : ni tête, ni impact, ni traînée ; Makankosappo : ni tête, ni traînée) : le corps du rayon
        private Sprite OrBody(Sprite sprite)
        {
            return sprite != null ? sprite : CurrentBody();
        }

        // Image du corps à cet instant : en alternance si bodySprites est rempli
        private Sprite CurrentBody()
        {
            if (bodySprites.Length == 0)
                return bodySprite;
            return bodySprites[(int)(Time.time * animationFramesPerSecond) % bodySprites.Length];
        }

        private void UpdateSprites(int visible, bool isHitting, int trailIndex)
        {
            int frame = impactSprites.Length > 0 ? (int)(Time.time * animationFramesPerSecond) % impactSprites.Length : 0;
            Sprite impactSprite = impactSprites.Length > 0 ? impactSprites[frame] : null;
            int last = visible - 1;

            for (int i = 0; i < cells.Length; i++)
            {
                var sr = cells[i];
                bool erased = IsReleased && i < trailIndex;
                if (i >= visible || erased)
                {
                    sr.enabled = false;
                    continue;
                }

                // Case qui apparaît : début de son ouverture
                if (!sr.enabled)
                    cellAppearTimes[i] = Time.time;
                sr.enabled = true;
                int cellLevel = cellLevels[i / segmentsPerCell];
                sr.sortingOrder = sortingOrder + cellLevel * LevelMap.SortingOrdersPerLevel;
                // Joueur au rang 0 de la tranche de son niveau : un rang en dessous, il est dessiné devant le départ
                if (i == 0 && (startBehindPlayerDirections & DirectionSide(direction)) != 0)
                    sr.sortingOrder = cellLevel * LevelMap.SortingOrdersPerLevel - 1;
                if (IsReleased && i == trailIndex && i != last)
                    sr.sprite = OrBody(releaseTrailSprite);
                else if (i == 0 && startSprites.Length > 0)
                    sr.sprite = OrBody(startSprites[(int)(Time.time * animationFramesPerSecond) % startSprites.Length]);
                else if (i == 0 && startSprite != null)
                    sr.sprite = startSprite;
                else if (i == last && (isHitting || (impactAtTip && !IsReleased)))
                    sr.sprite = OrBody(impactSprite);
                else if (i == last && headSprites.Length > 0)
                    sr.sprite = headSprites[(int)(Time.time * animationFramesPerSecond) % headSprites.Length];
                else if (i == last)
                    sr.sprite = OrBody(headSprite);
                else
                    sr.sprite = CurrentBody();
                SetThickness(sr, OpenFactor(i));
            }
            UpdateStartFiller();
        }

        // Bout du rayon avancé de targetOverlapPixels dans la case de la cible ; les autres morceaux restent à leur place
        private void UpdateTipOverlap(int visible, bool overlapping)
        {
            if (targetOverlapPixels <= 0)
                return;
            int last = visible - 1;
            // Au plus un morceau, sinon le bouchage ne suffit plus
            float overlap = Mathf.Min(targetOverlapPixels / 32f, 1f / segmentsPerCell);
            bool behindTarget = (tipBehindTargetDirections & DirectionSide(direction)) != 0;

            // Seul le départ est affiché (Final Flash à une case libre de la cible) : il reste dans les mains,
            // un bout de corps raccourci à la longueur de l'avancée est posé devant lui, dans la case de la cible
            bool extendStart = overlapping && last == 0;
            overlapping &= last > 0;
            tipExtension.enabled = extendStart && cells[0].enabled;
            if (tipExtension.enabled)
            {
                var start = cells[0];
                // Du bord avant du départ (reculé vers les mains, vers le haut / bas) jusqu'à l'avancée dans la case de la cible
                float from = segmentPositions[0].x + 0.5f / segmentsPerCell;
                float to = 0.5f + overlap;
                tipExtension.transform.localPosition = new Vector3((from + to) / 2f, segmentPositions[0].y, 0f);
                tipExtension.sprite = CurrentBody();
                // Corps fait de colonnes identiques (Final Flash) : raccourci sans changer d'aspect
                tipExtension.transform.localScale = new Vector3((to - from) * segmentsPerCell, start.transform.localScale.y, 1f);
                tipExtension.sortingOrder = behindTarget
                    ? cellLevels[0] * LevelMap.SortingOrdersPerLevel - 1
                    : sortingOrder + cellLevels[0] * LevelMap.SortingOrdersPerLevel;
            }

            for (int i = 0; i < cells.Length; i++)
                cells[i].transform.localPosition = segmentPositions[i] + (overlapping && i == last ? new Vector3(overlap, 0f, 0f) : Vector3.zero);

            // Personnages au rang 0 de la tranche de leur niveau : un rang en dessous, la cible est dessinée devant le bout
            if (overlapping && behindTarget)
                cells[last].sortingOrder = cellLevels[last / segmentsPerCell] * LevelMap.SortingOrdersPerLevel - 1;

            // Bouchage collé au bord arrière du bout avancé (pas dessous : il se verrait dans ses parties transparentes)
            endFiller.enabled = overlapping;
            if (endFiller.enabled)
                endFiller.transform.localPosition = segmentPositions[last] + new Vector3(overlap - 1f / segmentsPerCell, 0f, 0f);
            UpdateEndFiller(visible);
        }

        // Le bouchage du bout prend le corps, l'épaisseur et l'ordre du morceau d'avant, juste en dessous
        private void UpdateEndFiller(int visible)
        {
            if (endFiller == null || !endFiller.enabled)
                return;
            int last = visible - 1;
            var previous = cells[last - 1];
            var tip = cells[last];
            // Traînée arrivée sur le morceau d'avant : plus rien à boucher
            if (!tip.enabled || !previous.enabled)
            {
                endFiller.enabled = false;
                return;
            }
            endFiller.sprite = CurrentBody();
            endFiller.sortingOrder = Mathf.Min(previous.sortingOrder, tip.sortingOrder) - 1;
            endFiller.transform.localScale = tip.transform.localScale;
        }

        // Le morceau de bouchage n'a de sens qu'entre le départ et le morceau suivant : affiché quand les deux le sont
        // (sinon, collé à un mur, il dépasserait devant le départ), avec l'épaisseur du morceau suivant, sous les deux
        private void UpdateStartFiller()
        {
            if (startFiller == null)
                return;
            var start = cells[0];
            var next = cells[1];
            startFiller.enabled = start.enabled && next.enabled;
            if (!startFiller.enabled)
                return;
            startFiller.sprite = CurrentBody();
            startFiller.sortingOrder = Mathf.Min(start.sortingOrder, next.sortingOrder) - 1;
            startFiller.transform.localScale = next.transform.localScale;
        }
    }
}
