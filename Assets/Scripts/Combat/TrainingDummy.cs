using System.Collections;
using UnityEngine;

namespace DBZ.Combat
{
    // Cible d'entraînement : clignote quand elle est touchée et affiche les dégâts dans la console.
    // La disparition et la réapparition après un K.O. sont gérées par DisappearOnDeath
    [RequireComponent(typeof(Health), typeof(SpriteRenderer))]
    public class TrainingDummy : MonoBehaviour
    {
        [SerializeField] private Color hitColor = Color.red;
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
            health.Died += OnDied;
        }

        private void OnDisable()
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }

        private void OnDamaged(float amount, bool isCritical)
        {
            string critical = isCritical ? " (critique)" : "";
            Debug.Log($"Punching-ball : -{amount} PV{critical} ({health.Current}/{health.Max})");
            StopCoroutine(nameof(Flash));
            StartCoroutine(nameof(Flash));
        }

        private void OnDied()
        {
            Debug.Log("Punching-ball K.O. !");
        }

        private IEnumerator Flash()
        {
            spriteRenderer.color = hitColor;
            yield return new WaitForSeconds(flashDuration);
            spriteRenderer.color = baseColor;
        }
    }
}
