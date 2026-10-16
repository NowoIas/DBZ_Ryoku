using UnityEngine;

namespace DBZ.Combat
{
    // Dernier moment où le joueur a infligé ou reçu des dégâts : le vol est interdit un moment après un combat.
    // Statique : gardé d'une scène à l'autre (Time.time continue)
    public static class PlayerCombat
    {
        private static float lastFightTime = float.NegativeInfinity;

        public static float SecondsSinceFight => Time.time - lastFightTime;

        public static void RegisterFight()
        {
            lastFightTime = Time.time;
        }

        // Coup du joueur : compte comme un combat seulement si la cible était encore en vie.
        // Les alliés ne sont jamais blessés par le joueur (ses tirs s'arrêtent quand même sur eux)
        public static void Hit(Health target, float amount, bool isCritical = false)
        {
            if (target.IsDead || target.IsAlly || amount <= 0f)
                return;
            target.TakeDamage(amount, isCritical);
            RegisterFight();
        }

        // Domain Reload désactivé : sans cette remise à zéro, l'heure d'un ancien lancement du mode Play resterait
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            lastFightTime = float.NegativeInfinity;
        }
    }
}
