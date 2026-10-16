using UnityEngine;
using UnityEngine.Tilemaps;

namespace DBZ.World
{
    // Tuile de logique du calque Niveaux (LevelMap) : en arrivant sur cette case, on passe au niveau indiqué.
    // À poser sur les escaliers, rampes et cases d'arrivée des rebords. Cachée en jeu.
    [CreateAssetMenu(fileName = "niveau_", menuName = "DBZ/Level Tile")]
    public class LevelTile : Tile
    {
        [Tooltip("Niveau de hauteur pris en arrivant sur la case (0 = sol, 1 = haut...)")]
        public int level;

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            base.GetTileData(position, tilemap, ref tileData);
            tileData.colliderType = ColliderType.None;
        }
    }
}
