using System.Collections;
using UnityEngine;

namespace DBZ.Enemies
{
    // Attaque spéciale de Freezer : il pointe le doigt (état Special, planche s1 : image 0 = il se prépare, image 1 = tir)
    // et lance un rayon fin à tête chercheuse (Projectile avec HomingTarget) qui suit Goku en tournant au centre des cases.
    // Une fois lancé, le rayon vit seul : Freezer reprend sa fuite
    public class HomingBeam : EnemySpecialAttack
    {
        [Tooltip("Rayon lancé (Projectile, images tournées : Rotate With Direction ; virages : Homing Turn Every Cells / Max Turns)")]
        [SerializeField] private Projectile projectilePrefab;
        [Tooltip("Moment du tir dans l'animation (0 à 1 ; 0,5 = début de la 2e image sur 2)")]
        [Range(0f, 1f)]
        [SerializeField] private float fireTime = 0.5f;
        [Tooltip("Distance (en cases) depuis le centre de Freezer où apparaît le rayon")]
        [SerializeField] private float spawnOffset = 0.75f;

        private static readonly int SpecialState = Animator.StringToHash("Special");

        protected override IEnumerator Perform(EnemyController owner)
        {
            Animator animator = owner.GetComponent<Animator>();
            animator.Play(SpecialState, 0, 0f);
            // L'état n'est pris en compte qu'à l'image suivante
            yield return null;

            // Attente du moment du tir (tiré quand même si l'animation a été quittée)
            while (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == SpecialState
                   && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < fireTime)
                yield return null;
            Fire(owner);

            // Fin de l'animation (la transition de sortie ramène à l'attente)
            while (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == SpecialState
                   && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
                yield return null;
        }

        private void Fire(EnemyController owner)
        {
            if (projectilePrefab == null)
                return;
            Vector2 direction = owner.Facing;
            Vector3 spawnPos = owner.transform.position + (Vector3)(direction * spawnOffset);
            Projectile projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
            projectile.Owner = owner.transform;
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
                projectile.HomingTarget = player.transform;
            projectile.Launch(direction, owner.Level);
        }
    }
}
