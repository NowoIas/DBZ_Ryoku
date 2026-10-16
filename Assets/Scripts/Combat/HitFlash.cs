using System.Collections;
using UnityEngine;

namespace DBZ.Combat
{
    // Clignote d'une couleur (rouge) à chaque coup reçu : joueur, ennemis.
    // Teinte le SpriteRenderer de l'objet ; l'Animator ne change que l'image, la couleur reste libre
    [RequireComponent(typeof(Health), typeof(SpriteRenderer))]
    public class HitFlash : MonoBehaviour
    {
        [SerializeField] private Color hitColor = Color.red;
        [Tooltip("Durée du clignotement, en secondes")]
        [SerializeField] private float flashDuration = 0.1f;

        private Health health;
        private SpriteRenderer spriteRenderer;
        private Color baseColor;

        private void Awake()
        {
            health = GetComponent<Health>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            baseColor = spriteRenderer.color;
        }

        private void OnEnable()
        {
            health.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            health.Damaged -= OnDamaged;
            // Coupé en plein clignotement : on ne reste pas rouge
            spriteRenderer.color = baseColor;
        }

        private void OnDamaged(float amount, bool isCritical)
        {
            StopCoroutine(nameof(Flash));
            StartCoroutine(nameof(Flash));
        }

        private IEnumerator Flash()
        {
            spriteRenderer.color = hitColor;
            yield return new WaitForSeconds(flashDuration);
            spriteRenderer.color = baseColor;
        }
    }
}
