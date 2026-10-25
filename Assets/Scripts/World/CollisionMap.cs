using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DBZ.World
{
    // Calque de collision d'un niveau de hauteur (0 = sol, 1 = haut...), peint avec des CollisionTile
    // par-dessus la carte dessinée. Répond à « peut-on faire un pas d'ici dans cette direction ? »
    // (joueur, et plus tard ennemis). Visible dans l'éditeur, caché en jeu.
    [RequireComponent(typeof(Tilemap))]
    public class CollisionMap : MonoBehaviour
    {
        [Tooltip("Niveau de hauteur de ce calque (0 = sol, 1 = haut...)")]
        [SerializeField] private int level;
        [Tooltip("Cacher les tuiles de collision en jeu (elles restent visibles dans l'éditeur)")]
        [SerializeField] private bool hideInPlayMode = true;

        public int Level => level;

        // Tous les calques de collision actifs : le joueur et les techniques les consultent sans réglage dans l'Inspector
        private static readonly List<CollisionMap> activeMaps = new List<CollisionMap>();

        private Tilemap tilemap;

        private void Awake()
        {
            tilemap = GetComponent<Tilemap>();
            if (hideInPlayMode && TryGetComponent(out TilemapRenderer tilemapRenderer))
                tilemapRenderer.enabled = false;
        }

        private void OnEnable()
        {
            activeMaps.Add(this);
        }

        private void OnDisable()
        {
            activeMaps.Remove(this);
        }

        // Un pas d'une case (joueur) sur le calque de collision de ce niveau de hauteur
        public static bool CanMoveOnLevel(Vector3 fromWorld, Vector2 direction, int level)
        {
            // Sol infranchissable à pied (eau, montagnes : BlockingGround) ; au-dessus de l'eau, les techniques passent
            if (BlockingGround.IsBlocked(fromWorld + (Vector3)direction, level))
                return false;
            if (IsWalkBlockingCell(fromWorld + (Vector3)direction, level))
                return false;
            if (BlockingGround.BlocksTechniques(fromWorld + (Vector3)direction, level))
                return false;
            foreach (var map in activeMaps)
            {
                if (map.level == level && !map.CanMove(fromWorld, direction, true))
                    return false;
            }
            return true;
        }

        // Techniques (Ki Blast, Kamehameha) : même règle qu'un pas (case pleine, murs, sens unique dans le sens
        // qu'il bloque), sur le calque de collision du niveau de hauteur de la technique ; arrêtées aussi par
        // les montagnes du sol (BlockingGround), comme par un mur
        public static bool CanTechniquePass(Vector3 fromWorld, Vector2 direction, int level)
        {
            if (BlockingGround.BlocksTechniques(fromWorld + (Vector3)direction, level))
                return false;
            foreach (var map in activeMaps)
            {
                if (map.level == level && !map.CanMove(fromWorld, direction))
                    return false;
            }
            return true;
        }

        // Case qui arrête les techniques en entier (case pleine, montagne), à la différence d'un mur fin sur un bord :
        // un rayon peut y dessiner son impact
        public static bool IsTechniqueSolidCell(Vector3 world, int level)
        {
            return IsSolidCell(world, level) || BlockingGround.BlocksTechniques(world, level);
        }

        // Atterrissage refusé : case pleine, case d'eau peinte, ou sol infranchissable à pied (eau, montagnes)
        public static bool BlocksLanding(Vector3 world, int level)
        {
            return IsSolidCell(world, level) || IsWalkBlockingCell(world, level) || BlockingGround.IsBlocked(world, level);
        }

        // Case d'eau peinte (blocksWalking) sur le calque de ce niveau : ni marche ni atterrissage ;
        // CanTechniquePass ne la lit pas, les techniques passent
        public static bool IsWalkBlockingCell(Vector3 world, int level)
        {
            foreach (var map in activeMaps)
            {
                if (map.level != level)
                    continue;
                var tile = map.tilemap.GetTile<CollisionTile>(map.tilemap.WorldToCell(world));
                if (tile != null && tile.blocksWalking)
                    return true;
            }
            return false;
        }

        // Case pleine sur le calque de ce niveau (l'impact d'une technique se joue alors dans la case)
        public static bool IsSolidCell(Vector3 world, int level)
        {
            foreach (var map in activeMaps)
            {
                if (map.level != level)
                    continue;
                var tile = map.tilemap.GetTile<CollisionTile>(map.tilemap.WorldToCell(world));
                if (tile != null && tile.isSolid)
                    return true;
            }
            return false;
        }

        // Un pas d'une case dans une des 4 directions : bloqué par un mur (ou un sens unique) du côté franchi,
        // dans la case de départ comme dans celle d'arrivée, ou par une case d'arrivée pleine.
        // À pied (walking), les bords d'eau (walkWalls) bloquent aussi ; les techniques les traversent
        public bool CanMove(Vector3 fromWorld, Vector2 direction, bool walking = false)
        {
            CellSides exitSide = CollisionTile.SideOf(direction);
            CellSides entrySide = CollisionTile.SideOf(-direction);
            Vector3Int from = tilemap.WorldToCell(fromWorld);
            Vector3Int to = from + new Vector3Int(Mathf.RoundToInt(direction.x), Mathf.RoundToInt(direction.y), 0);

            // En sortant : seuls les murs bloquent (un sens unique laisse justement sortir)
            var fromTile = tilemap.GetTile<CollisionTile>(from);
            if (fromTile != null && (WallsOf(fromTile, walking) & exitSide) != 0)
                return false;

            // En entrant : case pleine, mur, ou sens unique (on ne rentre pas par un rebord)
            var toTile = tilemap.GetTile<CollisionTile>(to);
            if (toTile != null && (toTile.isSolid || ((WallsOf(toTile, walking) | toTile.oneWayExits) & entrySide) != 0))
                return false;

            return true;
        }

        private static CellSides WallsOf(CollisionTile tile, bool walking)
        {
            return walking ? tile.walls | tile.walkWalls : tile.walls;
        }
    }
}
