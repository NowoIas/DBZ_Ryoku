using DBZ.Combat;
using UnityEngine;

namespace DBZ.Enemies
{
    // Sortie de terre d'un sbire invoqué (Cell Jr) : la fissure s'ouvre (annonce), éclats et cailloux jaillissent,
    // puis le sbire monte du sol, le bas du corps caché sous la ligne du sol. Pendant ce temps il ne bouge pas,
    // n'attaque pas et ne prend pas de coups ; sa case est déjà occupée (Goku ne peut pas s'y mettre).
    // Lancée par Begin (BossStasis) ; sans appel, le sbire est actif tout de suite
    [RequireComponent(typeof(EnemyController), typeof(Health))]
    public class GroundEmergence : MonoBehaviour
    {
        // Début de la montée après l'éruption, et durée de la montée (lus aussi par GroundBurst pour le fondu)
        public const float RiseDelay = 0.1f;
        public const float RiseDuration = 0.4f;
        private const float PixelsPerUnit = 32f;

        [SerializeField] private GroundBurstSettings burst = new GroundBurstSettings();
        [Tooltip("Tremblement pendant la montée, en pixels (0 = aucun)")]
        [SerializeField] private int shakePixels = 1;

        private EnemyController controller;
        private Health health;
        private SpriteRenderer spriteRenderer;
        private Animator animator;
        private Sprite fullSprite;
        // Images coupées, par tremblement (-1, 0, +1 → 0, 1, 2) et lignes cachées, créées une fois
        private Sprite[,] partials;
        private float startTime = -1f;

        private void Awake()
        {
            controller = GetComponent<EnemyController>();
            health = GetComponent<Health>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            animator = GetComponent<Animator>();
        }

        // delay : attente avant l'annonce (sorties décalées entre sbires)
        public void Begin(float delay)
        {
            startTime = Time.time + delay;
            controller.IsPaused = true;
            health.IsInvulnerable = true;
            animator.enabled = false;
            fullSprite = spriteRenderer.sprite;
            spriteRenderer.enabled = false;
            partials = new Sprite[3, Mathf.RoundToInt(fullSprite.rect.height) + 1];
        }

        private bool burstStarted;

        private void LateUpdate()
        {
            if (startTime < 0f)
                return;
            float t = Time.time - startTime;
            if (t < 0f)
                return;

            if (!burstStarted)
            {
                burstStarted = true;
                var cellPos = new Vector2Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y));
                GroundBurst.Spawn(burst, cellPos, controller.Level);
            }

            float rise = (t - burst.openDuration - RiseDelay) / RiseDuration;
            if (rise < 0f)
                return;

            if (rise >= 1f)
            {
                Finish();
                return;
            }

            int height = Mathf.RoundToInt(fullSprite.rect.height);
            int hidden = Mathf.Clamp(Mathf.RoundToInt(height * (1f - rise)), 0, height - 1);
            int shake = shakePixels == 0 ? 0 : ((int)(t * 20f) % 2 == 0 ? 1 : -1) * Mathf.Min(1, shakePixels);
            spriteRenderer.enabled = true;
            spriteRenderer.sprite = Partial(hidden, shake);
        }

        // Haut du sprite seulement (les hidden lignes du bas sont sous le sol), posé plus bas d'autant :
        // le bas visible reste sur la ligne du sol (bas de l'image entière)
        private Sprite Partial(int hidden, int shake)
        {
            int s = Mathf.Clamp(shake, -1, 1) + 1;
            if (partials[s, hidden] != null)
                return partials[s, hidden];
            Rect r = fullSprite.rect;
            var rect = new Rect(r.x, r.y + hidden, r.width, r.height - hidden);
            // Pivot en pixels depuis le bas de la partie visible : même pivot que l'image entière, abaissé de hidden
            var pivotPixels = new Vector2(fullSprite.pivot.x - shake, fullSprite.pivot.y);
            var pivot = new Vector2(pivotPixels.x / rect.width, pivotPixels.y / rect.height);
            var sprite = Sprite.Create(fullSprite.texture, rect, pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
            partials[s, hidden] = sprite;
            return sprite;
        }

        private void Finish()
        {
            startTime = -1f;
            spriteRenderer.sprite = fullSprite;
            spriteRenderer.enabled = true;
            animator.enabled = true;
            health.IsInvulnerable = false;
            controller.IsPaused = false;
        }
    }
}
