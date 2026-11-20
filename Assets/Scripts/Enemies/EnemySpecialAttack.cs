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

        [Header("Mise en place (ennemi de corps à corps)")]
        [Tooltip("Attaque prête : un ennemi de corps à corps s'écarte pour se placer aligné avec Goku, à cette distance "
            + "(en cases, de ... à ...), puis la lance. 0 = il ne s'écarte pas (attaque lancée sur place)")]
        [SerializeField] protected Vector2Int firingRange = Vector2Int.zero;
        [Tooltip("Temps maximal passé à chercher sa place (secondes) ; au-delà, l'attaque est remise à plus tard")]
        [SerializeField] protected float maxRepositionTime = 4f;
        [Tooltip("Attaque remise à plus tard (place pas trouvée à temps) : nouvel essai après ce délai (secondes)")]
        [SerializeField] protected float retryDelay = 2f;

        private float nextTime = float.MinValue;

        public bool IsReady => Time.time >= nextTime;
        public bool WantsFiringSpot => firingRange.y > 0;
        public Vector2Int FiringRange => firingRange;
        public float MaxRepositionTime => maxRepositionTime;
        // Tir horizontal parti de cette rangée au-dessus de l'ennemi (Cell : mains à hauteur de la case du dessus) ; 0 = sa rangée
        public virtual int HorizontalRowOffset => 0;

        // Place pas trouvée à temps : il reprend le corps à corps et réessaiera plus tard
        public void Postpone()
        {
            nextTime = Time.time + retryDelay;
        }

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
