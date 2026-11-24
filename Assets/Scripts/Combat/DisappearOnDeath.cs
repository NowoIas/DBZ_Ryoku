using System.Collections;
using UnityEngine;

namespace DBZ.Combat
{
    // K.O. : l'objet disparaît de la carte.
    // Sans réapparition (ennemis) il est détruit ; avec réapparition (punching-ball) il est seulement caché,
    // puis revient au même endroit avec tous ses PV.
    // Animation de mort (images remplies, ennemis sans réapparition) : jouée d'abord, dernière image gardée
    // lyingDuration, puis l'objet est détruit ; pendant ce temps il ne bouge plus, ne bloque plus et ne prend plus de coups.
    // Explosions (prefab rempli, sans réapparition) : explosionCount explosions, une toutes les explosionInterval,
    // la 1re au centre, les suivantes au hasard dans la zone ; sans animation de mort, l'objet est caché pendant la 1re
    [RequireComponent(typeof(Health))]
    public class DisappearOnDeath : MonoBehaviour
    {
        [SerializeField] private bool respawn = false;

        [Tooltip("Délai avant réapparition, en secondes (si Respawn est coché)")]
        [SerializeField] private float respawnDelay = 1f;

        [Header("Animation de mort (vide = disparition immédiate)")]
        [Tooltip("Images de la mort, dans l'ordre (Cell : d0_0…2)")]
        [SerializeField] private Sprite[] deathFrames = new Sprite[0];
        [Tooltip("Vitesse de l'animation, en images par seconde (personnages : 6)")]
        [SerializeField] private float deathFramesPerSecond = 6f;
        [Tooltip("Temps passé couché sur la dernière image avant de disparaître, en secondes")]
        [SerializeField] private float lyingDuration = 0.5f;

        [Header("Explosions à la mort (vide = aucune)")]
        [Tooltip("Explosion jouée à la mort (prefab avec OneShotEffect : BossExplosion, EnemyExplosion)")]
        [SerializeField] private OneShotEffect explosionPrefab;
        [Tooltip("Nombre d'explosions")]
        [SerializeField] private int explosionCount = 1;
        [Tooltip("Temps entre deux explosions, en secondes")]
        [SerializeField] private float explosionInterval = 0.1f;
        [Tooltip("Durée d'une explosion, en secondes (images ÷ vitesse du prefab)")]
        [SerializeField] private float explosionDuration = 0.25f;
        [Tooltip("Centre de la zone des explosions, en pixels depuis le centre de la case (Cell : (0,16), milieu de son sprite 64×64)")]
        [SerializeField] private Vector2Int explosionCenterPixels = Vector2Int.zero;
        [Tooltip("Écart maximal autour du centre, en pixels (0 = toutes au centre)")]
        [SerializeField] private Vector2Int explosionSpreadPixels = Vector2Int.zero;
        [Tooltip("Distance minimale entre deux explosions, en pixels (respectée si possible)")]
        [SerializeField] private float explosionMinGapPixels = 8f;
        [Tooltip("Sans animation de mort : l'objet est caché ce temps après la 1re explosion (2e image = grosse boule, qui le cache)")]
        [SerializeField] private float hideDelay = 0.0625f;

        private const float PixelsPerUnit = 32f;

        // Durées lues par la barre du boss (elle se vide pendant l'animation et disparaît avec lui)
        public bool HasDeathAnimation => !respawn && deathFrames != null && deathFrames.Length > 0 && deathFramesPerSecond > 0f;
        public float DeathAnimationDuration => HasDeathAnimation ? deathFrames.Length / deathFramesPerSecond : 0f;
        public float LyingDuration => HasDeathAnimation ? lyingDuration : 0f;
        public bool HasExplosions => !respawn && explosionPrefab != null && explosionCount > 0;
        // Fin de la dernière explosion, depuis le K.O.
        public float ExplosionsDuration => HasExplosions ? (explosionCount - 1) * explosionInterval + explosionDuration : 0f;
        // Mort mise en scène (animation ou explosions) : la barre du boss se vide pendant l'animation, ou pendant les explosions s'il n'y en a pas,
        // puis disparaît avec le boss (fin du temps couché ou de la dernière explosion)
        public bool HasDeathSequence => HasDeathAnimation || HasExplosions;
        public float DeathDrainDuration => HasDeathAnimation ? DeathAnimationDuration : ExplosionsDuration;
        public float DeathHideDelay => HasDeathAnimation ? Mathf.Max(lyingDuration, ExplosionsDuration - DeathAnimationDuration) : 0f;

        private Health health;
        private SpriteRenderer[] spriteRenderers;
        private Collider2D[] colliders;
        private bool explosionsPending;

        private void Awake()
        {
            health = GetComponent<Health>();
            spriteRenderers = GetComponentsInChildren<SpriteRenderer>();
            colliders = GetComponentsInChildren<Collider2D>();
        }

        private void OnEnable()
        {
            health.Died += OnDied;
        }

        private void OnDisable()
        {
            health.Died -= OnDied;
        }

        private void OnDied()
        {
            if (respawn)
                StartCoroutine(Respawn());
            else if (HasDeathSequence)
                StartCoroutine(DeathSequence());
            else
                Destroy(gameObject);
        }

        private IEnumerator DeathSequence()
        {
            // Plus de collision : ne bloque plus le passage, ne reçoit plus de coups
            foreach (var col in colliders)
                col.enabled = false;
            // Animator et comportements de l'ennemi (déplacement, attaques, stase) arrêtés : ils changeraient l'image ou frapperaient encore
            var animator = GetComponent<Animator>();
            if (animator != null)
                animator.enabled = false;
            foreach (var behaviour in GetComponents<MonoBehaviour>())
            {
                if (behaviour == this || behaviour.GetType().Namespace != "DBZ.Enemies" || behaviour is DBZ.Enemies.Boss)
                    continue;
                behaviour.StopAllCoroutines();
                behaviour.enabled = false;
            }

            if (HasExplosions)
            {
                explosionsPending = true;
                StartCoroutine(Explosions());
            }

            if (HasDeathAnimation)
            {
                var spriteRenderer = GetComponent<SpriteRenderer>();
                float frameTime = 1f / deathFramesPerSecond;
                foreach (var frame in deathFrames)
                {
                    if (spriteRenderer != null)
                        spriteRenderer.sprite = frame;
                    yield return new WaitForSeconds(frameTime);
                }
                yield return new WaitForSeconds(lyingDuration);
            }
            else
            {
                yield return new WaitForSeconds(hideDelay);
                foreach (var sr in spriteRenderers)
                    sr.enabled = false;
            }

            // Les explosions vivent seules, mais cet objet doit rester jusqu'à la dernière lancée
            while (explosionsPending)
                yield return null;
            Destroy(gameObject);
        }

        private IEnumerator Explosions()
        {
            var ownRenderer = GetComponent<SpriteRenderer>();
            // Au pixel près (l'ennemi peut mourir entre deux cases)
            Vector3 origin = transform.position;
            origin.x = Mathf.Round(origin.x * PixelsPerUnit) / PixelsPerUnit;
            origin.y = Mathf.Round(origin.y * PixelsPerUnit) / PixelsPerUnit;
            var placed = new Vector2Int[explosionCount];
            for (int i = 0; i < explosionCount; i++)
            {
                placed[i] = i == 0 ? Vector2Int.zero : PickOffset(placed, i);
                Vector2Int pixels = explosionCenterPixels + placed[i];
                var effect = Instantiate(explosionPrefab, origin + new Vector3(pixels.x, pixels.y, 0f) / PixelsPerUnit, Quaternion.identity);
                // Devant l'objet ; chaque nouvelle explosion devant la précédente
                var effectRenderer = effect.GetComponent<SpriteRenderer>();
                if (ownRenderer != null && effectRenderer != null)
                {
                    effectRenderer.sortingLayerID = ownRenderer.sortingLayerID;
                    effectRenderer.sortingOrder = ownRenderer.sortingOrder + 1 + i;
                }
                if (i < explosionCount - 1)
                    yield return new WaitForSeconds(explosionInterval);
            }
            explosionsPending = false;
        }

        // Décalage au hasard dans la zone, au pixel, loin des explosions déjà posées (au moins explosionMinGapPixels si possible)
        private Vector2Int PickOffset(Vector2Int[] placed, int count)
        {
            Vector2Int best = Vector2Int.zero;
            float bestGap = -1f;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var candidate = new Vector2Int(
                    Random.Range(-explosionSpreadPixels.x, explosionSpreadPixels.x + 1),
                    Random.Range(-explosionSpreadPixels.y, explosionSpreadPixels.y + 1));
                float gap = float.MaxValue;
                for (int j = 0; j < count; j++)
                    gap = Mathf.Min(gap, Vector2.Distance(candidate, placed[j]));
                if (gap >= explosionMinGapPixels)
                    return candidate;
                if (gap > bestGap)
                {
                    bestGap = gap;
                    best = candidate;
                }
            }
            return best;
        }

        private IEnumerator Respawn()
        {
            // On cache l'objet au lieu de le désactiver : un GameObject inactif ne peut pas faire tourner cette coroutine
            SetVisible(false);
            yield return new WaitForSeconds(respawnDelay);
            health.ResetHealth();
            SetVisible(true);
        }

        // Sans sprite ni collider, l'objet n'est plus visible, ne bloque plus le passage et ne reçoit plus de coups
        private void SetVisible(bool visible)
        {
            foreach (var sr in spriteRenderers)
                sr.enabled = visible;
            foreach (var col in colliders)
                col.enabled = visible;
        }
    }
}
