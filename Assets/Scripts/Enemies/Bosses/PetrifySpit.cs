using System.Collections;
using DBZ.Combat;
using UnityEngine;

namespace DBZ.Enemies
{
    // Attaque spéciale de Dabura (2026-11-26) : il se met aligné avec Goku (firingRange), annonce (image 0 de s1, bouche
    // fermée, tremblement d'1 px) puis crache (image 1, bouche ouverte) un projectile qui pétrifie Goku (Projectile.petrifyDuration).
    // Images posées par ce script (Animator coupé) : s1 n'existe que de face (tourné à gauche / à droite) et de dos (s1_haut,
    // dessiné par Claude) ; vers le bas : images tournées à gauche (choix Claude)
    public class PetrifySpit : EnemySpecialAttack
    {
        [Header("Images (0 annonce, 1 crachat)")]
        [SerializeField] private Sprite[] spritesUp = new Sprite[2];
        [SerializeField] private Sprite[] spritesDown = new Sprite[2];
        [SerializeField] private Sprite[] spritesLeft = new Sprite[2];
        [SerializeField] private Sprite[] spritesRight = new Sprite[2];

        [Header("Crachat")]
        [Tooltip("Projectile craché (DaburaSpit : Projectile avec Petrify Duration)")]
        [SerializeField] private Projectile projectilePrefab;
        [Tooltip("Annonce avant le crachat (secondes)")]
        [SerializeField] private float windupDuration = 0.4f;
        [Tooltip("Tremblement pendant l'annonce (pixels, 0 = aucun)")]
        [SerializeField] private int shakePixels = 1;
        [Tooltip("Pose du crachat gardée après le tir (secondes)")]
        [SerializeField] private float recoverDuration = 0.3f;
        [Tooltip("Distance (en cases) depuis le centre de Dabura où apparaît le crachat")]
        [SerializeField] private float spawnOffset = 0.75f;

        private static readonly int IdleState = Animator.StringToHash("Idle");
        private static readonly int MoveX = Animator.StringToHash("moveX");
        private static readonly int MoveY = Animator.StringToHash("moveY");

        private EnemyController controller;
        private Health health;
        private SpriteRenderer spriteRenderer;
        private Animator animator;
        private Vector3 basePosition;
        private Vector2 direction;
        // Attaque en cours : tout remettre en ordre si la coroutine est coupée de l'extérieur (ballon, étourdi, mort)
        private bool running;
        private int lastTickFrame;

        private void Awake()
        {
            controller = GetComponent<EnemyController>();
            health = GetComponent<Health>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            animator = GetComponent<Animator>();
        }

        // Goku aligné (même ligne ou colonne), même niveau, à portée, ligne dégagée
        public override bool CanUse(Vector2Int delta, bool sameLevel)
        {
            if (!sameLevel || (delta.x != 0 && delta.y != 0) || delta == Vector2Int.zero)
                return false;
            int distance = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
            return distance <= range && controller.HasClearShot(delta);
        }

        protected override IEnumerator Perform(EnemyController owner)
        {
            direction = owner.Facing;
            running = true;
            basePosition = transform.position;
            Tick();
            animator.enabled = false;
            Sprite[] sprites = SpritesFor(direction);

            // Annonce : bouche fermée, léger tremblement sur le côté
            Vector3 side = direction.x != 0f ? Vector3.up : Vector3.right;
            for (float t = 0f; t < windupDuration; t += Time.deltaTime)
            {
                if (IsDead())
                {
                    Cleanup(false);
                    yield break;
                }
                SetSprite(sprites, 0);
                int step = shakePixels > 0 ? (Mathf.FloorToInt(t / 0.05f) % 2 == 0 ? 1 : -1) * shakePixels : 0;
                transform.position = basePosition + side * (step / 32f);
                Tick();
                yield return null;
            }
            transform.position = basePosition;

            // Crachat
            SetSprite(sprites, 1);
            Fire(owner);
            for (float t = 0f; t < recoverDuration; t += Time.deltaTime)
            {
                if (IsDead())
                {
                    Cleanup(false);
                    yield break;
                }
                SetSprite(sprites, 1);
                Tick();
                yield return null;
            }
            Cleanup(true);
        }

        private void Fire(EnemyController owner)
        {
            if (projectilePrefab == null)
                return;
            Projectile projectile = Instantiate(projectilePrefab, basePosition + (Vector3)(direction * spawnOffset), Quaternion.identity);
            projectile.Owner = owner.transform;
            projectile.Launch(direction, owner.Level);
        }

        // Coroutine coupée de l'extérieur (ballon, étourdi) : tout est remis en ordre à l'image suivante
        private void LateUpdate()
        {
            if (running && lastTickFrame < Time.frameCount - 1)
                Cleanup(!IsDead());
        }

        private void OnDisable()
        {
            if (running)
                Cleanup(false);
        }

        private void Tick() => lastTickFrame = Time.frameCount;

        private bool IsDead() => health == null || health.IsDead;

        private void Cleanup(bool restoreAnimator)
        {
            running = false;
            transform.position = basePosition;
            if (restoreAnimator && animator != null)
            {
                animator.enabled = true;
                animator.SetFloat(MoveX, direction.x);
                animator.SetFloat(MoveY, direction.y);
                animator.Play(IdleState, 0, 0f);
            }
        }

        private Sprite[] SpritesFor(Vector2 dir)
        {
            if (dir.y > 0.5f) return spritesUp;
            if (dir.y < -0.5f) return spritesDown;
            return dir.x < 0f ? spritesLeft : spritesRight;
        }

        private void SetSprite(Sprite[] sprites, int index)
        {
            if (sprites != null && index < sprites.Length && sprites[index] != null)
                spriteRenderer.sprite = sprites[index];
        }
    }
}
