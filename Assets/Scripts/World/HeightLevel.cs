using System.Collections.Generic;
using UnityEngine;

namespace DBZ.World
{
    // Niveau de hauteur actuel d'un objet qui peut bloquer ou être touché (0 = sol, 1 = haut...).
    // Goku et les ennemis le mettent à jour en marchant ; un objet fixe (cible, calque Destructibles) garde celui
    // réglé dans l'Inspector. Un objet SANS ce composant est au niveau 0 (au sol).
    // Règle : un coup, une technique ou un objet qui bloque n'agit que sur ce qui est à son niveau
    public class HeightLevel : MonoBehaviour
    {
        // Niveau d'un objet en vol (Goku) : au-dessus de tout, rien ne le bloque ni ne le touche
        public const int Air = 100;

        [Tooltip("Niveau de hauteur (0 = sol, 1 = haut). Mis à jour par le script de déplacement pour Goku et les ennemis")]
        [SerializeField] private int level;

        public int Level
        {
            get => level;
            set => level = value;
        }

        // Niveau de l'objet auquel appartient ce collider (composant sur lui ou sur un parent ; sinon le sol)
        public static int Of(Component hit)
        {
            var height = hit.GetComponentInParent<HeightLevel>();
            return height != null ? height.level : 0;
        }

        public static bool IsOnLevel(Component hit, int wantedLevel)
        {
            return Of(hit) == wantedLevel;
        }

        // Premier collider de la liste à ce niveau (null si aucun)
        public static Collider2D FirstOnLevel(IEnumerable<Collider2D> hits, int wantedLevel)
        {
            foreach (var hit in hits)
            {
                if (hit != null && IsOnLevel(hit, wantedLevel))
                    return hit;
            }
            return null;
        }
    }
}
