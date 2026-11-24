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
        [Tooltip("Traînée orange sous la barre de PV : garde un instant les PV perdus, puis se vide jusqu'aux PV actuels (vide = aucune)")]
        [SerializeField] private Image healthTrailFill;
        [Tooltip("Temps pendant lequel la traînée reste immobile après un coup, en secondes (relancé à chaque coup)")]
        [SerializeField] private float trailDelay = 0.4f;
        [Tooltip("Vitesse à laquelle la traînée se vide, en part de la barre par seconde (1 = toute la barre en 1 s)")]
        [SerializeField] private float trailDrainSpeed = 1f;
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
        private float trailRatio = -1f;
        private float trailHoldUntil;
        private float lastHealthRatio = 1f;

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
            float healthRatio = playerHealth.Current / playerHealth.Max;
            healthFill.fillAmount = healthRatio;
            UpdateHealthTrail(healthRatio);

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

        // La traînée suit la barre vers le haut (soin) aussitôt ; vers le bas, elle attend trailDelay puis se vide
        private void UpdateHealthTrail(float healthRatio)
        {
            if (healthTrailFill == null)
                return;
            if (trailRatio < 0f || healthRatio >= trailRatio)
                trailRatio = healthRatio;
            else
            {
                // Nouveau coup pendant l'attente ou la descente : l'attente repart
                if (healthRatio < lastHealthRatio)
                    trailHoldUntil = Time.time + trailDelay;
                if (Time.time >= trailHoldUntil)
                    trailRatio = Mathf.MoveTowards(trailRatio, healthRatio, trailDrainSpeed * Time.deltaTime);
            }
            lastHealthRatio = healthRatio;
            healthTrailFill.fillAmount = trailRatio;
        }
    }
}
