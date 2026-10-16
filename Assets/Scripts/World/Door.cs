using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DBZ.World
{
    // Porte vers une scène (ou un autre endroit de la même scène), posée sur une case de la carte (invisible en jeu).
    // Quand le joueur arrive sur sa case en marchant, il passe par la porte d'arrivée : dans la scène cible,
    // la porte qui ramène à la scène de départ avec le même linkName. Il ressort sur la case juste après.
    public class Door : MonoBehaviour
    {
        public enum ExitDirection { Auto, Up, Down, Left, Right }

        [Tooltip("Nom de la scène où mène cette porte (elle doit être dans la liste des scènes du build)")]
        [SerializeField] private string targetScene;
        [Tooltip("Facultatif : même nom aux deux bouts d'un passage, quand plusieurs portes relient les deux mêmes scènes " +
                 "(grotte à plusieurs sorties...). Vide : la porte d'arrivée est celle qui ramène à la scène de départ")]
        [SerializeField] private string linkName;
        [Tooltip("Côté par lequel on ressort quand on arrive par cette porte. Auto : dans le sens où l'on marchait en entrant")]
        [SerializeField] private ExitDirection exitDirection = ExitDirection.Auto;

        // Portes de la scène : le joueur les consulte sans réglage dans l'Inspector
        private static readonly List<Door> activeDoors = new List<Door>();

        public string TargetScene => targetScene;
        public string LinkName => linkName ?? "";

        // Case de la porte (celle où est posé l'objet, même s'il n'est pas pile au centre)
        public Vector3Int Cell
        {
            get
            {
                var pos = transform.position;
                return new Vector3Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y), 0);
            }
        }

        public Vector3 CellCenter => new Vector3(Cell.x + 0.5f, Cell.y + 0.5f, 0f);

        private void OnEnable()
        {
            activeDoors.Add(this);
        }

        private void OnDisable()
        {
            activeDoors.Remove(this);
        }

        // Porte posée sur la case de cette position, ou null
        public static Door FindAt(Vector3 worldPosition)
        {
            var cell = new Vector3Int(Mathf.FloorToInt(worldPosition.x), Mathf.FloorToInt(worldPosition.y), 0);
            foreach (var door in activeDoors)
            {
                if (door.Cell == cell)
                    return door;
            }
            return null;
        }

        // Porte d'arrivée : celle qui ramène à la scène de départ, avec le même linkName.
        // Dans la même scène, on exclut la porte prise pour ressortir par l'autre bout
        public static Door FindArrival(SceneTransition.Arrival arrival)
        {
            bool sameScene = SceneTransition.IsSameScene(arrival.FromScene);
            foreach (var door in activeDoors)
            {
                if (sameScene && door.Cell == arrival.EntryCell)
                    continue;
                if (string.Equals(door.targetScene, arrival.FromScene, System.StringComparison.OrdinalIgnoreCase)
                    && door.LinkName == (arrival.LinkName ?? ""))
                    return door;
            }
            return null;
        }

        // Sens dans lequel on ressort de cette porte : le sien s'il est forcé, sinon celui de la marche
        public Vector2 ExitVector(Vector2 walkingDirection)
        {
            switch (exitDirection)
            {
                case ExitDirection.Up: return Vector2.up;
                case ExitDirection.Down: return Vector2.down;
                case ExitDirection.Left: return Vector2.left;
                case ExitDirection.Right: return Vector2.right;
                default: return walkingDirection;
            }
        }

        // Couleur tirée de la destination : chaque porte a la sienne dans la vue Scene, sans réglage
        private Color EditorColor
        {
            get
            {
                unchecked
                {
                    // string.GetHashCode change d'une session à l'autre : on calcule le nôtre pour garder les mêmes couleurs
                    int hash = 23;
                    foreach (char c in (targetScene ?? "") + "/" + LinkName)
                        hash = hash * 31 + c;
                    return Color.HSVToRGB(Mathf.Abs(hash % 1000) / 1000f, 0.75f, 1f);
                }
            }
        }

#if UNITY_EDITOR
        // Vue Scene seulement : case de la porte en couleur, flèche si la sortie est forcée, destination écrite au-dessus
        private void OnDrawGizmos()
        {
            var color = EditorColor;
            Gizmos.color = new Color(color.r, color.g, color.b, 0.35f);
            Gizmos.DrawCube(CellCenter, new Vector3(1f, 1f, 0f));
            Gizmos.color = color;
            Gizmos.DrawWireCube(CellCenter, new Vector3(1f, 1f, 0f));

            if (exitDirection != ExitDirection.Auto)
            {
                Vector3 exit = ExitVector(Vector2.zero);
                Vector3 side = new Vector3(-exit.y, exit.x, 0f);
                Vector3 tip = CellCenter + exit * 0.9f;
                Gizmos.DrawLine(CellCenter, tip);
                Gizmos.DrawLine(tip, tip - exit * 0.25f + side * 0.2f);
                Gizmos.DrawLine(tip, tip - exit * 0.25f - side * 0.2f);
            }

            var style = new GUIStyle { fontStyle = FontStyle.Bold, fontSize = 11 };
            style.normal.textColor = color;
            string label = "→ " + targetScene + (LinkName != "" ? " (" + LinkName + ")" : "");
            UnityEditor.Handles.Label(CellCenter + new Vector3(-0.5f, 0.9f, 0f), label, style);
        }
#endif
    }
}
