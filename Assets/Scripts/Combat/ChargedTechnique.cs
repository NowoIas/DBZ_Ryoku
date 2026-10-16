using UnityEngine;

namespace DBZ.Combat
{
    // Technique de la touche R, chargée tant que la touche est tenue puis lâchée au relâchement (Genkidama de Goku,
    // Big Bang de Vegeta). Le PlayerController la crée sur la case du joueur et ne connaît qu'elle : chaque personnage
    // choisit la sienne dans sa fiche (CharacterData)
    public abstract class ChargedTechnique : MonoBehaviour
    {
        [Header("Pause")]
        [Tooltip("Pause après le relâchement, avant de pouvoir lancer une autre attaque (secondes, 0 = aucune)")]
        [Min(0f)]
        [SerializeField] private float cooldown = 0f;

        public float Cooldown => cooldown;

        // À l'appui, juste après la création : playerCellCenter = centre de la case du joueur,
        // dir = direction du joueur, level = son niveau de hauteur. La technique se place elle-même
        public abstract void Begin(Vector2 playerCellCenter, Vector2 dir, int level);

        // Touche relâchée
        public abstract void Release();

        // Joueur K.O. pendant la charge : la technique disparaît
        public abstract void Cancel();
    }
}
