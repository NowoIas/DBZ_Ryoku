using DBZ.Combat;
using DBZ.World;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class Projectile : MonoBehaviour
{
    public float speed = 10f;
    // Portée en cases : le projectile disparaît au bout de cette distance, quelle que soit sa vitesse (0 = durée de vie seule)
    public float maxDistance = 0f;
    // Durée de vie en secondes avant destruction automatique (utilisée seulement si maxDistance = 0)
    public float lifetime = 1f;
    public float damage = 10f;
    // Peut casser les tuiles du calque Destructibles (rochers, cailloux...)
    public bool canDestroyObstacles = true;
    // Effet joué au centre de la case touchée (obstacle ou ennemi)
    public OneShotEffect impactPrefab;

    // Layers qui arrêtent le projectile : obstacles et cibles (SolidObjects)
    public LayerMask hitLayers;
    // Rayon de la zone de contact du projectile (en cases)
    public float hitRadius = 0.2f;

    // Images du projectile selon la direction, jouées une fois au fil du trajet (la dernière reste ensuite)
    public Sprite[] framesUp;
    public Sprite[] framesDown;
    public Sprite[] framesLeft;
    public Sprite[] framesRight;
    // Distance parcourue (en cases) entre deux images : l'animation suit le trajet, pas le temps
    [Min(0.1f)]
    public float cellsPerFrame = 2f;
    // Les N dernières images jouées en boucle une fois les autres passées (0 = la dernière reste ; Burning Blast : 2)
    [Min(0)]
    public int loopFrameCount = 0;
    // Distance (en cases) entre deux images de la boucle
    [Min(0.1f)]
    public float loopCellsPerFrame = 1f;

    // Images dessinées vers la droite (Frames Right), tournées selon la direction du tir (Volleyball de Gotenks) ;
    // décochée : une liste d'images par direction
    public bool rotateWithDirection = false;
    [Tooltip("Rotate With Direction : image dessinée vers un autre côté que la droite (180 = vers la gauche : crachat de Dabura)")]
    public float rotationOffsetDegrees = 0f;
    [Tooltip("Une image par direction, 8 directions (remplace les listes ci-dessus si remplie), dans l'ordre : bas, bas-droite, " +
             "droite, haut-droite, haut, haut-gauche, gauche, bas-gauche (boules de la toupie de Raditz)")]
    public Sprite[] framesEightDirections;
    // Décalage du projectile à son départ, en pixels (x vers la droite, y vers le haut), selon la direction du tir
    public Vector2Int upOffsetPixels;
    public Vector2Int downOffsetPixels;
    public Vector2Int leftOffsetPixels;
    public Vector2Int rightOffsetPixels;

    // Ennemi ou cible touché : transformé en ballon pendant ce temps (secondes, 0 = non), sans impact ;
    // un obstacle garde l'impact (Volleyball de Gotenks)
    [Min(0f)]
    public float lockDuration = 0f;
    public Sprite lockSprite;
    // Icône de l'état, sur la case au-dessus de la cible
    public Sprite lockIcon;

    // Ennemi ou cible touché : brûlé pendant ce temps (secondes, 0 = non), dégâts répétés (Burning Blast de Trunks)
    [Min(0f)]
    public float burnDuration = 0f;
    [Min(0f)]
    public float burnDamage = 5f;
    [Min(0.05f)]
    public float burnInterval = 0.5f;
    public Sprite burnIcon;
    [Tooltip("Icône animée (remplace burnIcon si remplie) : une image de plus à chaque tic, en boucle")]
    public Sprite[] burnIconFrames;

    [Tooltip("Absorbé par les ennemis qui absorbent le Ki (KiAbsorber : C19, Dr Gero) : aucun dégât, PV rendus. Ki Blast normal seulement")]
    public bool absorbable = false;

    [Tooltip("Joueur touché : pétrifié pendant ce temps (secondes, 0 = non ; composant Petrification du joueur) : crachat de Dabura")]
    [Min(0f)]
    public float petrifyDuration = 0f;

    [Tooltip("Le projectile laisse un sillage en V sur l'eau qu'il survole (WaterRipples, shader des tilemaps)")]
    public bool makesWaterRipples = true;

    // Tête chercheuse (rayon de Freezer), active seulement si HomingTarget est donné au tir :
    // le projectile ne tourne qu'au centre d'une case, d'un quart de tour vers la cible, jamais en demi-tour,
    // et peu souvent, pour rester esquivable
    [Tooltip("Tête chercheuse : cases à parcourir (depuis le tir ou le dernier virage) avant de pouvoir tourner")]
    [Min(1f)]
    public float homingTurnEveryCells = 2f;
    [Tooltip("Tête chercheuse : nombre de virages au plus")]
    [Min(0)]
    public int homingMaxTurns = 3;

    // Tiré par le joueur (et non par un ennemi) : ses dégâts comptent comme un combat (vol interdit un moment)
    public bool FiredByPlayer { get; set; }
    // Tireur (ennemi) : ses colliders sont ignorés (un tir en diagonale part encore dans sa case)
    public Transform Owner { get; set; }
    // Cible suivie (tête chercheuse) ; vide = tir droit
    public Transform HomingTarget { get; set; }

    private Vector2 direction;
    private Sprite[] frames;
    // Distance parcourue depuis l'apparition, en cases
    private float traveled;
    private bool hasHit;
    // Case où se trouve l'avant du projectile : les calques de collision sont testés à chaque changement de case
    private Vector2Int cell;
    // Niveau de hauteur de cette case : celui du joueur au tir, puis suit les tuiles Niveaux comme le joueur
    private int level;
    private SpriteRenderer spriteRenderer;
    // Ordre d'affichage réglé dans le prefab = celui du niveau 0 ; décalé selon le niveau du projectile
    private int baseSortingOrder;
    // Point de départ du trajet (ou du dernier virage), pour le sillage sur l'eau
    private Vector2 waterStart;
    // Distance parcourue au dernier virage (tête chercheuse) : les images repartent de la première après un virage
    private float segmentStart;
    private int turnsDone;

    public void Launch(Vector2 dir, int launchLevel)
    {
        direction = dir;
        spriteRenderer = GetComponent<SpriteRenderer>();
        baseSortingOrder = spriteRenderer.sortingOrder;
        SetLevel(launchLevel);

        ChooseFrames(dir);
        Vector2Int offset = dir.y > 0 ? upOffsetPixels : dir.y < 0 ? downOffsetPixels : dir.x < 0 ? leftOffsetPixels : rightOffsetPixels;
        transform.position += new Vector3(offset.x, offset.y, 0f) / 32f;
        waterStart = transform.position;
        UpdateFrame();

        if (maxDistance <= 0f)
            Destroy(gameObject, lifetime);

        // Le projectile apparaît dans la case devant le joueur : on part de la case du joueur.
        // Bloqué dès la sortie (collé à un mur) : impact sur la case devant, pour rester visible
        Vector2Int step = Vector2Int.RoundToInt(direction);
        cell = CellOf(transform.position) - step;
        if (IsBlockedBefore(LeadingEdge(0f)))
        {
            HitWall(cell + step);
            return;
        }

        // Tiré à bout portant contre une cible ou un Destructible (à son niveau) : il touche tout de suite
        Collider2D contact = null;
        foreach (Collider2D c in Physics2D.OverlapCircleAll(transform.position, hitRadius, hitLayers))
        {
            if (!IsOwner(c) && HeightLevel.IsOnLevel(c, level))
            {
                contact = c;
                break;
            }
        }
        if (contact != null)
            Hit(contact, contact.ClosestPoint(transform.position));
    }

    // Images (et rotation) selon la direction du tir
    private void ChooseFrames(Vector2 dir)
    {
        if (rotateWithDirection) frames = framesRight;
        else if (dir.y > 0) frames = framesUp;
        else if (dir.y < 0) frames = framesDown;
        else if (dir.x < 0) frames = framesLeft;
        else frames = framesRight;
        if (framesEightDirections != null && framesEightDirections.Length == 8)
        {
            // Bas = 0, puis 45° de plus à chaque image dans le sens inverse des aiguilles d'une montre
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            int index = Mathf.RoundToInt((angle + 90f) / 45f);
            frames = new[] { framesEightDirections[((index % 8) + 8) % 8] };
        }
        if (rotateWithDirection)
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + rotationOffsetDegrees);
    }

    private void Update()
    {
        if (hasHit)
            return;

        float remaining = speed * Time.deltaTime;
        // Tête chercheuse : le trajet de l'image est coupé au centre de chaque case, seul endroit où il peut tourner
        while (remaining > 0f)
        {
            float step = remaining;
            bool atCenter = false;
            if (IsHoming)
            {
                float toCenter = DistanceToNextCellCenter();
                if (toCenter <= step)
                {
                    step = toCenter;
                    atCenter = true;
                }
            }
            if (!Advance(step))
                return;
            remaining -= step;
            if (atCenter)
                TryTurnTowardTarget();
        }
        UpdateFrame();
    }

    // Tir droit (pas en diagonale) avec une cible et des virages restants
    private bool IsHoming => HomingTarget != null && turnsDone < homingMaxTurns && (direction.x == 0f || direction.y == 0f);

    // Distance jusqu'au prochain centre de case devant (toujours > 0 : posé sur un centre, il vise le suivant)
    private float DistanceToNextCellCenter()
    {
        float along = direction.x != 0f ? transform.position.x * direction.x : transform.position.y * direction.y;
        float nextCenter = Mathf.Floor(along - 0.5f + 0.001f) + 1.5f;
        return nextCenter - along;
    }

    // Au centre d'une case : quart de tour vers la cible si elle est plus loin sur le côté que devant
    // (cible derrière mais décalée : il tourne aussi ; pile derrière : il continue, jamais de demi-tour)
    private void TryTurnTowardTarget()
    {
        // Recalé exactement sur le centre (les petites erreurs de calcul s'additionneraient)
        Vector3 p = transform.position;
        if (direction.x != 0f) p.x = Mathf.Floor(p.x) + 0.5f;
        else p.y = Mathf.Floor(p.y) + 0.5f;
        transform.position = p;

        if (traveled - segmentStart < homingTurnEveryCells - 0.01f)
            return;
        Vector2 target = CellCenter(CellOf(HomingTarget.position));
        Vector2 delta = target - (Vector2)p;
        Vector2 side = new Vector2(-direction.y, direction.x);
        float forward = Vector2.Dot(delta, direction);
        float lateral = Vector2.Dot(delta, side);
        if (Mathf.Abs(lateral) < 0.5f || Mathf.Abs(lateral) <= forward)
            return;

        direction = side * Mathf.Sign(lateral);
        turnsDone++;
        segmentStart = traveled;
        ChooseFrames(direction);
        // Le sillage sur l'eau repart du virage
        if (makesWaterRipples)
        {
            WaterRipples.EndProjectile(this);
            waterStart = p;
        }
    }

    // Avance de cette distance (murs, cibles, portée) ; faux si le projectile a disparu
    private bool Advance(float distance)
    {
        // On teste tout le trajet, sinon un projectile rapide pourrait traverser un mur fin

        // Bout de la portée : il disparaît sans impact
        bool lastStep = false;
        if (maxDistance > 0f && traveled + distance >= maxDistance)
        {
            distance = maxDistance - traveled;
            lastStep = true;
        }

        // Murs et sens uniques des calques de collision, testés avant la physique
        // pour ne pas toucher une cible derrière un mur
        if (IsBlockedBefore(LeadingEdge(distance)))
        {
            // Case pleine (ou montagne) : impact dedans, comme sur un obstacle ; mur ou sens unique : avant le côté bloqué
            Vector2Int next = cell + Vector2Int.RoundToInt(direction);
            HitWall(CollisionMap.IsTechniqueSolidCell(CellCenter(next), level) ? next : cell);
            return false;
        }

        // Premier objet touché à son niveau de hauteur (un tir sur le pont passe au-dessus de ce qui est dessous)
        foreach (RaycastHit2D hit in Physics2D.CircleCastAll(transform.position, hitRadius, direction, distance, hitLayers))
        {
            if (IsOwner(hit.collider) || !HeightLevel.IsOnLevel(hit.collider, level))
                continue;
            transform.position = hit.centroid;
            Hit(hit.collider, hit.point);
            return false;
        }

        transform.position += (Vector3)(direction * distance);
        traveled += distance;
        if (makesWaterRipples)
            WaterRipples.SetProjectile(this, waterStart, transform.position);
        if (lastStep)
        {
            Destroy(gameObject);
            return false;
        }
        return true;
    }

    // Projectile disparu (impact, bout de portée, changement de scène) : son sillage finit de s'écarter
    private void OnDestroy()
    {
        WaterRipples.EndProjectile(this);
    }

    // Une nouvelle image toutes les cellsPerFrame cases parcourues, puis la boucle des dernières (loopFrameCount)
    private void UpdateFrame()
    {
        if (frames == null || frames.Length == 0)
            return;
        // Depuis le tir, ou depuis le dernier virage (tête chercheuse : le rayon repart court)
        float distance = traveled - segmentStart;
        int index = (int)(distance / cellsPerFrame);
        int firstLoopFrame = frames.Length - Mathf.Min(loopFrameCount, frames.Length);
        if (loopFrameCount > 0 && index >= firstLoopFrame)
        {
            float loopStart = firstLoopFrame * cellsPerFrame;
            index = firstLoopFrame + (int)((distance - loopStart) / loopCellsPerFrame) % (frames.Length - firstLoopFrame);
        }
        else
        {
            index = Mathf.Min(index, frames.Length - 1);
        }
        spriteRenderer.sprite = frames[index];
    }

    // Au sol, le projectile passe sous le pont ; en haut, par-dessus
    private void SetLevel(int newLevel)
    {
        level = newLevel;
        spriteRenderer.sortingOrder = baseSortingOrder + level * LevelMap.SortingOrdersPerLevel;
    }

    // Impact affiché au niveau du projectile (son ordre réglé dans le prefab = celui du niveau 0)
    private void SpawnImpact(Vector3 position)
    {
        var impact = Instantiate(impactPrefab, position, Quaternion.identity);
        impact.SetDirection(direction);
        if (impact.TryGetComponent(out SpriteRenderer impactRenderer))
            impactRenderer.sortingOrder += level * LevelMap.SortingOrdersPerLevel;
    }

    private static Vector2Int CellOf(Vector2 position)
    {
        return new Vector2Int(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y));
    }

    private static Vector2 CellCenter(Vector2Int c)
    {
        return new Vector2(c.x + 0.5f, c.y + 0.5f);
    }

    // Avant du projectile (bord de sa zone de contact) après avoir avancé de cette distance
    private Vector2 LeadingEdge(float distance)
    {
        return (Vector2)transform.position + direction * (distance + hitRadius);
    }

    // Avance case par case jusqu'à celle de l'avant du projectile ; vrai si un côté franchi est bloqué
    // (cell reste alors sur la dernière case libre)
    // En diagonale, le pas passe si l'un des deux chemins en « L » (côté puis haut / bas, ou l'inverse) est libre
    private bool IsBlockedBefore(Vector2 leadingEdge)
    {
        Vector2Int target = CellOf(leadingEdge);
        while (cell != target)
        {
            Vector2Int step = new Vector2Int(System.Math.Sign(target.x - cell.x), System.Math.Sign(target.y - cell.y));
            if (step.x != 0 && step.y != 0)
            {
                if (!CanPassDiagonal(cell, step))
                    return true;
            }
            else if (!CollisionMap.CanTechniquePass(CellCenter(cell), step, level))
            {
                return true;
            }
            cell += step;
            SetLevel(LevelMap.LevelAfterEntering(CellCenter(cell), level));
        }
        return false;
    }

    private bool CanPassDiagonal(Vector2Int from, Vector2Int step)
    {
        Vector2 x = new Vector2(step.x, 0f);
        Vector2 y = new Vector2(0f, step.y);
        Vector2 start = CellCenter(from);
        bool xThenY = CollisionMap.CanTechniquePass(start, x, level) && CollisionMap.CanTechniquePass(start + x, y, level);
        bool yThenX = CollisionMap.CanTechniquePass(start, y, level) && CollisionMap.CanTechniquePass(start + y, x, level);
        return xThenY || yThenX;
    }

    private bool IsOwner(Collider2D c)
    {
        return Owner != null && c.transform.IsChildOf(Owner);
    }

    // Arrêté par un calque de collision (case pleine, mur, sens unique) : pas de dégâts, impact au centre de la case
    private void HitWall(Vector2Int impactCell)
    {
        hasHit = true;
        if (impactPrefab != null)
            SpawnImpact(CellCenter(impactCell));
        Destroy(gameObject);
    }

    // contactPoint : point de contact, sur le bord de la cible
    private void Hit(Collider2D target, Vector2 contactPoint)
    {
        hasHit = true;

        var health = target.GetComponentInParent<Health>();

        // Cible qui absorbe le Ki (C19, Dr Gero) : ni dégâts ni impact, elle reprend des PV
        if (absorbable && health != null && !health.IsDead && health.TryGetComponent(out KiAbsorber absorber))
        {
            absorber.Absorb(damage);
            Destroy(gameObject);
            return;
        }

        if (health != null && FiredByPlayer)
            PlayerCombat.Hit(health, damage);
        else if (health != null)
            health.TakeDamage(damage);

        // Le point de contact est sur le bord de la case : on avance un peu pour tomber dedans
        Vector2 insideHitCell = contactPoint + direction * 0.1f;

        if (canDestroyObstacles)
        {
            var destructibles = target.GetComponent<DestructibleTilemap>();
            if (destructibles != null)
                destructibles.TakeDamage(insideHitCell, damage);
        }

        if (petrifyDuration > 0f && health != null && !health.IsDead && health.TryGetComponent(out Petrification petrification))
            petrification.TryPetrify(petrifyDuration);

        if (burnDuration > 0f && health != null && !health.IsAlly && !health.IsDead)
            StatusEffects.Of(health).AddDamageOverTime(StatusEffects.Burn, burnDuration, burnDamage, burnInterval, burnIcon, burnIconFrames);

        // Ennemi ou cible (pas un allié) encore en vie : transformé en ballon, sans impact
        if (lockDuration > 0f && health != null && !health.IsAlly && !health.IsDead)
        {
            StatusEffects.Of(health).Lock(lockDuration, lockSprite, lockIcon);
            Destroy(gameObject);
            return;
        }

        if (impactPrefab != null)
        {
            var cellCenter = new Vector3(Mathf.Floor(insideHitCell.x) + 0.5f, Mathf.Floor(insideHitCell.y) + 0.5f, 0f);
            SpawnImpact(cellCenter);
        }

        Destroy(gameObject);
    }
}
