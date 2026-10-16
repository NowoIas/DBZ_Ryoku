using System.Collections.Generic;
using UnityEngine;

namespace DBZ.World
{
    // Calcul de chemin sur la grille, case par case, avec les mêmes règles qu'un pas du joueur :
    // calque de collision du niveau de hauteur (cases pleines, murs, sens uniques) et tuiles Niveaux
    // (escaliers, rampes). Une case est donc un couple (case, niveau) : le dessous et le dessus d'un pont sont distincts.
    // Les objets qui bloquent (autres ennemis, cibles, rochers) ne sont pas testés : ils bougent, et les tester
    // coûterait cher ; celui qui suit le chemin les vérifie au moment de faire chaque pas, et peut donner
    // les cases trouvées occupées (avoidCells) pour que le calcul suivant les contourne.
    // Recherche en largeur (le plus court chemin en nombre de pas), limitée en distance et en nombre de cases
    // pour rester léger même avec beaucoup d'ennemis.
    public static class GridPathfinder
    {
        private static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        // Réutilisés d'un appel à l'autre : pas de nouvelle mémoire à chaque calcul (important sur le Web)
        private static readonly Queue<Vector3Int> queue = new Queue<Vector3Int>();
        // Pour chaque case atteinte (x, y, niveau), la case d'où l'on vient
        private static readonly Dictionary<Vector3Int, Vector3Int> cameFrom = new Dictionary<Vector3Int, Vector3Int>();

        // Cherche un chemin de start (au niveau startLevel) jusqu'à une case voisine de target, au niveau targetLevel,
        // d'où l'on peut faire un pas vers target (pas de mur entre les deux). On ne passe jamais sur target.
        // path reçoit les directions des pas à faire, dans l'ordre.
        // Renvoie faux si aucun chemin n'est trouvé dans les limites : path mène alors au plus près de target
        // (vide si l'on y est déjà).
        // maxRadius : distance maximale (en cases, sur chaque axe) depuis start ; maxNodes : nombre maximal de cases explorées
        public static bool FindPathToNeighbor(Vector2Int start, int startLevel, Vector2Int target, int targetLevel,
                                              int maxRadius, int maxNodes, List<Vector2> path,
                                              List<Vector2Int> avoidCells = null)
        {
            return Search(start, startLevel, target, targetLevel, true, maxRadius, maxNodes, path, avoidCells);
        }

        // Cherche un chemin de start jusqu'à la case target elle-même (à n'importe quel niveau) : retour d'un ennemi
        // à son point de départ. Même fonctionnement que FindPathToNeighbor
        public static bool FindPathTo(Vector2Int start, int startLevel, Vector2Int target,
                                      int maxRadius, int maxNodes, List<Vector2> path,
                                      List<Vector2Int> avoidCells = null)
        {
            return Search(start, startLevel, target, startLevel, false, maxRadius, maxNodes, path, avoidCells);
        }

        // toNeighbor : but = case voisine de target au niveau targetLevel (sans passer sur target) ;
        // sinon but = target elle-même, quel que soit le niveau
        private static bool Search(Vector2Int start, int startLevel, Vector2Int target, int targetLevel, bool toNeighbor,
                                   int maxRadius, int maxNodes, List<Vector2> path, List<Vector2Int> avoidCells)
        {
            path.Clear();
            queue.Clear();
            cameFrom.Clear();

            var startNode = new Vector3Int(start.x, start.y, startLevel);
            cameFrom[startNode] = startNode;
            queue.Enqueue(startNode);

            Vector3Int best = startNode;
            int bestScore = Score(startNode, target, toNeighbor ? targetLevel : startLevel);
            bool found = false;

            while (queue.Count > 0)
            {
                Vector3Int node = queue.Dequeue();
                if (toNeighbor ? IsNextTo(node, target, targetLevel) : node.x == target.x && node.y == target.y)
                {
                    best = node;
                    found = true;
                    break;
                }

                int score = Score(node, target, toNeighbor ? targetLevel : node.z);
                if (score < bestScore)
                {
                    best = node;
                    bestScore = score;
                }

                if (cameFrom.Count >= maxNodes)
                    continue;

                foreach (var dir in Directions)
                {
                    if (!CollisionMap.CanMoveOnLevel(Center(node), (Vector2)dir, node.z))
                        continue;

                    var cell = new Vector2Int(node.x + dir.x, node.y + dir.y);
                    if (toNeighbor && cell == target)
                        continue;
                    if (Mathf.Abs(cell.x - start.x) > maxRadius || Mathf.Abs(cell.y - start.y) > maxRadius)
                        continue;
                    // Case trouvée occupée par un objet (liste courte : quelques cases au plus)
                    if (avoidCells != null && avoidCells.Contains(cell))
                        continue;

                    int cellLevel = LevelMap.LevelAfterEntering(Center(cell), node.z);
                    var next = new Vector3Int(cell.x, cell.y, cellLevel);
                    if (cameFrom.ContainsKey(next))
                        continue;

                    cameFrom[next] = node;
                    queue.Enqueue(next);
                }
            }

            // On remonte de la case retenue jusqu'au départ, puis on remet les pas dans l'ordre
            for (Vector3Int node = best; node != startNode; node = cameFrom[node])
            {
                Vector3Int previous = cameFrom[node];
                path.Add(new Vector2(node.x - previous.x, node.y - previous.y));
            }
            path.Reverse();
            return found;
        }

        // Case voisine de target, au bon niveau, sans mur entre les deux
        private static bool IsNextTo(Vector3Int node, Vector2Int target, int targetLevel)
        {
            if (node.z != targetLevel)
                return false;
            int dx = target.x - node.x;
            int dy = target.y - node.y;
            if (Mathf.Abs(dx) + Mathf.Abs(dy) != 1)
                return false;
            return CollisionMap.CanMoveOnLevel(Center(node), new Vector2(dx, dy), node.z);
        }

        // Plus petit = plus près de la cible (sans chemin complet, on s'approche au mieux)
        private static int Score(Vector3Int node, Vector2Int target, int targetLevel)
        {
            int distance = Mathf.Abs(target.x - node.x) + Mathf.Abs(target.y - node.y);
            return distance * 2 + (node.z == targetLevel ? 0 : 1);
        }

        private static Vector3 Center(Vector3Int node)
        {
            return new Vector3(node.x + 0.5f, node.y + 0.5f, 0f);
        }

        private static Vector3 Center(Vector2Int cell)
        {
            return new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
        }
    }
}
