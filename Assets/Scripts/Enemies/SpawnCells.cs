using System.Collections.Generic;
using DBZ.World;
using UnityEngine;

namespace DBZ.Enemies
{
    // Choix de cases d'apparition autour d'un invocateur (sbires de Cell, tourelles de Dr Gero) :
    // cases libres atteignables à pied depuis sa case, au même niveau, ni sur Goku ni sur un objet qui bloque
    public static class SpawnCells
    {
        private static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        // count cases à distance (en pas) dans range (de ... à ...), mélangées ; pas assez de place : plus près, puis plus loin.
        // ignore : objet dont les colliders ne bloquent pas (l'invocateur lui-même)
        public static List<Vector2Int> Pick(Vector3 origin, int level, Vector2Int range, int count, LayerMask solidObjectsLayer, Transform ignore)
        {
            List<Vector2Int>[] byDistance = FindCellsByDistance(origin, level, range.y + 3, solidObjectsLayer, ignore);
            var chosen = new List<Vector2Int>();

            // Dans la plage voulue : toutes ses cases mélangées ensemble (sinon toutes à la plus petite distance)
            var inRange = new List<Vector2Int>();
            for (int d = range.x; d <= range.y && d < byDistance.Length; d++)
                inRange.AddRange(byDistance[d]);
            Shuffle(inRange);
            foreach (var c in inRange)
            {
                if (chosen.Count >= count) break;
                chosen.Add(c);
            }

            // Puis plus près, puis plus loin
            var order = new List<int>();
            for (int d = range.x - 1; d >= 1; d--) order.Add(d);
            for (int d = range.y + 1; d < byDistance.Length; d++) order.Add(d);
            foreach (int d in order)
            {
                Shuffle(byDistance[d]);
                foreach (var c in byDistance[d])
                {
                    if (chosen.Count >= count) break;
                    chosen.Add(c);
                }
            }
            return chosen;
        }

        // Cases libres atteignables à pied depuis la case de départ, rangées par nombre de pas, au même niveau qu'elle
        private static List<Vector2Int>[] FindCellsByDistance(Vector3 origin, int startLevel, int maxDistance, LayerMask solidObjectsLayer, Transform ignore)
        {
            var result = new List<Vector2Int>[maxDistance + 1];
            for (int i = 0; i <= maxDistance; i++) result[i] = new List<Vector2Int>();

            var start = new Vector3Int(Mathf.FloorToInt(origin.x), Mathf.FloorToInt(origin.y), startLevel);
            var visited = new List<Vector3Int> { start };
            var frontier = new List<Vector3Int> { start };

            Vector2Int playerCell = new Vector2Int(int.MinValue, int.MinValue);
            Vector2Int playerTarget = playerCell;
            var playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null && playerObject.TryGetComponent(out PlayerController player))
            {
                playerCell = Vector2Int.FloorToInt(player.transform.position);
                playerTarget = Vector2Int.FloorToInt(player.OccupiedCell);
            }

            for (int distance = 1; distance <= maxDistance; distance++)
            {
                var next = new List<Vector3Int>();
                foreach (var node in frontier)
                {
                    foreach (var dir in Directions)
                    {
                        Vector3 from = new Vector3(node.x + 0.5f, node.y + 0.5f, 0f);
                        if (!CollisionMap.CanMoveOnLevel(from, (Vector2)dir, node.z))
                            continue;
                        Vector3 center = from + new Vector3(dir.x, dir.y, 0f);
                        var cell = new Vector3Int(node.x + dir.x, node.y + dir.y, LevelMap.LevelAfterEntering(center, node.z));
                        if (visited.Contains(cell))
                            continue;
                        visited.Add(cell);
                        // Un objet qui bloque (punching-ball, autre ennemi, tourelle) : ni apparition ni passage
                        if (IsBlocked(center, cell.z, solidObjectsLayer, ignore))
                            continue;
                        next.Add(cell);
                        var flat = new Vector2Int(cell.x, cell.y);
                        if (cell.z == startLevel && flat != playerCell && flat != playerTarget)
                            result[distance].Add(flat);
                    }
                }
                frontier = next;
            }
            return result;
        }

        private static bool IsBlocked(Vector3 center, int level, LayerMask solidObjectsLayer, Transform ignore)
        {
            foreach (var hit in Physics2D.OverlapCircleAll(center, 0.3f, solidObjectsLayer))
            {
                if ((ignore == null || !hit.transform.IsChildOf(ignore)) && HeightLevel.IsOnLevel(hit, level))
                    return true;
            }
            return false;
        }

        private static void Shuffle(List<Vector2Int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
