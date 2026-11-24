using System.Collections.Generic;
using DBZ.Combat;
using UnityEngine;

namespace DBZ.Enemies
{
    // Marque un ennemi comme boss (Raditz, Freezer, Cell) : un ennemi sans ce composant est un ennemi classique.
    // Quand le boss repère Goku, il est affiché par les barres du HUD (badge + PV, en haut à droite).
    // Plusieurs boss (C19 + Dr Gero) : une barre chacun, l'une sous l'autre, dans l'ordre de repérage
    [RequireComponent(typeof(EnemyController), typeof(Health))]
    public class Boss : MonoBehaviour
    {
        [Tooltip("Nom du boss (pour plus tard : affichage, dialogues)")]
        [SerializeField] private string displayName = "Boss";
        [Tooltip("Vignette du boss, affichée à gauche de sa barre de vie")]
        [SerializeField] private Sprite badge;

        // Boss affichés par les barres du HUD, dans l'ordre de repérage ; chacun le reste jusqu'à sa destruction ou la fin de la poursuite
        private static readonly List<Boss> shown = new List<Boss>();
        public static IReadOnlyList<Boss> Shown => shown;

        public string DisplayName => displayName;
        public Sprite Badge => badge;

        // Changement de forme (BossForms) : la barre du HUD relit le badge
        public void SetBadge(Sprite newBadge) => badge = newBadge;
        public Health Health { get; private set; }
        public EnemyController Controller { get; private set; }

        // Statiques gardés entre deux Play (Disable Domain Reload)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => shown.Clear();

        private void Awake()
        {
            Health = GetComponent<Health>();
            Controller = GetComponent<EnemyController>();
        }

        private void OnEnable() => Controller.ChaseStarted += OnChaseStarted;

        private void OnDisable()
        {
            Controller.ChaseStarted -= OnChaseStarted;
            shown.Remove(this);
        }

        private void OnChaseStarted()
        {
            if (!Health.IsDead && !shown.Contains(this))
                shown.Add(this);
        }

        private void Update()
        {
            // Poursuite abandonnée (Goku trop loin, en vol, K.O.) : barre cachée. Le K.O. est géré par la barre (elle se vide d'abord)
            if (!Health.IsDead && !Controller.IsChasing)
                shown.Remove(this);
        }
    }
}
