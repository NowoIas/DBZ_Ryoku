using System.Collections;
using UnityEngine;

namespace DBZ.Enemies
{
    // Attaque spéciale de Raditz : il tourne sur lui-même (état Special, planche s1) et tire une boule à chaque image,
    // dans la direction où il regarde sur cette image. Chaque rangée de la planche commence dans la direction regardée,
    // puis tourne de 45° par image dans le sens inverse des aiguilles d'une montre
    public class SpinBarrage : EnemySpecialAttack
    {
        [Tooltip("Boule tirée à chaque image (images en 8 directions : Frames Eight Directions)")]
        [SerializeField] private Projectile projectilePrefab;
        [Tooltip("Nombre d'images de la toupie (une boule par image)")]
        [SerializeField] private int frameCount = 8;
        [Tooltip("Rotation entre deux images, en degrés (positif = sens inverse des aiguilles d'une montre)")]
        [SerializeField] private float degreesPerFrame = 45f;
        [Tooltip("Distance (en cases) depuis le centre de Raditz où apparaît la boule (au moins 0,72 : la boule doit naître " +
                 "dans la case voisine, diagonales comprises)")]
        [SerializeField] private float spawnOffset = 0.75f;

        private static readonly int SpecialState = Animator.StringToHash("Special");

        protected override IEnumerator Perform(EnemyController owner)
        {
            Animator animator = owner.GetComponent<Animator>();
            animator.Play(SpecialState, 0, 0f);
            // L'état n'est pris en compte qu'à l'image suivante
            yield return null;

            float startAngle = Mathf.Atan2(owner.Facing.y, owner.Facing.x) * Mathf.Rad2Deg;
            int fired = 0;
            while (fired < frameCount)
            {
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                bool inSpecial = info.shortNameHash == SpecialState;
                // Image affichée (toutes tirées si l'animation est finie ou a été quittée)
                int frame = inSpecial ? Mathf.Min(Mathf.FloorToInt(info.normalizedTime * frameCount), frameCount - 1) : frameCount - 1;
                while (fired <= frame)
                {
                    Fire(owner, startAngle + degreesPerFrame * fired);
                    fired++;
                }
                if (fired < frameCount)
                    yield return null;
            }

            // Fin de l'animation (la transition de sortie ramène à l'attente)
            while (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == SpecialState
                   && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
                yield return null;
        }

        private void Fire(EnemyController owner, float angle)
        {
            if (projectilePrefab == null)
                return;
            float radians = angle * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            // Directions exactes (évite 0,0000001 au lieu de 0, qui fausserait les tests de case)
            direction = new Vector2(Mathf.Round(direction.x * 1000f) / 1000f, Mathf.Round(direction.y * 1000f) / 1000f).normalized;
            Vector3 spawnPos = owner.transform.position + (Vector3)(direction * spawnOffset);
            Projectile projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
            projectile.Owner = owner.transform;
            projectile.Launch(direction, owner.Level);
        }
    }
}
