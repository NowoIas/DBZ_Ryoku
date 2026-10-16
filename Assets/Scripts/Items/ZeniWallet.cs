using System;
using UnityEngine;

namespace DBZ.Items
{
    // Zénis ramassés par Goku. Statique : le total est gardé d'une scène à l'autre (portes) et à la mort.
    // Remis à zéro à chaque lancement du mode Play (« Disable Domain Reload » garde les valeurs statiques)
    public static class ZeniWallet
    {
        public static int Total { get; private set; }

        // Nouveau total, pour l'affichage (compteur du HUD)
        public static event Action<int> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Total = 0;
            Changed = null;
        }

        public static void Add(int amount)
        {
            if (amount <= 0)
                return;
            Total += amount;
            Changed?.Invoke(Total);
        }
    }
}
