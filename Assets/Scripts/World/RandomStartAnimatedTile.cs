using UnityEngine;
using UnityEngine.Tilemaps;

namespace DBZ.World
{
    // Tuile animée (feu de camp...) dont chaque case démarre à un moment différent : sinon toutes les
    // tuiles identiques de la carte bougeraient exactement en même temps. Le décalage dépend de la case
    // (et non du hasard) pour rester le même à chaque rafraîchissement de la tilemap.
    // Créée par le menu DBZ > Créer une tuile animée (Scripts/Editor/AnimatedTileCreator).
    public class RandomStartAnimatedTile : AnimatedTile
    {
        // Plage du décalage de départ (s) : bien plus longue qu'un cycle pour bien mélanger les cases
        private const float MaxStartOffset = 10f;

        public override bool GetTileAnimationData(Vector3Int position, ITilemap tilemap, ref TileAnimationData tileAnimationData)
        {
            if (!base.GetTileAnimationData(position, tilemap, ref tileAnimationData))
                return false;
            tileAnimationData.animationStartTime += Hash01(position) * MaxStartOffset;
            return true;
        }

        // Nombre entre 0 et 1, toujours le même pour une case donnée
        private static float Hash01(Vector3Int position)
        {
            unchecked
            {
                uint h = (uint)position.x * 73856093u ^ (uint)position.y * 19349663u ^ (uint)position.z * 83492791u;
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }
    }
}
