using System.Collections;
using UnityEngine;

namespace DBZ.Combat
{
    // Absorbe les Ki Blast (C19, Dr Gero) : un projectile « absorbable » (Projectile.absorbable, Ki Blast normal des persos)
    // qui le touche ne fait aucun dégât et lui rend des PV (chiffre vert), avec un bref éclat de couleur.
    // Les autres techniques blessent normalement
    [RequireComponent(typeof(Health))]
    public class KiAbsorber : MonoBehaviour
    {
        [Tooltip("PV rendus = dégâts du Ki Blast × cette valeur (1 = autant que les dégâts)")]
        [SerializeField] private float healRatio = 1f;
        [Tooltip("Couleur de l'éclat à l'absorption")]
        [SerializeField] private Color flashColor = new Color(0.6f, 0.9f, 1f, 1f);
        [Tooltip("Durée de l'éclat, en secondes")]
        [SerializeField] private float flashDuration = 0.15f;

        private Health health;
        private SpriteRenderer spriteRenderer;
        private Coroutine flash;

        private void Awake()
        {
            health = GetComponent<Health>();
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        public void Absorb(float damage)
        {
            if (health.IsDead)
                return;
            health.Heal(damage * healRatio);
            if (spriteRenderer == null || flashDuration <= 0f || !isActiveAndEnabled)
                return;
            if (flash != null)
                StopCoroutine(flash);
            flash = StartCoroutine(Flash());
        }

        private IEnumerator Flash()
        {
            spriteRenderer.color = flashColor;
            yield return new WaitForSeconds(flashDuration);
            spriteRenderer.color = Color.white;
            flash = null;
        }
    }
}
