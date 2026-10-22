using System.Collections.Generic;
using System.IO;
using DBZ.World;
using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DBZ.EditorTools
{
    // Menu DBZ > Créer une tuile animée (Ctrl+Alt+A) : les tuiles sélectionnées dans la Tile Palette
    // (cadre tiré à la souris, ex. les 2 images d'un feu dans Tiles1Palette) deviennent une tuile animée,
    // rangée dans Tiles/Animated/ et ajoutée à AnimatedPalette. Ordre des images : de gauche à droite,
    // de haut en bas. Éditeur seulement (dossier Editor) : rien de tout ça n'est dans le jeu.
    public static class AnimatedTileCreator
    {
        private const string TileFolder = "Assets/Tiles/Animated";
        private const string PalettePath = "Assets/Tiles/AnimatedPalette.prefab";
        private const string SourcePalettePath = "Assets/Tiles/Tiles1Palette.prefab";
        // Vitesse = images par seconde (animationFrameRate des tilemaps à 1 par défaut) : 2 = 2 alternances/s
        private const float Speed = 2f;
        // Largeur de AnimatedPalette : au-delà, les tuiles passent à la rangée suivante
        private const int PaletteColumns = 8;

        [MenuItem("DBZ/Créer une tuile animée %&a")]
        private static void CreateFromSelection()
        {
            var sprites = SelectedSprites();
            if (sprites.Count < 2)
            {
                Debug.LogWarning("Tuile animée : sélectionne au moins 2 tuiles dans la Tile Palette (cadre à la souris), puis relance.");
                return;
            }

            var existing = FindExisting(sprites);
            if (existing != null)
            {
                Debug.Log($"Tuile animée : existe déjà ({AssetDatabase.GetAssetPath(existing)}).", existing);
                return;
            }

            if (!AssetDatabase.IsValidFolder(TileFolder))
                AssetDatabase.CreateFolder("Assets/Tiles", "Animated");
            var tile = ScriptableObject.CreateInstance<RandomStartAnimatedTile>();
            tile.m_AnimatedSprites = sprites.ToArray();
            tile.m_MinSpeed = Speed;
            tile.m_MaxSpeed = Speed;
            string path = AssetDatabase.GenerateUniqueAssetPath($"{TileFolder}/Anim_{sprites[0].name}.asset");
            AssetDatabase.CreateAsset(tile, path);
            AssetDatabase.SaveAssets();

            Vector3Int cell = AddToPalette(tile);
            Debug.Log($"Tuile animée créée : {path} ({sprites.Count} images : {string.Join(", ", sprites.ConvertAll(s => s.name))}), " +
                      $"case ({cell.x}, {cell.y}) de AnimatedPalette.", tile);
        }

        // Images des tuiles prises dans la palette (le pinceau courant), de gauche à droite puis de haut en bas
        private static List<Sprite> SelectedSprites()
        {
            var sprites = new List<Sprite>();
            if (!(GridPaintingState.gridBrush is GridBrush brush))
                return sprites;
            Vector3Int size = brush.size;
            for (int y = size.y - 1; y >= 0; y--)
            {
                for (int x = 0; x < size.x; x++)
                {
                    var cell = brush.cells[brush.GetCellIndex(x, y, 0)];
                    if (cell.tile is Tile t && t.sprite != null)
                        sprites.Add(t.sprite);
                }
            }
            return sprites;
        }

        // Tuile animée déjà faite avec exactement les mêmes images, pour ne pas créer de doublon
        private static RandomStartAnimatedTile FindExisting(List<Sprite> sprites)
        {
            if (!AssetDatabase.IsValidFolder(TileFolder))
                return null;
            foreach (string guid in AssetDatabase.FindAssets("t:RandomStartAnimatedTile", new[] { TileFolder }))
            {
                var other = AssetDatabase.LoadAssetAtPath<RandomStartAnimatedTile>(AssetDatabase.GUIDToAssetPath(guid));
                if (other == null || other.m_AnimatedSprites == null || other.m_AnimatedSprites.Length != sprites.Count)
                    continue;
                bool same = true;
                for (int i = 0; i < sprites.Count && same; i++)
                    same = other.m_AnimatedSprites[i] == sprites[i];
                if (same)
                    return other;
            }
            return null;
        }

        // Pose la tuile sur la 1re case libre de AnimatedPalette (créée au besoin, copie vidée de Tiles1Palette
        // pour garder les mêmes réglages de grille)
        private static Vector3Int AddToPalette(TileBase tile)
        {
            if (!File.Exists(PalettePath))
            {
                AssetDatabase.CopyAsset(SourcePalettePath, PalettePath);
                var empty = PrefabUtility.LoadPrefabContents(PalettePath);
                foreach (var map in empty.GetComponentsInChildren<Tilemap>())
                    map.ClearAllTiles();
                PrefabUtility.SaveAsPrefabAsset(empty, PalettePath);
                PrefabUtility.UnloadPrefabContents(empty);
            }

            var root = PrefabUtility.LoadPrefabContents(PalettePath);
            var tilemap = root.GetComponentInChildren<Tilemap>();
            Vector3Int cell = Vector3Int.zero;
            for (int i = 0; ; i++)
            {
                cell = new Vector3Int(i % PaletteColumns, -(i / PaletteColumns), 0);
                if (tilemap.GetTile(cell) == null)
                    break;
            }
            tilemap.SetTile(cell, tile);
            PrefabUtility.SaveAsPrefabAsset(root, PalettePath);
            PrefabUtility.UnloadPrefabContents(root);
            return cell;
        }
    }
}
