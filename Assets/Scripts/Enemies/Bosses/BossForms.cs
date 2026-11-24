using System;
using System.Collections;
using DBZ.Combat;
using UnityEngine;

namespace DBZ.Enemies
{
    // Formes d'un boss (Freezer) : à chaque seuil de PV (75 %, 50 %, 25 %), il s'arrête, devient invulnérable
    // (barre et badge grisés), joue sa transformation puis prend l'animation et le badge de la forme suivante.
    // Les PV continuent de baisser normalement ; un gros coup s'arrête pile au seuil (une forme n'est jamais sautée).
    // Forme suivante sans controller (planches pas encore faites) : seuil ignoré, le boss garde sa forme
    [RequireComponent(typeof(Health), typeof(EnemyController), typeof(Boss))]
    public class BossForms : MonoBehaviour
    {
        [Serializable]
        private class Form
        {
            [Tooltip("Nom de la forme (repère dans l'Inspector)")]
            public string name;
            [Tooltip("Animations de la forme (1re forme : vide = celles du prefab ; suivantes : vide = forme pas prête)")]
            public RuntimeAnimatorController controller;
            [Tooltip("Badge affiché à gauche de la barre de vie (vide = aucun)")]
            public Sprite badge;
            [Tooltip("Transformation vers la forme suivante (planche tr1, de face, dans l'ordre ; vide = changement immédiat)")]
            public Sprite[] transformFrames;
        }

        [Tooltip("Formes dans l'ordre (Forme1, Forme2, Forme3, Finale)")]
        [SerializeField] private Form[] forms = new Form[0];
        [Tooltip("Parts des PV max qui déclenchent la transformation vers la forme suivante, de la plus haute à la plus basse")]
        [SerializeField] private float[] healthThresholds = { 0.75f, 0.5f, 0.25f };
        [Tooltip("Vitesse de la transformation, en images par seconde (personnages : 6)")]
        [SerializeField] private float transformFramesPerSecond = 6f;
        [Tooltip("Sans images de transformation : temps d'arrêt avant de changer de forme, en secondes")]
        [SerializeField] private float pauseWithoutFrames = 0.5f;

        public int CurrentForm { get; private set; }
        public bool IsTransforming { get; private set; }

        private static readonly int MoveX = Animator.StringToHash("moveX");
        private static readonly int MoveY = Animator.StringToHash("moveY");

        private Health health;
        private EnemyController controller;
        private Boss boss;
        private Animator animator;
        private SpriteRenderer spriteRenderer;
        private Sprite[] playingFrames;
        private float transformStart;

        private void Awake()
        {
            health = GetComponent<Health>();
            controller = GetComponent<EnemyController>();
            boss = GetComponent<Boss>();
            animator = GetComponent<Animator>();
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void Start()
        {
            if (forms.Length > 0)
            {
                if (forms[0].controller != null && animator != null)
                    animator.runtimeAnimatorController = forms[0].controller;
                if (forms[0].badge != null)
                    boss.SetBadge(forms[0].badge);
            }
            // Dans Start : les PV max sont lus par Health.Awake
            SetFloor();
        }

        private void OnEnable() => health.Damaged += OnDamaged;

        private void OnDisable() => health.Damaged -= OnDamaged;

        // Forme suivante prête (controller rempli) et seuil défini
        private bool HasNextForm => CurrentForm < healthThresholds.Length && CurrentForm + 1 < forms.Length
            && forms[CurrentForm + 1].controller != null;

        private float NextThreshold => healthThresholds[CurrentForm] * health.Max;

        private void SetFloor()
        {
            health.MinHealth = HasNextForm ? NextThreshold : 0f;
        }

        private void OnDamaged(float amount, bool isCritical)
        {
            if (IsTransforming || health.IsDead || !HasNextForm)
                return;
            if (health.Current <= NextThreshold + 0.001f)
                StartCoroutine(Transformation());
        }

        private IEnumerator Transformation()
        {
            IsTransforming = true;
            health.IsInvulnerable = true;
            controller.IsPaused = true;

            Sprite[] frames = forms[CurrentForm].transformFrames;
            if (frames != null && frames.Length > 0 && transformFramesPerSecond > 0f)
            {
                // Animator coupé : les images sont posées en LateUpdate
                if (animator != null)
                    animator.enabled = false;
                playingFrames = frames;
                transformStart = Time.time;
                yield return new WaitForSeconds(frames.Length / transformFramesPerSecond);
                playingFrames = null;
            }
            else
                yield return new WaitForSeconds(pauseWithoutFrames);

            CurrentForm++;
            Form form = forms[CurrentForm];
            if (animator != null)
            {
                animator.runtimeAnimatorController = form.controller;
                animator.enabled = true;
                // Nouveau controller = paramètres remis à zéro : le boss garde la direction regardée
                Vector2 facing = controller.Facing;
                animator.SetFloat(MoveX, facing.x);
                animator.SetFloat(MoveY, facing.y);
            }
            boss.SetBadge(form.badge);

            SetFloor();
            health.IsInvulnerable = false;
            controller.IsPaused = false;
            IsTransforming = false;
        }

        // Après l'Animator (même s'il est réactivé par un autre effet, ballon de Gotenks) : l'image de transformation gagne
        private void LateUpdate()
        {
            if (playingFrames == null || spriteRenderer == null)
                return;
            int frame = Mathf.Min((int)((Time.time - transformStart) * transformFramesPerSecond), playingFrames.Length - 1);
            spriteRenderer.sprite = playingFrames[frame];
        }
    }
}
