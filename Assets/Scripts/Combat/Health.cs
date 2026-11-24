using System;
using UnityEngine;

namespace DBZ.Combat
{
    // Points de vie de n'importe quel personnage ou objet qui peut prendre des dégâts
    public class Health : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;

        [Tooltip("Part des PV au lancement (1 = pleins ; 0,5 = moitié, pour tester les soins)")]
        [Range(0.01f, 1f)]
        [SerializeField] private float startHealthRatio = 1f;

        [Tooltip("Allié du joueur : ses coups ne lui font rien, ses soins (Masenko…) le soignent")]
        [SerializeField] private bool isAlly;

        public float Max => maxHealth;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;
        public bool IsAlly => isAlly;

        // Invulnérable (stase des boss) : les coups ne font rien, ni clignotement ; « 0 » gris (BlockedHit)
        public bool IsInvulnerable { get; set; }
        // Plancher de PV : un coup ne descend pas en dessous (seuils de stase : un gros coup ne saute pas une phase). 0 = aucun
        public float MinHealth { get; set; }

        // Paramètres : dégâts reçus, coup critique ou non (pour l'affichage des dégâts)
        public event Action<float, bool> Damaged;
        // Paramètre : PV réellement rendus (pour l'affichage du soin)
        public event Action<float> Healed;
        public event Action Died;
        // Coup reçu pendant l'invulnérabilité (stase des boss) : sans dégâts, affiché « 0 » en gris
        public event Action BlockedHit;

        private void Awake()
        {
            Current = maxHealth * startHealthRatio;
        }

        public void TakeDamage(float amount, bool isCritical = false)
        {
            if (IsDead || amount <= 0f)
                return;
            if (IsInvulnerable)
            {
                BlockedHit?.Invoke();
                return;
            }

            float before = Current;
            Current = Mathf.Max(Current > MinHealth ? MinHealth : 0f, Current - amount);
            // Dégâts réellement reçus (un coup arrêté au seuil de stase n'affiche que ce qu'il a retiré)
            Damaged?.Invoke(before - Current, isCritical);

            if (IsDead)
                Died?.Invoke();
        }

        // Coût payé en PV (techniques de Piccolo) : refusé s'il ferait tomber à 0, ce n'est pas un coup reçu
        public bool CanSpend(float amount)
        {
            return Current > amount;
        }

        // Sans clignotement ni chiffre de dégâts (événement Damaged non déclenché)
        public bool TrySpend(float amount)
        {
            if (!CanSpend(amount))
                return false;
            Current -= amount;
            return true;
        }

        // Renvoie les PV réellement rendus (0 si déjà pleins) ; le chiffre vert n'apparaît que dans ce cas
        public float Heal(float amount)
        {
            if (IsDead || amount <= 0f)
                return 0f;
            float healed = Mathf.Min(maxHealth, Current + amount) - Current;
            if (healed <= 0f)
                return 0f;
            Current += healed;
            Healed?.Invoke(healed);
            return healed;
        }

        // Remet une valeur gardée (passage d'une scène à l'autre), sans déclencher d'événement de dégâts
        public void SetCurrent(float value)
        {
            Current = Mathf.Clamp(value, 0f, maxHealth);
        }

        public void ResetHealth()
        {
            Current = maxHealth;
        }
    }
}
