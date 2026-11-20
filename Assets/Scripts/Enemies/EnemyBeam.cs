using System.Collections;
using DBZ.Combat;
using UnityEngine;

namespace DBZ.Enemies
{
    // Attaque spéciale de Cell : Kamehameha, même mécanique que chez les persos. Charge (état Special, image 0 de s1),
    // puis tir (état SpecialFire, images 1 et 2 en alternance) : le rayon part de la case devant lui et reste tenu
    // holdDuration secondes avant d'être relâché (la traînée l'efface).
    // Avec Firing Range rempli, Cell (corps à corps) s'écarte d'abord pour se placer aligné avec Goku (EnemyController)
    public class EnemyBeam : EnemySpecialAttack
    {
        [Header("Rayon")]
        [Tooltip("Rayon tiré (KamehamehaBeam avec Fired By Enemy coché, Hit Layers : SolidObjects + Player)")]
        [SerializeField] private KamehamehaBeam beamPrefab;
        [Tooltip("Durée de la charge avant le tir (secondes)")]
        [SerializeField] private float chargeDuration = 0.5f;
        [Tooltip("Durée pendant laquelle le rayon est tenu (secondes)")]
        [SerializeField] private float holdDuration = 1.5f;
        [Tooltip("Distance minimale de Goku pour tirer, en cases (au contact, il préfère frapper)")]
        [SerializeField] private int minDistance = 2;
        [Tooltip("Tir à gauche / à droite parti de cette rangée au-dessus de l'ennemi (Cell, 64 px de haut : 1 = mains à hauteur "
            + "de la case du dessus ; il se place alors une rangée sous Goku). 0 = sa rangée. Haut / bas : inchangés")]
        [Min(0)]
        [SerializeField] private int horizontalShotRowOffset = 0;

        public override int HorizontalRowOffset => horizontalShotRowOffset;

        private static readonly int SpecialState = Animator.StringToHash("Special");
        private static readonly int SpecialFireState = Animator.StringToHash("SpecialFire");
        private static readonly int IdleState = Animator.StringToHash("Idle");

        private EnemyController controller;
        private KamehamehaBeam activeBeam;

        private void Awake()
        {
            controller = GetComponent<EnemyController>();
        }

        // Aligné avec Goku (même colonne, ou à gauche / à droite horizontalShotRowOffset rangées sous lui), même niveau,
        // entre minDistance et range, rien entre eux
        public override bool CanUse(Vector2Int delta, bool sameLevel)
        {
            if (!sameLevel || controller == null)
                return false;
            if (delta.x == 0)
                return Mathf.Abs(delta.y) >= minDistance && Mathf.Abs(delta.y) <= range && controller.HasClearShot(delta);
            if (delta.y == horizontalShotRowOffset)
                return Mathf.Abs(delta.x) >= minDistance && Mathf.Abs(delta.x) <= range
                    && controller.HasClearShotFromRow(delta, horizontalShotRowOffset);
            return false;
        }

        protected override IEnumerator Perform(EnemyController owner)
        {
            Animator animator = owner.GetComponent<Animator>();
            Health health = owner.GetComponent<Health>();
            SpriteRenderer spriteRenderer = owner.GetComponent<SpriteRenderer>();
            int baseOrder = spriteRenderer.sortingOrder;

            animator.Play(SpecialState, 0, 0f);
            float end = Time.time + chargeDuration;
            while (Time.time < end)
            {
                if (IsInterrupted(health))
                    yield break;
                yield return null;
            }

            animator.Play(SpecialFireState, 0, 0f);
            if (beamPrefab != null)
            {
                Vector2 direction = owner.Facing;
                // Tir horizontal : départ de la rangée des mains (au-dessus de lui pour Cell)
                Vector3 origin = owner.transform.position;
                if (direction.y == 0f)
                    origin += Vector3.up * horizontalShotRowOffset;
                activeBeam = Instantiate(beamPrefab, origin + (Vector3)direction, Quaternion.identity);
                activeBeam.Launch(direction, owner.Level);
            }

            // Tenu holdDuration, ou jusqu'au bout d'un duel de rayons ; battu (repoussé par Goku) : fin du tir
            end = Time.time + holdDuration;
            while (!IsInterrupted(health) && activeBeam != null && !activeBeam.IsReleased
                   && (Time.time < end || activeBeam.IsClashing))
            {
                // Comme le joueur : devant son rayon (haut, côtés), sous lui quand le rayon passe sous la cible (bas)
                if (activeBeam != null)
                    spriteRenderer.sortingOrder = baseOrder + activeBeam.ShooterSortingOffset;
                yield return null;
            }

            ReleaseBeam();
            spriteRenderer.sortingOrder = baseOrder;
            if (!health.IsDead)
                animator.Play(IdleState, 0, 0f);
        }

        // K.O. ou transformé en ballon / étourdi : le rayon s'arrête
        private bool IsInterrupted(Health health)
        {
            return health.IsDead || StatusEffects.IsLockedOn(gameObject);
        }

        private void ReleaseBeam()
        {
            if (activeBeam != null)
                activeBeam.Release();
            activeBeam = null;
        }

        // Cell détruit pendant le tir (coroutine arrêtée) : le rayon ne doit pas rester affiché
        private void OnDisable()
        {
            ReleaseBeam();
        }
    }
}
