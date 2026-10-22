using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

namespace DBZ.World
{
    // Sur le calque du sol (Background) : les tuiles listées (eau, montagnes...) bloquent au niveau 0 sans
    // peindre de collision par-dessus (la carte du monde en compte des dizaines de milliers : autant de cases
    // de collision alourdiraient beaucoup la scène). Le vol passe ; l'atterrissage y est refusé.
    // Niveau 1 non concerné : un pont au-dessus d'une rivière reste praticable.
    [RequireComponent(typeof(Tilemap))]
    public class BlockingGround : MonoBehaviour
    {
        [Tooltip("Infranchissables à pied, les techniques passent au-dessus (eau)")]
        [FormerlySerializedAs("blockingTiles")]
        [SerializeField] private TileBase[] walkBlockingTiles;
        [Tooltip("Infranchissables à pied et arrêtent les techniques comme un mur (montagnes)")]
        [SerializeField] private TileBase[] solidTiles;

        // Calques actifs : CollisionMap les consulte sans réglage dans l'Inspector
        private static readonly List<BlockingGround> activeGrounds = new List<BlockingGround>();

        private Tilemap tilemap;
        private HashSet<TileBase> walkBlocking;
        private HashSet<TileBase> solid;

        private void Awake()
        {
            tilemap = GetComponent<Tilemap>();
            walkBlocking = new HashSet<TileBase>(walkBlockingTiles);
            solid = new HashSet<TileBase>(solidTiles);
        }

        private void OnEnable()
        {
            activeGrounds.Add(this);
        }

        private void OnDisable()
        {
            activeGrounds.Remove(this);
        }

        // Case du sol infranchissable à pied à ce niveau de hauteur (eau ou montagne)
        public static bool IsBlocked(Vector3 world, int level)
        {
            if (level != 0)
                return false;
            foreach (var ground in activeGrounds)
            {
                var tile = ground.TileAt(world);
                if (ground.walkBlocking.Contains(tile) || ground.solid.Contains(tile))
                    return true;
            }
            return false;
        }

        // Case du sol qui arrête aussi les techniques à ce niveau de hauteur (montagne)
        public static bool BlocksTechniques(Vector3 world, int level)
        {
            if (level != 0)
                return false;
            foreach (var ground in activeGrounds)
            {
                if (ground.solid.Contains(ground.TileAt(world)))
                    return true;
            }
            return false;
        }

        private TileBase TileAt(Vector3 world)
        {
            return tilemap.GetTile(tilemap.WorldToCell(world));
        }
    }
}
