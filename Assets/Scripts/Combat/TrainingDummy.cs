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

            // Comme le joueur et les ennemis : recentré sur sa case, sinon posé un peu à côté dans l'éditeur,
            // il déborde sur la case voisine et les impacts (Ki Blast) y tombent
            Vector3 pos = transform.position;
            transform.position = new Vector3(Mathf.Floor(pos.x) + 0.5f, Mathf.Floor(pos.y) + 0.5f, pos.z);
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
