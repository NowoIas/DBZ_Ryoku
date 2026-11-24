using System.Collections;
using DBZ.Combat;
using DBZ.World;
using UnityEngine;

namespace DBZ.Enemies
{
    // Tourelle (invoquée par Dr Gero) : fixe sur sa case, se tourne vers Goku et tire quand il est aligné à portée,
    // ligne dégagée, au même niveau. Au tir, le canon joue ses 2 images de tir avec la lueur de départ (muzzleFrames)
    // collée à la bouche, et le projectile part devant. Apparition : images jouées une fois, invulnérable et inactive pendant.
    // Sans Animator : les images sont posées par ce script (ballon de Gotenks : son image gagne, en LateUpdate)
    [RequireComponent(typeof(Health), typeof(SpriteRenderer))]
    public class Turret : MonoBehaviour
    {
        [Header("Images (0 = attente, puis les images de tir)")]
        [SerializeField] private Sprite[] upFrames;
        [SerializeField] private Sprite[] downFrames;
        [SerializeField] private Sprite[] leftFrames;
        [SerializeField] private Sprite[] rightFrames;
        [Tooltip("Vitesse des images de tir, en images par seconde")]
        [SerializeField] private float fireFramesPerSecond = 8f;

        [Header("Apparition")]
        [Tooltip("Images de l'apparition, dans l'ordre (vide = active tout de suite)")]
        [SerializeField] private Sprite[] appearFrames;
        [SerializeField] private float appearFramesPerSecond = 6f;

        [Header("Lueur de départ du tir (dessinée vers la droite, tournée)")]
        [Tooltip("Une image par image de tir du canon (p1_0, p1_1)")]
        [SerializeField] private Sprite[] muzzleFrames;
        [Tooltip("Centre de la lueur par image de tir, en pixels depuis le centre de la tourelle (x vers la droite, y vers le haut)")]
        [SerializeField] private Vector2[] muzzleUpPixels = { new Vector2(0f, 13f), new Vector2(0f, 12f) };
        [SerializeField] private Vector2[] muzzleDownPixels = { new Vector2(0f, -15f), new Vector2(0f, -13f) };
        [SerializeField] private Vector2[] muzzleLeftPixels = { new Vector2(-17f, 1f), new Vector2(-15f, 1f) };
        [SerializeField] private Vector2[] muzzleRightPixels = { new Vector2(17f, 1f), new Vector2(15f, 1f) };

        [Header("Tir")]
        [SerializeField] private Projectile projectilePrefab;
        [Tooltip("Le projectile part à cette distance devant la lueur, en pixels")]
        [SerializeField] private float projectileAheadPixels = 8f;
        [Tooltip("Distance (en cases) à laquelle la tourelle se tourne vers Goku")]
        [SerializeField] private int watchRange = 10;
        [Tooltip("Portée de tir, en cases (Goku aligné)")]
        [SerializeField] private int fireRange = 6;
        [Tooltip("Pause entre deux tirs, en secondes")]
        [SerializeField] private float fireCooldown = 2f;
        [Tooltip("Délai avant le premier tir après l'apparition, en secondes")]
        [SerializeField] private float firstFireDelay = 1f;
        [Tooltip("Layer(s) des objets qui bloquent la ligne de tir (cibles, ennemis, Destructibles)")]
        [SerializeField] private LayerMask solidObjectsLayer;

        private const float PixelsPerUnit = 32f;

        private Health health;
        private SpriteRenderer spriteRenderer;
        private HeightLevel heightLevel;
        private SpriteRenderer muzzle;
        private PlayerController player;
        private Health playerHealth;
        private int level;
        private Vector2 facing = Vector2.down;
        // Image affichée dans la liste de la direction (0 = attente)
        private int frame;
        private bool isFiring;
        private float appearStart = -1f;
        private float nextFireTime;

        private void Awake()
        {
            health = GetComponent<Health>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            heightLevel = GetComponent<HeightLevel>();
            // Recentrée sur sa case, comme les autres
            var pos = transform.position;
            transform.position = new Vector3(Mathf.Floor(pos.x) + 0.5f, Mathf.Floor(pos.y) + 0.5f, pos.z);

            var go = new GameObject("Muzzle");
            go.transform.SetParent(transform, false);
            muzzle = go.AddComponent<SpriteRenderer>();
            muzzle.enabled = false;
        }

        private void Start()
        {
            var playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null)
            {
                player = playerObject.GetComponent<PlayerController>();
                playerHealth = playerObject.GetComponent<Health>();
            }
            level = LevelMap.LevelAfterEntering(transform.position, 0);
            if (heightLevel != null)
                heightLevel.Level = level;
            spriteRenderer.sortingOrder += level * LevelMap.SortingOrdersPerLevel;

            if (appearFrames != null && appearFrames.Length > 0 && appearFramesPerSecond > 0f)
            {
                appearStart = Time.time;
                health.IsInvulnerable = true;
            }
            nextFireTime = Time.time + AppearDuration + firstFireDelay;
        }

        private float AppearDuration => appearFrames != null && appearFramesPerSecond > 0f ? appearFrames.Length / appearFramesPerSecond : 0f;

        private void Update()
        {
            if (appearStart >= 0f)
            {
                if (Time.time - appearStart < AppearDuration)
                    return;
                appearStart = -1f;
                health.IsInvulnerable = false;
            }
            if (health.IsDead || isFiring || player == null || StatusEffects.IsLockedOn(gameObject))
                return;
            if ((playerHealth != null && playerHealth.IsDead) || player.IsFlying)
                return;

            Vector2Int myCell = CellOf(transform.position);
            Vector2Int delta = CellOf(player.OccupiedCell) - myCell;
            int distance = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
            if (distance == 0 || distance > watchRange)
                return;

            // Tournée vers Goku (direction principale)
            facing = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? new Vector2(System.Math.Sign(delta.x), 0f) : new Vector2(0f, System.Math.Sign(delta.y));

            if ((delta.x == 0 || delta.y == 0) && distance <= fireRange && player.Level == level
                && Time.time >= nextFireTime && HasClearShot(delta))
                StartCoroutine(Fire());
        }

        private IEnumerator Fire()
        {
            isFiring = true;
            Sprite[] frames = FramesFor(facing);
            int fireFrames = Mathf.Max(1, frames.Length - 1);
            float frameTime = 1f / Mathf.Max(0.1f, fireFramesPerSecond);
            for (int i = 0; i < fireFrames; i++)
            {
                frame = Mathf.Min(i + 1, frames.Length - 1);
                ShowMuzzle(i);
                if (i == 0)
                    Launch();
                yield return new WaitForSeconds(frameTime);
            }
            frame = 0;
            muzzle.enabled = false;
            nextFireTime = Time.time + fireCooldown;
            isFiring = false;
        }

        // Lueur collée à la bouche du canon, tournée dans le sens du tir
        private void ShowMuzzle(int index)
        {
            if (muzzleFrames == null || muzzleFrames.Length == 0)
                return;
            muzzle.sprite = muzzleFrames[Mathf.Min(index, muzzleFrames.Length - 1)];
            Vector2[] offsets = OffsetsFor(facing);
            Vector2 pixels = offsets != null && offsets.Length > 0 ? offsets[Mathf.Min(index, offsets.Length - 1)] : Vector2.zero;
            muzzle.transform.localPosition = pixels / PixelsPerUnit;
            muzzle.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg);
            muzzle.sortingLayerID = spriteRenderer.sortingLayerID;
            muzzle.sortingOrder = spriteRenderer.sortingOrder + 1;
            muzzle.enabled = true;
        }

        private void Launch()
        {
            if (projectilePrefab == null)
                return;
            Vector2[] offsets = OffsetsFor(facing);
            Vector2 pixels = (offsets != null && offsets.Length > 0 ? offsets[0] : Vector2.zero) + facing * projectileAheadPixels;
            var projectile = Instantiate(projectilePrefab, transform.position + (Vector3)(pixels / PixelsPerUnit), Quaternion.identity);
            projectile.Owner = transform;
            projectile.Launch(facing, level);
        }

        // Après tout le reste (ballon de Gotenks : son image passe avant, posée en LateUpdate par StatusEffects)
        private void LateUpdate()
        {
            if (health.IsDead)
                return;
            if (appearStart >= 0f)
            {
                int i = Mathf.Min((int)((Time.time - appearStart) * appearFramesPerSecond), appearFrames.Length - 1);
                spriteRenderer.sprite = appearFrames[i];
                return;
            }
            if (StatusEffects.IsLockedOn(gameObject))
                return;
            Sprite[] frames = FramesFor(facing);
            if (frames != null && frames.Length > 0)
                spriteRenderer.sprite = frames[Mathf.Min(frame, frames.Length - 1)];
        }

        private Sprite[] FramesFor(Vector2 dir) => dir.y > 0 ? upFrames : dir.y < 0 ? downFrames : dir.x < 0 ? leftFrames : rightFrames;

        private Vector2[] OffsetsFor(Vector2 dir) => dir.y > 0 ? muzzleUpPixels : dir.y < 0 ? muzzleDownPixels : dir.x < 0 ? muzzleLeftPixels : muzzleRightPixels;

        // Rien entre la tourelle et Goku (aligné) : ni côté bloqué sur les calques de collision, ni objet qui bloque
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
                if (i < distance - 1 && HeightLevel.FirstOnLevel(Physics2D.OverlapCircleAll(cell, 0.3f, solidObjectsLayer), cellLevel) != null)
                    return false;
            }
            return true;
        }

        private static Vector2Int CellOf(Vector3 world) => new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));
    }
}
