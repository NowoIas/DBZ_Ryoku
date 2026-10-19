using DBZ.Combat;

namespace DBZ.Player
{
    // État du joueur gardé d'une scène à l'autre au passage d'une porte : chaque scène a son propre Player
    // (prefab), qui reprend ici ses PV, le Ki de chaque forme et sa forme (0 = normale, 1 = SSJ...)
    public static class PlayerState
    {
        private static bool hasSaved;
        private static float health;
        private static float[] stageKi;
        private static int formLevel;
        // Fantôme de Gotenks : il garde cette forme en passant une porte
        private static bool ghost;

        // Personnage choisi par la touche de test (F2) : gardé d'une scène à l'autre et à la mort.
        // Vide = celui réglé sur le prefab Player
        public static CharacterData Character;

        // Fusion en cours (Gotenks) : personnage d'origine (Goten), retrouvé à la séparation, et fin du chrono (Time.time,
        // qui continue d'une scène à l'autre). Vide = pas de fusion
        public static CharacterData FusionOrigin;
        public static float FusionEndTime;

        // Remise à zéro à chaque lancement du mode Play (« Disable Domain Reload » garde les valeurs statiques) :
        // un état gardé au dernier passage de porte ne doit pas resservir au lancement suivant
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            hasSaved = false;
            Character = null;
            FusionOrigin = null;
        }

        public static void Save(Health playerHealth, PlayerKi playerKi, bool isGhost = false)
        {
            health = playerHealth.Current;
            stageKi = playerKi.GetAllStageKi();
            formLevel = playerKi.Level;
            ghost = isGhost;
            hasSaved = true;
        }

        // Remet l'état gardé (une seule fois) ; formLevel dit au PlayerController quelles animations reprendre
        public static bool TryRestore(Health playerHealth, PlayerKi playerKi, out int restoredFormLevel, out bool restoredGhost)
        {
            restoredFormLevel = 0;
            restoredGhost = false;
            if (!hasSaved)
                return false;

            playerHealth.SetCurrent(health);
            playerKi.Restore(stageKi, formLevel);
            restoredFormLevel = formLevel;
            restoredGhost = ghost;
            hasSaved = false;
            return true;
        }
    }
}
