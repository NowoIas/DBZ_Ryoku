using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DBZ.World
{
    // Côtés d'une case, combinables (ex. Up | Left pour un coin)
    [Flags]
    public enum CellSides
    {
        None = 0,
        Up = 1,
        Down = 2,
        Left = 4,
        Right = 8,
    }

    // Tuile de logique, posée sur un calque de collision (CollisionMap) par-dessus la carte dessinée.
    // Cachée en jeu : elle dit seulement si on peut entrer dans la case, et par quels côtés on peut en sortir.
    [CreateAssetMenu(fileName = "collision_", menuName = "DBZ/Collision Tile")]
    public class CollisionTile : Tile
    {
        [Tooltip("Case pleine : infranchissable, arrête aussi les techniques (collider sur le layer Terrain)")]
        public bool isSolid;
        [Tooltip("Murs : côtés qu'on ne franchit dans aucun sens (clôture, bord de falaise, côtés d'un pont)")]
        public CellSides walls;
        [Tooltip("Sens unique : côtés par lesquels on peut sortir de la case mais pas y entrer "
            + "(rebord : on descend, on ne remonte pas)")]
        public CellSides oneWayExits;

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            base.GetTileData(position, tilemap, ref tileData);
            // Seules les cases pleines ont un collider (layer Terrain) : les murs et les sens uniques sont lus
            // par CollisionMap (déplacement et techniques)
            tileData.colliderType = isSolid ? ColliderType.Grid : ColliderType.None;
        }

        // Côté de la case traversé en allant dans cette direction
        public static CellSides SideOf(Vector2 direction)
        {
            if (direction == Vector2.up) return CellSides.Up;
            if (direction == Vector2.down) return CellSides.Down;
            if (direction == Vector2.left) return CellSides.Left;
            if (direction == Vector2.right) return CellSides.Right;
            return CellSides.None;
        }
    }
}
