using System.Collections;
using UnityEngine;

namespace DBZ.Combat
{
    // K.O. : l'objet disparaît de la carte.
    // Sans réapparition (ennemis) il est détruit ; avec réapparition (punching-ball) il est seulement caché,
    // puis revient au même endroit avec tous ses PV.
    [RequireComponent(typeof(Health))]
    public class DisappearOnDeath : MonoBehaviour
    {
        [SerializeField] private bool respawn = false;

        [Tooltip("Délai avant réapparition, en secondes (si Respawn est coché)")]
        [SerializeField] private float respawnDelay = 1f;

        private Health health;
        private SpriteRenderer[] spriteRenderers;
        private Collider2D[] colliders;

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
            else
                Destroy(gameObject);
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
