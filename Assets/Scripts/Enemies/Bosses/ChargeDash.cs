using System.Collections;
using System.Collections.Generic;
using DBZ.Combat;
using DBZ.World;
using UnityEngine;

namespace DBZ.Enemies
{
    // Attaque spéciale de Cell semi-parfait (2026-11-26, demande util.) : aligné avec Goku, il annonce sa charge
    // (« ! » au-dessus de la tête, chevrons qui défilent au sol sur toute la trajectoire, il tremble), charge un court
    // instant (planche s1 : images 0-1), puis fonce en ligne droite (image 1 seule, images rémanentes derrière lui)
    // jusqu'à Goku, un obstacle ou sa portée maximale, et finit sur les images 2-3.
    // Goku touché : dégâts, Goku ne bouge pas, Cell s'arrête devant lui. Raté : Cell s'arrête, sans étourdissement.
    // Plus difficile à esquiver (2026-11-26, choix util.) : pendant la charge, Cell se retourne vers Goku s'il change
    // d'axe (direction bloquée aimLockTime avant le départ) ; la ruée a 3 cases de large (hitHalfWidth) : Goku est
    // touché aussi sur les cases voisines de la trajectoire (réglé à 0 : une seule ligne, choix util. après essai).
    // Puis (choix util.) ruée plus rapide et relance : ratée, Cell se recale d'abord sur la ligne de Goku (glisse sur le
    // côté de maxRealignCells cases au plus, à la vitesse de la ruée), puis repart vers lui après une annonce courte.
    // Trace au sol (style A choisi par l'util., faite avec pixelfx : effet impacts/dash-trail) : un morceau par case
    // traversée, posé quand Cell passe au milieu de la case, tourné dans le sens de la ruée (variante tirée au hasard).
    // L'Animator est coupé pendant l'attaque (images posées par ce script), rendu à la fin.
    public class ChargeDash : EnemySpecialAttack
    {
        [Header("Charge")]
        [Tooltip("Distance minimale de Goku pour charger, en cases (la portée maximale est Range)")]
        [SerializeField] private int minDistance = 2;
        [Tooltip("Images de la planche s1 par direction : 0-1 charge, 1 ruée, 2-3 arrivée")]
        [SerializeField] private Sprite[] spritesUp = new Sprite[4];
        [SerializeField] private Sprite[] spritesDown = new Sprite[4];
        [SerializeField] private Sprite[] spritesLeft = new Sprite[4];
        [SerializeField] private Sprite[] spritesRight = new Sprite[4];
        [Tooltip("Durée de la charge (annonce comprise), en secondes : le temps qu'a Goku pour s'écarter")]
        [SerializeField] private float chargeDuration = 0.6f;
        [Tooltip("Images de charge (0 ↔ 1) par seconde")]
        [SerializeField] private float chargeFramesPerSecond = 8f;
        [Tooltip("Direction bloquée ce temps avant la ruée (secondes) ; avant, Cell se retourne vers Goku s'il change d'axe")]
        [SerializeField] private float aimLockTime = 0.2f;
        [Tooltip("Demi-largeur de la ruée, en cases : 1 = Goku touché aussi sur les 2 cases voisines de la trajectoire")]
        [SerializeField] private int hitHalfWidth = 1;
        [Tooltip("Tremblement pendant la charge, en pixels")]
        [SerializeField] private int shakePixels = 1;
        [Tooltip("Temps entre deux secousses, en secondes")]
        [SerializeField] private float shakeStepTime = 0.05f;

        [Header("Relance")]
        [Tooltip("Ruées supplémentaires si la précédente rate : Cell se retourne aussitôt vers Goku et repart (0 = aucune)")]
        [SerializeField] private int followUpCharges = 1;
        [Tooltip("Durée de l'annonce d'une relance, en secondes")]
        [SerializeField] private float followUpChargeDuration = 0.3f;
        [Tooltip("Relance : décalage maximal (cases) dont Cell glisse sur le côté pour se mettre sur la ligne de Goku ; au-delà, pas de relance")]
        [SerializeField] private int maxRealignCells = 2;

        [Header("Ruée")]
        [Tooltip("Vitesse de la ruée, en cases par seconde (Goku marche à 4)")]
        [SerializeField] private float dashCellsPerSecond = 14f;
        [Tooltip("Longueur maximale de la ruée, en cases")]
        [SerializeField] private int maxDashCells = 8;
        [Tooltip("Dégâts à Goku s'il est percuté")]
        [SerializeField] private float damage = 20f;
        [Tooltip("Images d'arrivée (2 puis 3) par seconde")]
        [SerializeField] private float arrivalFramesPerSecond = 8f;

        [Header("Images rémanentes (vitesse)")]
        [Tooltip("Une copie de Cell laissée toutes les ... cases")]
        [SerializeField] private float afterimageSpacing = 0.5f;
        [Tooltip("Opacité d'une copie à sa naissance")]
        [SerializeField, Range(0f, 1f)] private float afterimageAlpha = 0.5f;
        [Tooltip("Durée de l'effacement d'une copie, en secondes")]
        [SerializeField] private float afterimageFade = 0.15f;

        [Header("Annonce")]
        [Tooltip("« ! » au-dessus de la tête")]
        [SerializeField] private Sprite alertSprite;
        [Tooltip("Position du « ! » depuis le centre de la case, en pixels")]
        [SerializeField] private Vector2Int alertOffsetPixels = new Vector2Int(0, 46);
        [Tooltip("Rebond du « ! » à son apparition, en pixels")]
        [SerializeField] private int alertBouncePixels = 2;
        [Tooltip("Chevron posé au sol sur chaque case de la trajectoire (dessiné vers la droite, tourné par code)")]
        [SerializeField] private Sprite chevronSprite;
        [Tooltip("Défilement des chevrons vers la cible, en cases par seconde")]
        [SerializeField] private float chevronCellsPerSecond = 12f;
        [Tooltip("Nombre de chevrons allumés ensemble dans la vague")]
        [SerializeField] private int chevronLitCount = 3;
        [Tooltip("Ordre d'affichage des chevrons au niveau 0 (calque background, au-dessus des Decor)")]
        [SerializeField] private int groundSortingOrder = 20;

        [Header("Trace au sol")]
        [Tooltip("Morceaux de trace (une case, OneShotEffect) : une variante tirée au hasard par case ; vide = pas de trace")]
        [SerializeField] private GameObject[] trailPrefabs;
        [Tooltip("Trace sous le centre de la case, en pixels (pieds), pour une ruée à gauche / à droite")]
        [SerializeField] private int trailFeetPixels = 12;

        private const float PixelsPerUnit = 32f;
        private static readonly int IdleState = Animator.StringToHash("Idle");

        private EnemyController controller;
        private Health health;
        private SpriteRenderer spriteRenderer;
        private Animator animator;
        private readonly List<SpriteRenderer> chevrons = new List<SpriteRenderer>();
        // Rang de chaque chevron sur la trajectoire (0 = case devant Cell), pour la vague
        private readonly List<int> chevronRows = new List<int>();
        private int laneLength;
        private SpriteRenderer alert;
        // Attaque en cours (sert à tout remettre en ordre si la coroutine est coupée : ballon, stase, mort)
        private bool running;
        // Attaque coupée par la mort de Cell (les étapes s'arrêtent)
        private bool aborted;
        // Direction de la dernière ruée : l'Animator rendu regarde de ce côté
        private Vector2 lastDirection;
        private static readonly int MoveX = Animator.StringToHash("moveX");
        private static readonly int MoveY = Animator.StringToHash("moveY");
        private int lastTickFrame;

        private void Awake()
        {
            controller = GetComponent<EnemyController>();
            health = GetComponent<Health>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            animator = GetComponent<Animator>();
        }

        // Goku dans la bande de la ruée (son axe, à hitHalfWidth case près), à bonne distance, même niveau,
        // rien sur le chemin jusqu'à lui
        public override bool CanUse(Vector2Int delta, bool sameLevel)
        {
            if (!sameLevel)
                return false;
            Vector2 direction = DirectionToward(delta, Vector2.zero);
            if (direction == Vector2.zero)
                return false;
            int distance = Mathf.Abs(Mathf.RoundToInt(Vector2.Dot(direction, delta)));
            if (distance < minDistance || distance > range)
                return false;
            return LaneLength(direction, distance - 1) >= distance - 1;
        }

        // Axe de la ruée vers Goku : celui où il est dans la bande (l'axe actuel d'abord) ; zéro si aucun
        private Vector2 DirectionToward(Vector2Int delta, Vector2 current)
        {
            Vector2 horizontal = Mathf.Abs(delta.y) <= hitHalfWidth && delta.x != 0 ? new Vector2(System.Math.Sign(delta.x), 0f) : Vector2.zero;
            Vector2 vertical = Mathf.Abs(delta.x) <= hitHalfWidth && delta.y != 0 ? new Vector2(0f, System.Math.Sign(delta.y)) : Vector2.zero;
            if (current != Vector2.zero && (current == horizontal || current == vertical))
                return current;
            // Les deux possibles (Goku presque en diagonale tout près) : l'axe le plus long
            if (horizontal != Vector2.zero && vertical != Vector2.zero)
                return Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? horizontal : vertical;
            return horizontal != Vector2.zero ? horizontal : vertical;
        }

        // Cases libres devant Cell dans cette direction (au plus max)
        private int LaneLength(Vector2 direction, int max)
        {
            Vector3 cell = transform.position;
            int cellLevel = controller.Level;
            int count = 0;
            while (count < max && controller.CanDashStep(cell, direction, cellLevel))
            {
                cell += (Vector3)direction;
                cellLevel = LevelMap.LevelAfterEntering(cell, cellLevel);
                count++;
            }
            return count;
        }

        private List<Vector3> ComputeLane(Vector2 direction)
        {
            var lane = new List<Vector3>();
            Vector3 cell = transform.position;
            int cellLevel = controller.Level;
            while (lane.Count < maxDashCells && controller.CanDashStep(cell, direction, cellLevel))
            {
                cell += (Vector3)direction;
                cellLevel = LevelMap.LevelAfterEntering(cell, cellLevel);
                lane.Add(cell);
            }
            return lane;
        }

        // Goku sur cette case ou sur une case voisine de la trajectoire (bande de 2 × hitHalfWidth + 1 cases)
        private bool PlayerInBand(EnemyController owner, Vector3 cell, Vector2 direction)
        {
            Vector3 side = Perpendicular(direction);
            for (int k = -hitHalfWidth; k <= hitHalfWidth; k++)
                if (owner.IsPlayerAt(cell + side * k, owner.Level))
                    return true;
            return false;
        }

        protected override IEnumerator Perform(EnemyController owner)
        {
            Vector2 direction = owner.Facing;
            running = true;
            aborted = false;
            lastDirection = Vector2.zero;
            Tick();
            animator.enabled = false;

            // 1re charge, puis (ratée) une relance aussitôt tournée vers Goku avec une annonce courte
            for (int attempt = 0; attempt <= followUpCharges; attempt++)
            {
                if (attempt > 0)
                {
                    // Recalage sur la ligne de Goku (le plus petit écart, s'il ne dépasse pas maxRealignCells)
                    if (!owner.TryGetPlayerDelta(out Vector2Int delta, out bool sameLevel) || !sameLevel)
                        break;
                    if (delta.x != 0 && delta.y != 0)
                    {
                        bool slideX = Mathf.Abs(delta.x) <= Mathf.Abs(delta.y);
                        int offset = slideX ? delta.x : delta.y;
                        if (Mathf.Abs(offset) > maxRealignCells)
                            break;
                        Vector2 slide = slideX ? new Vector2(System.Math.Sign(offset), 0f) : new Vector2(0f, System.Math.Sign(offset));
                        Vector2 next = slideX ? new Vector2(0f, System.Math.Sign(delta.y)) : new Vector2(System.Math.Sign(delta.x), 0f);
                        bool[] done = { false };
                        yield return Slide(owner, slide, Mathf.Abs(offset), next, done);
                        if (aborted)
                            yield break;
                        if (!done[0] || !owner.TryGetPlayerDelta(out delta, out sameLevel) || !sameLevel)
                            break;
                    }
                    if (delta.x != 0 && delta.y != 0)
                        break;
                    int along = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
                    if (along < 1 || along > range)
                        break;
                    direction = new Vector2(System.Math.Sign(delta.x), System.Math.Sign(delta.y));
                }
                float duration = attempt == 0 ? chargeDuration : followUpChargeDuration;
                Vector2[] aim = { direction };
                yield return Charge(owner, aim, duration);
                if (aborted)
                    yield break;
                direction = aim[0];
                bool[] hit = { false };
                yield return Dash(owner, direction, hit);
                if (aborted)
                    yield break;
                if (hit[0])
                    break;
            }
            lastDirection = direction;

            // Arrivée : images 2 puis 3
            Sprite[] sprites = SpritesFor(direction);
            float frameTime = 1f / Mathf.Max(1f, arrivalFramesPerSecond);
            for (int frame = 2; frame <= 3; frame++)
            {
                SetSprite(sprites, frame);
                float wait = 0f;
                while (wait < frameTime)
                {
                    if (IsDead())
                    {
                        Abort();
                        yield break;
                    }
                    Tick();
                    wait += Time.deltaTime;
                    yield return null;
                }
            }
            Cleanup(true);
        }

        // Annonce + charge sur place (images 0 ↔ 1, tremblement, « ! », chevrons) ; visée jusqu'à aimLockTime du départ
        private IEnumerator Charge(EnemyController owner, Vector2[] aim, float duration)
        {
            Vector2 direction = aim[0];
            Sprite[] sprites = SpritesFor(direction);
            ShowWarning(ComputeLane(direction), direction, owner.Level);
            Vector3 home = transform.position;
            float t = 0f;
            while (t < duration)
            {
                if (IsDead())
                {
                    Abort();
                    yield break;
                }
                // Goku passé sur l'autre axe → Cell se retourne, trajectoire refaite
                if (t < duration - aimLockTime && owner.TryGetPlayerDelta(out Vector2Int delta, out bool sameLevel) && sameLevel)
                {
                    Vector2 aimed = DirectionToward(delta, direction);
                    if (aimed != Vector2.zero && aimed != direction && LaneLength(aimed, 1) > 0)
                    {
                        transform.position = home;
                        direction = aimed;
                        sprites = SpritesFor(direction);
                        ShowWarning(ComputeLane(direction), direction, owner.Level);
                    }
                }
                SetSprite(sprites, Mathf.FloorToInt(t * chargeFramesPerSecond) % 2);
                int step = Mathf.FloorToInt(t / Mathf.Max(0.01f, shakeStepTime));
                float shake = (step % 2 == 0 ? 1 : -1) * shakePixels / PixelsPerUnit;
                transform.position = home + (Vector3)(Perpendicular(direction) * shake);
                UpdateWarning(t);
                Tick();
                t += Time.deltaTime;
                yield return null;
            }
            transform.position = home;
            HideWarning();
            aim[0] = direction;
        }

        // Ruée case par case : Goku sur la trajectoire = percuté (hit[0]) ; obstacle ou portée = arrêt
        private IEnumerator Dash(EnemyController owner, Vector2 direction, bool[] hit)
        {
            Sprite[] sprites = SpritesFor(direction);
            SetSprite(sprites, 1);
            Vector3 current = transform.position;
            SpawnTrail(current, direction, owner.Level);
            float sinceAfterimage = 0f;
            for (int cells = 0; cells < maxDashCells; cells++)
            {
                if (PlayerInBand(owner, current + (Vector3)direction, direction))
                {
                    owner.HitPlayer(damage);
                    hit[0] = true;
                    yield break;
                }
                if (!owner.CanDashStep(current, direction, owner.Level))
                    yield break;
                Vector3 next = current + (Vector3)direction;
                owner.MoveDuringSpecial(transform.position, next);
                while (transform.position != next)
                {
                    if (IsDead())
                    {
                        Abort();
                        yield break;
                    }
                    // Goku entré sur la trajectoire pendant le trajet : percuté, Cell revient sur sa case
                    if (PlayerInBand(owner, next, direction))
                    {
                        owner.HitPlayer(damage);
                        owner.MoveDuringSpecial(current, current);
                        owner.SnapToTargetCell();
                        hit[0] = true;
                        yield break;
                    }
                    float move = dashCellsPerSecond * Time.deltaTime;
                    Vector3 position = Vector3.MoveTowards(transform.position, next, move);
                    sinceAfterimage += Vector3.Distance(position, transform.position);
                    owner.MoveDuringSpecial(position, next);
                    while (sinceAfterimage >= afterimageSpacing)
                    {
                        sinceAfterimage -= afterimageSpacing;
                        SpawnAfterimage(position - (Vector3)(direction * sinceAfterimage));
                    }
                    Tick();
                    yield return null;
                }
                owner.SnapToTargetCell();
                current = next;
                SpawnTrail(current, direction, owner.Level);
            }
        }

        // Glissade sur le côté (recalage avant une relance) : cases libres seulement, à la vitesse de la ruée,
        // images rémanentes ; image de charge tournée vers la prochaine ruée. done[0] faux si bloqué en route
        private IEnumerator Slide(EnemyController owner, Vector2 slide, int cells, Vector2 facing, bool[] done)
        {
            SetSprite(SpritesFor(facing), 0);
            Vector3 current = transform.position;
            float sinceAfterimage = 0f;
            for (int i = 0; i < cells; i++)
            {
                if (owner.IsPlayerAt(current + (Vector3)slide, owner.Level) || !owner.CanDashStep(current, slide, owner.Level))
                    yield break;
                Vector3 next = current + (Vector3)slide;
                owner.MoveDuringSpecial(transform.position, next);
                while (transform.position != next)
                {
                    if (IsDead())
                    {
                        Abort();
                        yield break;
                    }
                    Vector3 position = Vector3.MoveTowards(transform.position, next, dashCellsPerSecond * Time.deltaTime);
                    sinceAfterimage += Vector3.Distance(position, transform.position);
                    owner.MoveDuringSpecial(position, next);
                    while (sinceAfterimage >= afterimageSpacing)
                    {
                        sinceAfterimage -= afterimageSpacing;
                        SpawnAfterimage(position - (Vector3)(slide * sinceAfterimage));
                    }
                    Tick();
                    yield return null;
                }
                owner.SnapToTargetCell();
                current = next;
            }
            done[0] = true;
        }

        // Mort pendant l'attaque : tout est rangé, l'Animator reste coupé (animation de mort)
        private void Abort()
        {
            aborted = true;
            Cleanup(false);
        }

        // Coroutine coupée de l'extérieur (ballon, étourdissement, stase) : tout est remis en ordre à l'image suivante
        private void LateUpdate()
        {
            if (running && lastTickFrame < Time.frameCount - 1)
            {
                controller.SnapToTargetCell();
                Cleanup(!IsDead());
            }
        }

        private void OnDisable()
        {
            if (running)
                Cleanup(false);
        }

        private void Tick() => lastTickFrame = Time.frameCount;

        private bool IsDead() => health == null || health.IsDead;

        private void Cleanup(bool restoreAnimator)
        {
            running = false;
            HideWarning();
            if (restoreAnimator && animator != null)
            {
                animator.enabled = true;
                if (lastDirection != Vector2.zero)
                {
                    animator.SetFloat(MoveX, lastDirection.x);
                    animator.SetFloat(MoveY, lastDirection.y);
                }
                animator.Play(IdleState, 0, 0f);
            }
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

        private static Vector2 Perpendicular(Vector2 direction) => new Vector2(-direction.y, direction.x);

        // Annonce : chevrons au sol (un par case, tournés vers la cible) + « ! » au-dessus de la tête
        private void ShowWarning(List<Vector3> lane, Vector2 direction, int groundLevel)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            if (chevronSprite != null)
            {
                // Un chevron par case de la bande (trajectoire + cases voisines touchées aussi)
                Vector3 side = Perpendicular(direction);
                int width = hitHalfWidth * 2 + 1;
                int needed = lane.Count * width;
                laneLength = lane.Count;
                while (chevrons.Count < needed)
                {
                    var go = new GameObject("ChargeChevron");
                    chevrons.Add(go.AddComponent<SpriteRenderer>());
                }
                chevronRows.Clear();
                for (int i = 0; i < chevrons.Count; i++)
                {
                    SpriteRenderer chevron = chevrons[i];
                    bool used = i < needed;
                    chevron.gameObject.SetActive(used);
                    chevronRows.Add(used ? i / width : -1);
                    if (!used)
                        continue;
                    Vector3 position = lane[i / width] + side * (i % width - hitHalfWidth);
                    chevron.sprite = chevronSprite;
                    chevron.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
                    // Au sol : sous les persos (niveau 0 : background ; plus haut : juste sous le rang des persos)
                    if (groundLevel <= 0)
                    {
                        chevron.sortingLayerName = "background";
                        chevron.sortingOrder = groundSortingOrder;
                    }
                    else
                    {
                        chevron.sortingLayerName = "player";
                        chevron.sortingOrder = groundLevel * LevelMap.SortingOrdersPerLevel - 1;
                    }
                    chevron.enabled = false;
                }
            }
            if (alertSprite != null)
            {
                if (alert == null)
                {
                    var go = new GameObject("ChargeAlert");
                    go.transform.SetParent(transform, false);
                    alert = go.AddComponent<SpriteRenderer>();
                    alert.sprite = alertSprite;
                }
                alert.gameObject.SetActive(true);
                alert.sortingLayerID = spriteRenderer.sortingLayerID;
                alert.sortingOrder = spriteRenderer.sortingOrder + 1;
            }
            UpdateWarning(0f);
        }

        private void UpdateWarning(float time)
        {
            // Vague de chevrons allumés qui court vers la cible, en boucle (toute la largeur de la bande ensemble)
            int period = laneLength + chevronLitCount;
            int head = Mathf.FloorToInt(time * chevronCellsPerSecond) % Mathf.Max(1, period);
            for (int i = 0; i < chevrons.Count; i++)
            {
                if (!chevrons[i].gameObject.activeSelf)
                    continue;
                int row = chevronRows[i];
                chevrons[i].enabled = row <= head && row > head - chevronLitCount;
            }
            if (alert != null && alert.gameObject.activeSelf)
            {
                // Petit rebond à l'apparition (au pixel), puis immobile
                int bounce = time < 0.08f ? alertBouncePixels : 0;
                alert.transform.localPosition = new Vector3(alertOffsetPixels.x, alertOffsetPixels.y + bounce, 0f) / PixelsPerUnit;
            }
        }

        private void HideWarning()
        {
            foreach (var chevron in chevrons)
                if (chevron != null)
                    chevron.gameObject.SetActive(false);
            if (alert != null)
                alert.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            foreach (var chevron in chevrons)
                if (chevron != null)
                    Destroy(chevron.gameObject);
        }

        // Morceau de trace sur cette case : dessiné vers la droite ; à gauche retourné (bord éclairé gardé en bas),
        // en haut / en bas tourné ; au sol, sous les persos
        private void SpawnTrail(Vector3 cell, Vector2 direction, int groundLevel)
        {
            if (trailPrefabs == null || trailPrefabs.Length == 0)
                return;
            GameObject prefab = trailPrefabs[Random.Range(0, trailPrefabs.Length)];
            if (prefab == null)
                return;
            bool horizontal = Mathf.Abs(direction.x) > 0.5f;
            Vector3 position = cell + (horizontal ? Vector3.down * (trailFeetPixels / PixelsPerUnit) : Vector3.zero);
            Quaternion rotation = horizontal ? Quaternion.identity : Quaternion.Euler(0f, 0f, direction.y > 0f ? 90f : -90f);
            GameObject piece = Instantiate(prefab, position, rotation);
            var pieceRenderer = piece.GetComponent<SpriteRenderer>();
            if (pieceRenderer == null)
                return;
            pieceRenderer.flipX = horizontal && direction.x < 0f;
            if (groundLevel <= 0)
            {
                pieceRenderer.sortingLayerName = "background";
                pieceRenderer.sortingOrder = groundSortingOrder;
            }
            else
            {
                pieceRenderer.sortingLayerName = "player";
                pieceRenderer.sortingOrder = groundLevel * LevelMap.SortingOrdersPerLevel - 1;
            }
        }

        // Copie de Cell à cet endroit, qui s'efface (effet de vitesse)
        private void SpawnAfterimage(Vector3 position)
        {
            var go = new GameObject("ChargeAfterimage");
            go.transform.position = position;
            var copy = go.AddComponent<SpriteRenderer>();
            copy.sprite = spriteRenderer.sprite;
            copy.flipX = spriteRenderer.flipX;
            copy.sortingLayerID = spriteRenderer.sortingLayerID;
            copy.sortingOrder = spriteRenderer.sortingOrder - 1;
            copy.color = new Color(1f, 1f, 1f, afterimageAlpha);
            StartCoroutine(FadeAfterimage(copy));
        }

        private IEnumerator FadeAfterimage(SpriteRenderer copy)
        {
            float t = 0f;
            while (t < afterimageFade && copy != null)
            {
                copy.color = new Color(1f, 1f, 1f, afterimageAlpha * (1f - t / afterimageFade));
                t += Time.deltaTime;
                yield return null;
            }
            if (copy != null)
                Destroy(copy.gameObject);
        }
    }
}
