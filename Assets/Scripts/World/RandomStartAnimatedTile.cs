using UnityEngine;
using UnityEngine.Tilemaps;

namespace DBZ.World
{
    // Tuile animée (feu de camp...) créée par le menu DBZ > Créer une tuile animée (Scripts/Editor/AnimatedTileCreator).
    // Toutes les tuiles animées bougent en même temps (choix util. 2026-10-24) : le décalage de départ par case
    // (calculé sur la position) a été retiré. Le nom de la classe est gardé : les tuiles déjà créées l'utilisent.
    public class RandomStartAnimatedTile : AnimatedTile
    {
        // Case de la tuile dans son objet (feu de 2×2... ; x vers la droite, y vers le haut, (0,0) en bas à
        // gauche) : sert à l'outil de création (doublons) ; à réutiliser si un départ décalé par objet revient
        public Vector2Int blockCell;
    }
}
