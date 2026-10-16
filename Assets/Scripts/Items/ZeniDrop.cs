using System.Collections.Generic;
using DBZ.Combat;
using DBZ.World;
using UnityEngine;

namespace DBZ.Items
{
    // Au K.O. (Health.Died), lâche des pièces de zénis pour une somme tirée au hasard entre minValue et maxValue,
    // composée au hasard (ex. 7 : sept pièces de 1, ou 5 + 2...). Les pièces tombent sur les cases libres autour de l'objet
    // (au plus 3 par case, sans se chevaucher), sur les cases qu'on peut atteindre à pied depuis la sienne
    [RequireComponent(typeof(Health))]
    public class ZeniDrop : MonoBehaviour
    {
        [SerializeField] private ZeniCoin coinPrefab;
        [Tooltip("Somme minimale lâchée, en zénis")]
        [SerializeField] private int minValue = 7;
        [Tooltip("Somme maximale lâchée, en zénis")]
        [SerializeField] private int maxValue = 10;
        [Tooltip("Poser aussi des pièces sur la case de l'objet. Décocher pour un objet qui réapparaît sur place (punching-ball) : " +
                 "les pièces y seraient inaccessibles")]
        [SerializeField] private bool dropOnOwnCell = true;
        [Tooltip("Layer(s) des objets qui bloquent une case (cibles, ennemis, Destructibles) : pas de pièce dessous")]
        [SerializeField] private LayerMask solidObjectsLayer;

        private const int MaxCoinsPerCell = 3;
        // Distance maximale (en pas) des cases où tombent les pièces
        private const int MaxDropDistance = 2;

        // Positions des pièces dans une case selon leur nombre, en pixels depuis le centre de la case.
        // Les images (32×32) ont la pièce en bas : une grosse pièce occupe 14×18 px. À 3, deux en bas et une au-dessus
        // (elle dépasse de 3 px en haut de la case) : aucune ne chevauche une autre
        private static readonly Vector2Int[][] SlotPixels =
        {
            new[] { new Vector2Int(0, 6) },
            new[] { new Vector2Int(-7, 6), new Vector2Int(7, 6) },
            new[] { new Vector2Int(-8, -1), new Vector2Int(8, -1), new Vector2Int(0, 17) },
        };

        private static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        private Health health;

        private void Awake()
        {
            health = GetComponent<Health>();
        }

        private void OnEnable()
        {
            health.Died += OnDied;
        }

        private void OnDisable()
        {
            health.Died -= OnDied;
        }

        private void OnDied()
        {
            if (coinPrefab == null)
                return;

            List<int> coins = PickCoins(Random.Range(minValue, maxValue + 1));
            int level = HeightLevel.Of(this);
            List<Vector3Int> cells = FindDropCells(level);
            if (cells.Count == 0)
                return;

            // Au plus 2 pièces par case quand il y a de la place (plus joli), jusqu'à 3 sinon
            int cellCount = Mathf.Min(cells.Count, Mathf.CeilToInt(coins.Count / 2f));
            // Plus de pièces que de place (3 par case) : le surplus est perdu
            int placed = Mathf.Min(coins.Count, cellCount * MaxCoinsPerCell);

            // Répartition en tourniquet sur les cases retenues, puis placement selon le nombre de pièces de chaque case
            var perCell = new List<int>[cellCount];
            for (int i = 0; i < placed; i++)
            {
                int c = i % cellCount;
                if (perCell[c] == null)
                    perCell[c] = new List<int>(MaxCoinsPerCell);
                perCell[c].Add(coins[i]);
            }

            for (int c = 0; c < cellCount; c++)
            {
                if (perCell[c] == null)
                    continue;
                Vector2Int[] slots = SlotPixels[perCell[c].Count - 1];
                Vector3Int key = cells[c];
                for (int i = 0; i < perCell[c].Count; i++)
                {
                    var position = new Vector3(key.x + 0.5f + slots[i].x / 32f, key.y + 0.5f + slots[i].y / 32f, 0f);
                    ZeniCoin.Spawn(coinPrefab, perCell[c][i], position, key);
                }
            }
        }

        // Compose la somme avec des pièces tirées au hasard parmi celles qui ne la dépassent pas, dans un ordre mélangé
        private List<int> PickCoins(int total)
        {
            int[] values = coinPrefab.GetValues();
            var coins = new List<int>();
            var candidates = new List<int>(values.Length);
            int remaining = total;
            while (remaining > 0)
            {
                candidates.Clear();
                foreach (int v in values)
                {
                    if (v > 0 && v <= remaining)
                        candidates.Add(v);
                }
                if (candidates.Count == 0)
                    break;
                int value = candidates[Random.Range(0, candidates.Count)];
                coins.Add(value);
                remaining -= value;
            }
            return coins;
        }

        // Cases libres autour de l'objet, des plus proches aux plus lointaines (ordre mélangé à distance égale),
        // atteignables à pied depuis sa case : pas à travers un mur. Clé = (x, y, niveau)
        private List<Vector3Int> FindDropCells(int startLevel)
        {
            var start = new Vector3Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y), startLevel);
            var result = new List<Vector3Int>();
            var visited = new List<Vector3Int> { start };
            var frontier = new List<Vector3Int> { start };

            Vector2Int playerCell = new Vector2Int(int.MinValue, int.MinValue);
            Vector2Int playerTarget = playerCell;
            var playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null && playerObject.TryGetComponent(out PlayerController player))
            {
                playerCell = CellOf(player.transform.position);
                playerTarget = CellOf(player.OccupiedCell);
            }

            if (dropOnOwnCell)
                result.Add(start);

            for (int distance = 1; distance <= MaxDropDistance; distance++)
            {
                var next = new List<Vector3Int>();
                foreach (var node in frontier)
                {
                    foreach (var dir in Directions)
                    {
                        Vector3 from = new Vector3(node.x + 0.5f, node.y + 0.5f, 0f);
                        if (!CollisionMap.CanMoveOnLevel(from, (Vector2)dir, node.z))
                            continue;
                        var cellCenter = from + new Vector3(dir.x, dir.y, 0f);
                        int level = LevelMap.LevelAfterEntering(cellCenter, node.z);
                        var cell = new Vector3Int(node.x + dir.x, node.y + dir.y, level);
                        if (visited.Contains(cell))
                            continue;
                        visited.Add(cell);
                        next.Add(cell);
                    }
                }

                Shuffle(next);
                foreach (var cell in next)
                {
                    var flat = new Vector2Int(cell.x, cell.y);
                    // Pas sous Goku (il devrait repartir et revenir pour les ramasser), ni sous un objet qui bloque
                    if (flat == playerCell || flat == playerTarget || IsBlocked(cell))
                        continue;
                    result.Add(cell);
                }
                frontier = next;
            }
            return result;
        }

        private bool IsBlocked(Vector3Int cell)
        {
            var center = new Vector2(cell.x + 0.5f, cell.y + 0.5f);
            foreach (var hit in Physics2D.OverlapCircleAll(center, 0.3f, solidObjectsLayer))
            {
                if (!hit.transform.IsChildOf(transform) && HeightLevel.IsOnLevel(hit, cell.z))
                    return true;
            }
            return false;
        }

        private static void Shuffle(List<Vector3Int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static Vector2Int CellOf(Vector3 world)
        {
            return new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));
        }
    }
}
