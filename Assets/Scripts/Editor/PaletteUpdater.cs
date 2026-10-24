using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DBZ.EditorTools
{
    // Menu DBZ > Mettre à jour la palette (ou clic droit > DBZ > Mettre à jour la palette), sur la planche,
    // une de ses images, une de ses tuiles ou sa palette :
    // après avoir dessiné dans une case qui était vide au découpage, ajoute le sprite de cette case, sa tuile
    // et sa place dans la palette. Les sprites existants ne sont pas touchés (mêmes noms, mêmes spriteID) :
    // les tuiles déjà peintes ne bougent pas. Une case vidée sur la planche n'est jamais supprimée.
    // Éditeur seulement (dossier Editor).
    public static class PaletteUpdater
    {
        private const int CellSize = 32;
        private const string PalettesFolder = "Assets/Tiles";

        [MenuItem("DBZ/Mettre à jour la palette")]
        [MenuItem("Assets/DBZ/Mettre à jour la palette")]
        private static void UpdateSelected()
        {
            // Planche (.png), image découpée (même fichier), tuile ou palette : on remonte aux planches utilisées
            // (une palette coupée en morceaux, ex. T8Palette, donne T8a à T8d)
            var sheets = new List<string>();
            foreach (var o in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                    continue;
                var candidates = IsPng(path) ? new[] { path } : AssetDatabase.GetDependencies(path, true);
                foreach (string candidate in candidates)
                    if (IsPng(candidate) && !sheets.Contains(candidate))
                        sheets.Add(candidate);
            }
            if (sheets.Count == 0)
            {
                Debug.LogWarning("Mise à jour de palette : sélectionne dans la fenêtre Project la planche (.png), " +
                                 "une de ses images, une de ses tuiles ou sa palette, puis relance.");
                return;
            }
            foreach (string sheet in sheets)
                UpdateSheet(sheet);
        }

        private static void UpdateSheet(string texturePath)
        {
            string sheet = Path.GetFileNameWithoutExtension(texturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var rects = new List<SpriteRect>(provider.GetSpriteRects());
            if (rects.Count == 0)
            {
                Debug.LogWarning($"Mise à jour de palette : {sheet} n'est pas découpée, rien à compléter.");
                return;
            }

            // Cases dessinées sans sprite (lecture du .png lui-même : à jour même avant le réimport)
            var taken = new HashSet<Vector2Int>();
            int nextNumber = 0;
            foreach (var r in rects)
            {
                taken.Add(new Vector2Int((int)r.rect.x / CellSize, (int)r.rect.y / CellSize));
                int underscore = r.name.LastIndexOf('_');
                if (underscore >= 0 && int.TryParse(r.name.Substring(underscore + 1), out int n) && n >= nextNumber)
                    nextNumber = n + 1;
            }
            var image = new Texture2D(2, 2);
            image.LoadImage(File.ReadAllBytes(texturePath));
            int width = image.width, height = image.height;
            var pixels = image.GetPixels32();
            Object.DestroyImmediate(image);
            int rows = height / CellSize;

            // Ordre de la planche : de haut en bas, de gauche à droite (comme le découpage d'origine)
            var added = new List<SpriteRect>();
            var reference = rects[0];
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < width / CellSize; col++)
                {
                    int y0 = (rows - 1 - row) * CellSize + (height - rows * CellSize);
                    var cell = new Vector2Int(col, y0 / CellSize);
                    if (taken.Contains(cell) || !HasPixels(pixels, width, col * CellSize, y0))
                        continue;
                    added.Add(new SpriteRect
                    {
                        name = $"{sheet}_{nextNumber++}",
                        rect = new Rect(col * CellSize, y0, CellSize, CellSize),
                        alignment = reference.alignment,
                        pivot = reference.pivot,
                        spriteID = GUID.Generate()
                    });
                }
            }
            if (added.Count == 0)
            {
                Debug.Log($"Mise à jour de palette : {sheet} est déjà complète (aucune nouvelle case dessinée).");
                return;
            }

            // Où ranger : dossier et palette des tuiles existantes de cette planche (trouvés par une tuile de référence)
            Tile referenceTile = FindTileOf(reference.name, texturePath);
            if (referenceTile == null)
            {
                Debug.LogWarning($"Mise à jour de palette : aucune tuile trouvée pour {reference.name}, impossible de savoir où ranger les nouvelles.");
                return;
            }
            string tileFolder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(referenceTile)).Replace('\\', '/');

            rects.AddRange(added);
            provider.SetSpriteRects(rects.ToArray());
            var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            var pairs = new List<SpriteNameFileIdPair>(names.GetNameFileIdPairs());
            foreach (var r in added)
                pairs.Add(new SpriteNameFileIdPair(r.name, r.spriteID));
            names.SetNameFileIdPairs(pairs);
            provider.Apply();
            importer.SaveAndReimport();

            var sprites = new Dictionary<string, Sprite>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(texturePath))
                if (o is Sprite s)
                    sprites[s.name] = s;
            var newTiles = new List<KeyValuePair<SpriteRect, Tile>>();
            foreach (var r in added)
            {
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprites[r.name];
                tile.colliderType = referenceTile.colliderType;
                tile.flags = referenceTile.flags;
                tile.color = referenceTile.color;
                AssetDatabase.CreateAsset(tile, $"{tileFolder}/{r.name}.asset");
                newTiles.Add(new KeyValuePair<SpriteRect, Tile>(r, tile));
            }
            AssetDatabase.SaveAssets();

            string placed = PlaceInPalette(referenceTile, reference, height, newTiles);
            Debug.Log($"Mise à jour de palette : {sheet}, {added.Count} case(s) ajoutée(s) " +
                      $"({string.Join(", ", added.ConvertAll(r => r.name))}), tuiles dans {tileFolder}. {placed}");
        }

        private static bool IsPng(string path)
        {
            return path.StartsWith("Assets/") && path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasPixels(Color32[] pixels, int width, int x0, int y0)
        {
            for (int y = y0; y < y0 + CellSize; y++)
                for (int x = x0; x < x0 + CellSize; x++)
                    if (pixels[y * width + x].a > 0)
                        return true;
            return false;
        }

        // Tuile dont le sprite est celui demandé (les tuiles portent le nom de leur sprite)
        private static Tile FindTileOf(string spriteName, string texturePath)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{spriteName} t:Tile"))
            {
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(AssetDatabase.GUIDToAssetPath(guid));
                if (tile != null && tile.sprite != null && tile.sprite.name == spriteName &&
                    AssetDatabase.GetAssetPath(tile.sprite) == texturePath)
                    return tile;
            }
            return null;
        }

        // Palette qui contient la tuile de référence : le décalage entre sa case de palette et sa case de planche
        // donne la place des nouvelles (vaut aussi pour les morceaux T8b... empilés sous T8a)
        private static string PlaceInPalette(Tile referenceTile, SpriteRect reference, int sheetHeight,
                                             List<KeyValuePair<SpriteRect, Tile>> newTiles)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:GridPalette", new[] { PalettesFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                var tilemap = root.GetComponentInChildren<Tilemap>();
                if (tilemap == null || !tilemap.ContainsTile(referenceTile))
                {
                    PrefabUtility.UnloadPrefabContents(root);
                    continue;
                }
                tilemap.CompressBounds();
                Vector3Int refCell = Vector3Int.zero;
                foreach (var p in tilemap.cellBounds.allPositionsWithin)
                    if (tilemap.GetTile(p) == referenceTile)
                    {
                        refCell = p;
                        break;
                    }
                Vector2Int refSheet = SheetCell(reference.rect, sheetHeight);
                int skipped = 0;
                foreach (var pair in newTiles)
                {
                    Vector2Int c = SheetCell(pair.Key.rect, sheetHeight);
                    var cell = new Vector3Int(refCell.x + c.x - refSheet.x, refCell.y - (c.y - refSheet.y), 0);
                    if (tilemap.GetTile(cell) != null)
                    {
                        skipped++;
                        continue;
                    }
                    tilemap.SetTile(cell, pair.Value);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                return $"Ajoutée(s) à {Path.GetFileNameWithoutExtension(path)}" +
                       (skipped > 0 ? $" ; {skipped} non posée(s) : case de palette déjà occupée." : ".");
            }
            return "Aucune palette ne contient cette planche : tuiles créées mais pas posées.";
        }

        // (colonne, rangée depuis le haut) d'une case de la planche
        private static Vector2Int SheetCell(Rect rect, int sheetHeight)
        {
            return new Vector2Int((int)rect.x / CellSize, (sheetHeight - (int)rect.y) / CellSize - 1);
        }
    }
}
