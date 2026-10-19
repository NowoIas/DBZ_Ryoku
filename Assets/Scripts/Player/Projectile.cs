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

    // Tiré par le joueur (et non par un ennemi) : ses dégâts comptent comme un combat (vol interdit un moment)
    public bool FiredByPlayer { get; set; }

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

    public void Launch(Vector2 dir, int launchLevel)
    {
        direction = dir;
        spriteRenderer = GetComponent<SpriteRenderer>();
        baseSortingOrder = spriteRenderer.sortingOrder;
        SetLevel(launchLevel);

        if (rotateWithDirection) frames = framesRight;
        else if (dir.y > 0) frames = framesUp;
        else if (dir.y < 0) frames = framesDown;
        else if (dir.x < 0) frames = framesLeft;
        else frames = framesRight;
        if (rotateWithDirection)
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        Vector2Int offset = dir.y > 0 ? upOffsetPixels : dir.y < 0 ? downOffsetPixels : dir.x < 0 ? leftOffsetPixels : rightOffsetPixels;
        transform.position += new Vector3(offset.x, offset.y, 0f) / 32f;
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
        Collider2D contact = HeightLevel.FirstOnLevel(Physics2D.OverlapCircleAll(transform.position, hitRadius, hitLayers), level);
        if (contact != null)
            Hit(contact, contact.ClosestPoint(transform.position));
    }

    private void Update()
    {
        if (hasHit)
            return;

        // On teste tout le trajet de l'image, sinon un projectile rapide pourrait traverser un mur fin
        float distance = speed * Time.deltaTime;

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
            // Case pleine : impact dedans, comme sur un obstacle ; mur ou sens unique : avant le côté bloqué
            Vector2Int next = cell + Vector2Int.RoundToInt(direction);
            HitWall(CollisionMap.IsSolidCell(CellCenter(next), level) ? next : cell);
            return;
        }

        // Premier objet touché à son niveau de hauteur (un tir sur le pont passe au-dessus de ce qui est dessous)
        foreach (RaycastHit2D hit in Physics2D.CircleCastAll(transform.position, hitRadius, direction, distance, hitLayers))
        {
            if (!HeightLevel.IsOnLevel(hit.collider, level))
                continue;
            transform.position = hit.centroid;
            Hit(hit.collider, hit.point);
            return;
        }

        transform.position += (Vector3)(direction * distance);
        traveled += distance;
        if (lastStep)
        {
            Destroy(gameObject);
            return;
        }
        UpdateFrame();
    }

    // Une nouvelle image toutes les cellsPerFrame cases parcourues, puis la boucle des dernières (loopFrameCount)
    private void UpdateFrame()
    {
        if (frames == null || frames.Length == 0)
            return;
        int index = (int)(traveled / cellsPerFrame);
        int firstLoopFrame = frames.Length - Mathf.Min(loopFrameCount, frames.Length);
        if (loopFrameCount > 0 && index >= firstLoopFrame)
        {
            float loopStart = firstLoopFrame * cellsPerFrame;
            index = firstLoopFrame + (int)((traveled - loopStart) / loopCellsPerFrame) % (frames.Length - firstLoopFrame);
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
    private bool IsBlockedBefore(Vector2 leadingEdge)
    {
        Vector2Int target = CellOf(leadingEdge);
        Vector2Int step = Vector2Int.RoundToInt(direction);
        while (cell != target)
        {
            if (!CollisionMap.CanTechniquePass(CellCenter(cell), direction, level))
                return true;
            cell += step;
            SetLevel(LevelMap.LevelAfterEntering(CellCenter(cell), level));
        }
        return false;
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
