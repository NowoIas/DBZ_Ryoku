using System.Collections.Generic;
using DBZ.Combat;
using DBZ.Enemies;
using DBZ.World;
using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DBZ.EditorTools
{
    // Menu DBZ > Déplacer une zone : déplace un rectangle de la carte sur TOUS les calques de la Grid à la fois
    // (la Tile Palette ne déplace que l'Active Tilemap). Zone = sélection de la Tile Palette (outil Select, sur
    // n'importe quel calque). Départ laissé vide (sol compris, choix util.) ; arrivée entièrement remplacée.
    // Portes, TileSwitch, punching-balls et ennemis posés dans la zone suivent. Un seul Ctrl+Z annule tout.
    // Éditeur seulement (dossier Editor).
    public class ZoneMover : EditorWindow
    {
        private Grid grid;
        private BoundsInt zone;
        private bool hasZone;
        private Vector2Int offset;

        // Contenu d'une case d'un calque, gardé à part : la zone d'arrivée peut chevaucher celle de départ
        private struct CellCopy
        {
            public Vector3Int position;
            public TileBase tile;
            public Matrix4x4 matrix;
            public Color color;
        }

        [MenuItem("DBZ/Déplacer une zone (tous les calques)")]
        private static void Open()
        {
            var window = GetWindow<ZoneMover>("Déplacer une zone");
            window.minSize = new Vector2(300, 190);
            window.TakeSelection();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("1. Tile Palette, outil Select (S) : tirer un cadre sur la zone, sur n'importe quel calque.\n" +
                                    "2. « Prendre la sélection ».\n3. Décalage ou case d'arrivée, puis « Déplacer ».",
                                    MessageType.None);
            if (GUILayout.Button("Prendre la sélection"))
                TakeSelection();

            if (!hasZone)
            {
                EditorGUILayout.LabelField("Aucune zone.");
                return;
            }

            EditorGUILayout.LabelField($"Zone : ({zone.xMin}, {zone.yMin}) → ({zone.xMax - 1}, {zone.yMax - 1}), " +
                                       $"{zone.size.x}×{zone.size.y} cases");
            offset = EditorGUILayout.Vector2IntField("Décalage (cases)", offset);
            // Case d'arrivée = coin bas-gauche de la zone une fois déplacée ; liée au décalage
            var origin = new Vector2Int(zone.xMin, zone.yMin);
            var destination = EditorGUILayout.Vector2IntField("Arrivée (coin bas-gauche)", origin + offset);
            offset = destination - origin;

            using (new EditorGUI.DisabledScope(offset == Vector2Int.zero || EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Déplacer"))
                    Move();
            }
            if (EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("Impossible pendant le Play.", MessageType.Warning);
        }

        private void TakeSelection()
        {
            if (!GridSelection.active || GridSelection.grid == null)
            {
                hasZone = false;
                Repaint();
                return;
            }
            grid = GridSelection.grid.GetComponent<Grid>();
            zone = GridSelection.position;
            // Une seule épaisseur en z : les cartes sont toutes en z = 0
            zone.zMin = 0;
            zone.zMax = 1;
            hasZone = grid != null && zone.size.x > 0 && zone.size.y > 0;
            Repaint();
        }

        private void Move()
        {
            if (grid == null)
            {
                Debug.LogWarning("Déplacer une zone : Grid introuvable, reprends la sélection.");
                return;
            }
            var shift = new Vector3Int(offset.x, offset.y, 0);
            var target = new BoundsInt(zone.position + shift, zone.size);
            var tilemaps = grid.GetComponentsInChildren<Tilemap>(true);
            var objects = ObjectsIn(zone);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Déplacer une zone");
            Undo.RegisterCompleteObjectUndo(tilemaps, "Déplacer une zone");

            int movedTiles = 0;
            foreach (var tilemap in tilemaps)
            {
                // 1. copie, 2. départ vidé, 3. arrivée vidée, 4. copie posée : marche aussi si les zones se chevauchent
                var copies = new List<CellCopy>();
                foreach (var position in zone.allPositionsWithin)
                {
                    var tile = tilemap.GetTile(position);
                    if (tile == null)
                        continue;
                    copies.Add(new CellCopy
                    {
                        position = position + shift,
                        tile = tile,
                        matrix = tilemap.GetTransformMatrix(position),
                        color = tilemap.GetColor(position)
                    });
                }
                ClearArea(tilemap, zone);
                ClearArea(tilemap, target);
                if (copies.Count == 0)
                    continue;
                var changes = new TileChangeData[copies.Count];
                for (int i = 0; i < copies.Count; i++)
                    changes[i] = new TileChangeData(copies[i].position, copies[i].tile, copies[i].color, copies[i].matrix);
                tilemap.SetTiles(changes, true);
                movedTiles += copies.Count;
            }

            foreach (var obj in objects)
            {
                Undo.RecordObject(obj.transform, "Déplacer une zone");
                obj.transform.position += grid.CellToWorld(shift) - grid.CellToWorld(Vector3Int.zero);
            }
            Undo.CollapseUndoOperations(group);

            Debug.Log($"Déplacer une zone : {movedTiles} tuile(s) sur {tilemaps.Length} calques et {objects.Count} objet(s) " +
                      $"déplacés de ({offset.x}, {offset.y}). Ctrl+Z pour annuler, Ctrl+S pour sauvegarder la scène.");

            // La sélection suit la zone : un 2e déplacement repart de la nouvelle position
            zone = target;
            offset = Vector2Int.zero;
            GridSelection.Select(GridSelection.target != null ? GridSelection.target : tilemaps[0].gameObject, target);
            Repaint();
        }

        private static void ClearArea(Tilemap tilemap, BoundsInt area)
        {
            var empty = new TileBase[area.size.x * area.size.y];
            tilemap.SetTilesBlock(area, empty);
        }

        // Objets de la scène posés dans la zone (case de leur position) qui doivent suivre les tuiles
        private List<GameObject> ObjectsIn(BoundsInt area)
        {
            var found = new List<GameObject>();
            AddObjects(FindObjectsByType<Door>(FindObjectsInactive.Include), area, found);
            AddObjects(FindObjectsByType<TileSwitch>(FindObjectsInactive.Include), area, found);
            AddObjects(FindObjectsByType<TrainingDummy>(FindObjectsInactive.Include), area, found);
            AddObjects(FindObjectsByType<EnemyController>(FindObjectsInactive.Include), area, found);
            return found;
        }

        private void AddObjects<T>(T[] components, BoundsInt area, List<GameObject> found) where T : Component
        {
            foreach (var component in components)
            {
                // Objets de la scène seulement (pas les prefabs du Project)
                if (EditorUtility.IsPersistent(component.gameObject) || component.gameObject.scene != grid.gameObject.scene)
                    continue;
                var cell = grid.WorldToCell(component.transform.position);
                cell.z = 0;
                if (area.Contains(cell) && !found.Contains(component.gameObject))
                    found.Add(component.gameObject);
            }
        }
    }
}
