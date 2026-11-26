using System.Collections;
using UnityEngine;

namespace DBZ.Combat
{
    // Pétrification (2026-11-26, crachat de Dabura) : posé sur le prefab Player.
    // Touché : la pierre monte des pieds à la tête (spreadDuration), Goku est figé (Animator arrêté, PlayerController
    // ne décide plus rien et coupe rayon / Genkidama tenus) pendant la durée donnée, icône au-dessus de lui ;
    // il prend les dégâts normalement (choix util.). Fin : la pierre éclate (StoneShatter), puis il ne peut plus être
    // pétrifié pendant immunityDuration. Mort pendant la pétrification : tout est rendu aussitôt
    [RequireComponent(typeof(Health), typeof(SpriteRenderer))]
    public class Petrification : MonoBehaviour
    {
        public const string StatusName = "stone";

        [Tooltip("Matériau « pierre » (Shaders/Stone.mat)")]
        [SerializeField] private Material stoneMaterial;
        [Tooltip("Temps de montée de la pierre, des pieds à la tête (secondes, compté dans la durée)")]
        [SerializeField] private float spreadDuration = 0.15f;
        [Tooltip("Après la libération : temps sans nouvelle pétrification possible (secondes)")]
        [SerializeField] private float immunityDuration = 1f;
        [Tooltip("Icône au-dessus du personnage (stun_icon)")]
        [SerializeField] private Sprite icon;
        [Tooltip("Pierre qui éclate à la libération")]
        [SerializeField] private StoneShatter.Settings shatter = new StoneShatter.Settings();
        [Tooltip("Zone du corps où naissent les éclats, en pixels depuis le centre de la case (x, y, largeur, hauteur)")]
        [SerializeField] private RectInt shatterBodyPixels = new RectInt(-7, -10, 14, 22);
        [Tooltip("Sol où retombent les cailloux, en pixels sous le centre de la case")]
        [SerializeField] private int shatterFeetPixels = 13;

        private Health health;
        private SpriteRenderer spriteRenderer;
        private Animator animator;
        private StoneSkin skin;
        private float savedAnimatorSpeed = 1f;
        private float immuneUntil = float.MinValue;
        private Coroutine routine;

        public bool IsPetrified => routine != null;

        private void Awake()
        {
            health = GetComponent<Health>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            animator = GetComponent<Animator>();
        }

        private void OnEnable() => health.Died += OnDied;

        private void OnDisable()
        {
            health.Died -= OnDied;
            Release(false);
        }

        // Refusé : mort, déjà en pierre, encore protégé
        public bool TryPetrify(float duration)
        {
            if (duration <= 0f || health.IsDead || IsPetrified || Time.time < immuneUntil || stoneMaterial == null)
                return false;
            routine = StartCoroutine(Petrify(duration));
            return true;
        }

        private IEnumerator Petrify(float duration)
        {
            if (animator != null)
            {
                savedAnimatorSpeed = animator.speed;
                animator.speed = 0f;
            }
            skin = new StoneSkin(spriteRenderer, stoneMaterial);
            StatusEffects.Of(health).Add(StatusName, duration, icon);

            for (float t = 0f; t < spreadDuration; t += Time.deltaTime)
            {
                skin.SetProgress(t / spreadDuration);
                yield return null;
            }
            skin.SetProgress(1f);
            yield return new WaitForSeconds(Mathf.Max(0f, duration - spreadDuration));

            Release(true);
        }

        private void Release(bool shatterStone)
        {
            if (routine == null && skin == null)
                return;
            if (routine != null)
                StopCoroutine(routine);
            routine = null;
            if (skin != null)
            {
                skin.End();
                skin = null;
            }
            if (animator != null)
                animator.speed = savedAnimatorSpeed;
            if (shatterStone)
            {
                immuneUntil = Time.time + immunityDuration;
                Vector2 center = transform.position;
                var body = new Rect(center + (Vector2)shatterBodyPixels.position / 32f, (Vector2)shatterBodyPixels.size / 32f);
                StoneShatter.Spawn(shatter, body, center.y - shatterFeetPixels / 32f, spriteRenderer.sortingLayerName, spriteRenderer.sortingOrder);
            }
        }

        private void OnDied() => Release(false);
    }
}
