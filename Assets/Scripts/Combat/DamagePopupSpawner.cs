using UnityEngine;

namespace DBZ.Combat
{
    // À poser sur tout ce qui a des PV (cibles, ennemis, joueur) :
    // affiche un DamagePopup au-dessus de l'objet à chaque coup reçu (chiffres rouges) et à chaque soin (chiffres verts)
    [RequireComponent(typeof(Health))]
    public class DamagePopupSpawner : MonoBehaviour
    {
        [SerializeField] private DamagePopup popupPrefab;

        [Tooltip("Afficher les dégâts reçus (décoché sur le joueur : seuls ses soins s'affichent)")]
        [SerializeField] private bool showDamage = true;
        [Tooltip("Afficher les PV rendus par un soin")]
        [SerializeField] private bool showHeal = true;

        [Tooltip("Position d'apparition par rapport à l'objet, en cases")]
        [SerializeField] private Vector2 offset = new Vector2(0f, 0.5f);

        [Tooltip("Décalage horizontal aléatoire maximum, en pixels, pour que les coups rapprochés ne se superposent pas")]
        [SerializeField] private int horizontalJitter = 4;

        private Health health;

        private void Awake()
        {
            health = GetComponent<Health>();
        }

        private void OnEnable()
        {
            health.Damaged += OnDamaged;
            health.Healed += OnHealed;
        }

        private void OnDisable()
        {
            health.Damaged -= OnDamaged;
            health.Healed -= OnHealed;
        }

        private void OnDamaged(float amount, bool isCritical)
        {
            if (showDamage)
                Spawn(amount, isCritical, false);
        }

        private void OnHealed(float amount)
        {
            if (showHeal)
                Spawn(amount, false, true);
        }

        private void Spawn(float amount, bool isCritical, bool isHeal)
        {
            if (popupPrefab == null)
                return;

            float jitter = Random.Range(-horizontalJitter, horizontalJitter + 1) / 32f;
            Vector3 position = transform.position + (Vector3)offset + Vector3.right * jitter;

            // Pas de parent : le nombre reste en place même si la cible bouge ou disparaît
            DamagePopup popup = Instantiate(popupPrefab, position, Quaternion.identity);
            popup.Show(Mathf.RoundToInt(amount), isCritical, isHeal);
        }
    }
}
