using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DBZ.World
{
    // Porte qui s'ouvre quand le joueur arrive sur la case devant (celle où est posé l'objet, invisible en jeu).
    // Méthode par superposition : porte ouverte peinte sur un calque du dessous (ex. Decor 1), porte fermée juste
    // au-dessus sur un autre calque (ex. Decor 2). À l'ouverture, les tuiles de la porte fermée sont retirées
    // (la porte ouverte apparaît) ; elles sont remises quand le joueur n'est plus ni devant ni dans la porte.
    // Ne bloque pas le passage. Ni les ennemis ni le vol ne l'ouvrent. Changements en jeu seulement : la scène
    // n'est jamais modifiée (tout revient à l'arrêt du Play).
    public class TileSwitch : MonoBehaviour
    {
        private const string DefaultLayerName = "Decor 2";

        [Tooltip("Calque où est peinte la porte fermée. Vide : le calque nommé « Decor 2 »")]
        [SerializeField] private Tilemap closedLayer;
        [Tooltip("Cases de la porte, comptées depuis cet objet (x vers la droite, y vers le haut). " +
                 "(0, 1) = la case juste au-dessus")]
        [SerializeField] private Vector2Int[] doorCells = { new Vector2Int(0, 1) };

        // Interrupteurs de la scène : le joueur les prévient sans réglage dans l'Inspector
        private static readonly List<TileSwitch> activeSwitches = new List<TileSwitch>();

        // Tuiles retirées à l'ouverture, remises à la fermeture (une par case de la porte)
        private TileBase[] removedTiles;
        private bool isOpen;

        // Case de déclenchement (celle où est posé l'objet, même s'il n'est pas pile au centre)
        public Vector3Int Cell
        {
            get
            {
                var pos = transform.position;
                return new Vector3Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y), 0);
            }
        }

        private Vector3 CellCenter => new Vector3(Cell.x + 0.5f, Cell.y + 0.5f, 0f);

        private void Awake()
        {
            if (closedLayer == null)
            {
                foreach (var tilemap in FindObjectsByType<Tilemap>())
                {
                    if (tilemap.name == DefaultLayerName && tilemap.gameObject.scene == gameObject.scene)
                    {
                        closedLayer = tilemap;
                        break;
                    }
                }
                if (closedLayer == null)
                    Debug.LogWarning($"TileSwitch {name} : aucun calque choisi et pas de calque « {DefaultLayerName} », porte inactive.", this);
            }
        }

        private void OnEnable()
        {
            activeSwitches.Add(this);
        }

        private void OnDisable()
        {
            activeSwitches.Remove(this);
            if (isOpen)
                Close();
        }

        // Appelé par le joueur à chaque arrivée sur une case ; onGround faux en vol (la porte se referme)
        public static void PlayerArrivedAt(Vector3 worldPosition, bool onGround)
        {
            var cell = new Vector3Int(Mathf.FloorToInt(worldPosition.x), Mathf.FloorToInt(worldPosition.y), 0);
            foreach (var tileSwitch in activeSwitches)
            {
                bool inside = onGround && tileSwitch.Contains(cell);
                if (inside && !tileSwitch.isOpen)
                    tileSwitch.Open();
                else if (!inside && tileSwitch.isOpen)
                    tileSwitch.Close();
            }
        }

        // Case devant ou case de la porte : la porte reste ouverte tant que le joueur y est
        private bool Contains(Vector3Int cell)
        {
            if (cell == Cell)
                return true;
            foreach (var offset in doorCells)
            {
                if (cell == Cell + new Vector3Int(offset.x, offset.y, 0))
                    return true;
            }
            return false;
        }

        private Vector3Int LayerCell(Vector2Int offset)
        {
            return closedLayer.WorldToCell(CellCenter + new Vector3(offset.x, offset.y, 0f));
        }

        private void Open()
        {
            if (closedLayer == null)
                return;
            removedTiles = new TileBase[doorCells.Length];
            for (int i = 0; i < doorCells.Length; i++)
            {
                var cell = LayerCell(doorCells[i]);
                removedTiles[i] = closedLayer.GetTile(cell);
                closedLayer.SetTile(cell, null);
            }
            isOpen = true;
        }

        private void Close()
        {
            isOpen = false;
            if (closedLayer == null || removedTiles == null)
                return;
            for (int i = 0; i < doorCells.Length; i++)
                closedLayer.SetTile(LayerCell(doorCells[i]), removedTiles[i]);
        }

#if UNITY_EDITOR
        // Vue Scene seulement : case de déclenchement en vert, cases de la porte en orange, reliées par un trait
        private void OnDrawGizmos()
        {
            var trigger = new Color(0.2f, 1f, 0.3f);
            Gizmos.color = new Color(trigger.r, trigger.g, trigger.b, 0.35f);
            Gizmos.DrawCube(CellCenter, new Vector3(1f, 1f, 0f));
            Gizmos.color = trigger;
            Gizmos.DrawWireCube(CellCenter, new Vector3(1f, 1f, 0f));

            var door = new Color(1f, 0.6f, 0.1f);
            foreach (var offset in doorCells)
            {
                var center = CellCenter + new Vector3(offset.x, offset.y, 0f);
                Gizmos.color = new Color(door.r, door.g, door.b, 0.25f);
                Gizmos.DrawCube(center, new Vector3(1f, 1f, 0f));
                Gizmos.color = door;
                Gizmos.DrawWireCube(center, new Vector3(1f, 1f, 0f));
                Gizmos.DrawLine(CellCenter, center);
            }

            var style = new GUIStyle { fontStyle = FontStyle.Bold, fontSize = 11 };
            style.normal.textColor = door;
            string layer = closedLayer != null ? closedLayer.name : DefaultLayerName;
            UnityEditor.Handles.Label(CellCenter + new Vector3(-0.5f, -0.6f, 0f), "porte (" + layer + ")", style);
        }
#endif
    }
}
