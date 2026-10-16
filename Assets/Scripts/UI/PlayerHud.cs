using DBZ.Combat;
using DBZ.Player;
using UnityEngine;
using UnityEngine.UI;

namespace DBZ.UI
{
    // Met à jour les barres de PV et de Ki du joueur
    public class PlayerHud : MonoBehaviour
    {
        // Vides = ceux de l'objet tagué "Player" (le prefab HUD marche ainsi dans toutes les scènes sans réglage)
        [SerializeField] private Health playerHealth;
        [SerializeField] private PlayerKi playerKi;

        [SerializeField] private Image healthFill;
        // Jauge bleue (forme normale)
        [SerializeField] private Image kiFill;
        // Une jauge par palier de transformation, dans l'ordre (jaune = SSJ, orange = SSJ3...),
        // affichée par-dessus la bleue ; seule celle du palier actuel est visible
        [SerializeField] private Image[] transformedKiFills;
        // Durée du remplissage d'une nouvelle jauge au début d'une transformation (en secondes)
        [SerializeField] private float transformedFillDuration = 1.5f;

        // Personnage qui paie ses techniques en PV (Piccolo) : jauge de Ki vide
        private PlayerController playerController;

        private PlayerKi.Form previousForm = PlayerKi.Form.Normal;
        private float transformStartTime;

        private void Awake()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                if (playerHealth == null)
                    playerHealth = player.GetComponent<Health>();
                if (playerKi == null)
                    playerKi = player.GetComponent<PlayerKi>();
                playerController = player.GetComponent<PlayerController>();
            }
        }

        private void Update()
        {
            healthFill.fillAmount = playerHealth.Current / playerHealth.Max;

            // Chaque jauge affiche le Ki de sa forme : la bleue reste figée pendant une transformation
            bool noKi = playerController != null && playerController.PaysWithHealth;
            kiFill.fillAmount = noKi ? 0f : playerKi.GetStageKi(0) / playerKi.Max;

            var form = playerKi.CurrentForm;
            if (form == PlayerKi.Form.Transforming && previousForm != PlayerKi.Form.Transforming)
                transformStartTime = Time.time;

            // Seule la jauge du palier actuel s'affiche, par-dessus la bleue : le retour se fait toujours
            // en forme normale, les jauges des paliers intermédiaires (jaune en SSJ3) ne servent plus.
            // Elle monte de 0 jusqu'à son Ki de départ, puis suit son Ki qui baisse
            bool isTransformed = form == PlayerKi.Form.Transforming || form == PlayerKi.Form.Transformed;
            float progress = Mathf.Clamp01((Time.time - transformStartTime) / transformedFillDuration);
            for (int i = 0; i < transformedKiFills.Length; i++)
            {
                int stage = i + 1;
                bool show = isTransformed && playerKi.Level == stage && !noKi;
                transformedKiFills[i].enabled = show;
                if (show)
                    transformedKiFills[i].fillAmount = playerKi.GetStageKi(stage) / playerKi.Max * progress;
            }

            previousForm = form;
        }
    }
}
