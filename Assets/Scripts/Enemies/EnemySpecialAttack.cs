using System.Collections;
using UnityEngine;

namespace DBZ.Enemies
{
    // Attaque spéciale d'un boss : un composant par attaque (toupie de Raditz…), posé sur le prefab à côté de l'EnemyController,
    // qui le trouve seul et la lance dès qu'elle est prête et que Goku est à portée
    public abstract class EnemySpecialAttack : MonoBehaviour
    {
        [Tooltip("Pause entre deux attaques spéciales, en secondes (comptée depuis la fin de l'attaque)")]
        [SerializeField] protected float cooldown = 6f;
        [Tooltip("Distance maximale de Goku, en cases sur chaque axe (diagonales comprises)")]
        [SerializeField] protected int range = 4;
        [Tooltip("Délai avant la première attaque spéciale, une fois Goku repéré, en secondes")]
        [SerializeField] protected float firstDelay = 2f;

        private float nextTime = float.MinValue;

        public bool IsReady => Time.time >= nextTime;

        // Appelé quand Goku est repéré : la première attaque ne part pas tout de suite
        public void OnChaseStarted()
        {
            nextTime = Mathf.Max(nextTime, Time.time + firstDelay);
        }

        // delta : case de Goku moins case du boss ; sameLevel : Goku au même niveau de hauteur
        public virtual bool CanUse(Vector2Int delta, bool sameLevel)
        {
            return sameLevel && Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) <= range;
        }

        // Joué par l'EnemyController (coroutine) ; la pause démarre à la fin
        public IEnumerator Run(EnemyController owner)
        {
            yield return Perform(owner);
            nextTime = Time.time + cooldown;
        }

        protected abstract IEnumerator Perform(EnemyController owner);
    }
}
