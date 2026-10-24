using System.Collections.Generic;
using System.IO;
using DBZ.World;
using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace DBZ.EditorTools
{
    // Menu DBZ > Créer une tuile animée (Ctrl+Alt+A) : les tuiles sélectionnées dans la Tile Palette
    // (cadre tiré à la souris, ex. les 2 images d'un feu dans Tiles1Palette) deviennent une tuile animée,
    // rangée dans Tiles/Animated/ et ajoutée à AnimatedPalette. Ordre des images : de gauche à droite,
    // de haut en bas.
    // Images éloignées ou objets de plusieurs cases : ajouter chaque image une par une (Ctrl+Alt+Z), un bloc
    // sélectionné = une image entière (même taille pour toutes), puis créer : une tuile animée par case du bloc,
    // posées dans la même disposition. Suppression : DBZ > Tuile animée : supprimer (Ctrl+Alt+D).
    // Éditeur seulement (dossier Editor) : rien de tout ça n'est dans le jeu.
    public static class AnimatedTileCreator
    {
        private const string TileFolder = "Assets/Tiles/Animated";
        private const string PalettePath = "Assets/Tiles/AnimatedPalette.prefab";
        private const string SourcePalettePath = "Assets/Tiles/Tiles1Palette.prefab";
        // Vitesse = images par seconde (animationFrameRate des tilemaps à 1 par défaut) : 2 = 2 alternances/s
        private const float Speed = 2f;
        // Largeur de AnimatedPalette : au-delà, les tuiles passent à la rangée suivante
        private const int PaletteColumns = 8;
        // Images en attente, gardées jusqu'à la fermeture d'Unity : SessionState survit aux recompilations
        // et au Play, contrairement à un champ statique
        private const string PendingKey = "DBZ.AnimatedTileCreator.Pending";

        // Une image d'un objet : bloc de cases, sprites rangés depuis le bas à gauche (index y * largeur + x),
        // null = case vide sur cette image
        private class Frame
        {
            public Vector2Int size;
            public Sprite[] sprites;
        }

        [MenuItem("DBZ/Tuile animée : ajouter l'image %&z")]
        private static void AddToPending()
        {
            var frame = SelectedFrame();
            if (frame == null)
            {
                Debug.LogWarning("Tuile animée : sélectionne une tuile ou un bloc (palette, ou pipette I dans la scène), puis relance.");
                return;
            }
            var pending = LoadPending();
            if (pending.Count > 0 && pending[0].size != frame.size)
            {
                Debug.LogWarning($"Tuile animée : image refusée, bloc de {frame.size.x}×{frame.size.y} alors que les images en attente " +
                                 $"font {pending[0].size.x}×{pending[0].size.y}. Sélectionne un bloc de même taille, ou vide la liste (Ctrl+Alt+X).");
                return;
            }
            // Même bloc deux fois de suite : souvent un double appui (vécu : 2×2 à 3 images A, A, B, désynchronisé
            // des tuiles à 2 images) ; ajouté quand même, une image peut volontairement durer plus longtemps
            if (pending.Count > 0 && SameSprites(pending[pending.Count - 1].sprites, frame.sprites))
                Debug.LogWarning("Tuile animée : image identique à la précédente, ajoutée quand même. " +
                                 "Ctrl+Alt+X pour recommencer si c'est une erreur.");
            pending.Add(frame);
            SavePending(pending);
            Debug.Log($"Tuile animée : {pending.Count} image(s) de {frame.size.x}×{frame.size.y} en attente " +
                      $"(dernière : {FirstName(frame.sprites)}). Ctrl+Alt+A pour créer, Ctrl+Alt+X pour vider.");
        }

        [MenuItem("DBZ/Tuile animée : vider la liste %&x")]
        private static void ClearPending()
        {
            SessionState.EraseString(PendingKey);
            Debug.Log("Tuile animée : liste en attente vidée.");
        }

        [MenuItem("DBZ/Créer une tuile animée %&a")]
        private static void Create()
        {
            // Liste en attente prioritaire ; vide = cadre sélectionné dans la palette, comme avant
            var pending = LoadPending();
            if (pending.Count > 0)
            {
                CreateFromPending(pending);
                return;
            }

            var sprites = SelectedSprites();
            if (sprites.Count < 2)
            {
                Debug.LogWarning("Tuile animée : sélectionne au moins 2 tuiles dans la Tile Palette (cadre à la souris), " +
                                 "ou ajoute les images une par une (Ctrl+Alt+Z), puis relance.");
                return;
            }
            var existing = FindExisting(sprites.ToArray(), Vector2Int.zero);
            if (existing != null)
            {
                Debug.Log($"Tuile animée : existe déjà ({AssetDatabase.GetAssetPath(existing)}).", existing);
                return;
            }
            var tile = CreateTile(sprites.ToArray(), Vector2Int.zero);
            Vector3Int cell = AddToPalette(new[] { tile }, Vector2Int.one);
            Debug.Log($"Tuile animée créée : {AssetDatabase.GetAssetPath(tile)} ({sprites.Count} images : " +
                      $"{string.Join(", ", sprites.ConvertAll(s => s.name))}), case ({cell.x}, {cell.y}) de AnimatedPalette.", tile);
        }

        // Une tuile animée par case du bloc ; ses images = la même case de chaque image en attente
        private static void CreateFromPending(List<Frame> pending)
        {
            if (pending.Count < 2)
            {
                Debug.LogWarning("Tuile animée : 1 seule image en attente, ajoute-en au moins une autre (Ctrl+Alt+Z), puis relance.");
                return;
            }
            Vector2Int size = pending[0].size;
            var cells = new Sprite[size.x * size.y][];
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = new Sprite[pending.Count];
                for (int f = 0; f < pending.Count; f++)
                    cells[i][f] = pending[f].sprites[i];
            }

            // Doublon : toutes les cases déjà faites à l'identique = objet déjà créé
            bool allExist = true;
            for (int i = 0; i < cells.Length && allExist; i++)
                allExist = IsEmpty(cells[i]) || FindExisting(cells[i], new Vector2Int(i % size.x, i / size.x)) != null;
            if (allExist)
            {
                SessionState.EraseString(PendingKey);
                Debug.Log("Tuile animée : existe déjà (mêmes images, même disposition). Liste vidée.");
                return;
            }

            var tiles = new TileBase[cells.Length];
            var names = new List<string>();
            for (int i = 0; i < cells.Length; i++)
            {
                // Case vide sur toutes les images : rien à animer, laissée vide dans la palette
                if (IsEmpty(cells[i]))
                    continue;
                tiles[i] = CreateTile(cells[i], new Vector2Int(i % size.x, i / size.x));
                names.Add(Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(tiles[i])));
            }
            Vector3Int corner = AddToPalette(tiles, size);
            SessionState.EraseString(PendingKey);
            Debug.Log($"Tuile animée créée : {names.Count} tuile(s) pour un objet de {size.x}×{size.y}, {pending.Count} images " +
                      $"({string.Join(", ", names)}), coin haut-gauche case ({corner.x}, {corner.y}) de AnimatedPalette. Liste vidée.");
        }

        [MenuItem("DBZ/Tuile animée : supprimer %&d")]
        private static void Delete()
        {
            var targets = new List<RandomStartAnimatedTile>();
            var brush = GridPaintingState.gridBrush as GridBrush;
            if (brush != null)
                foreach (var cell in brush.cells)
                    if (cell.tile is RandomStartAnimatedTile t && !targets.Contains(t))
                        targets.Add(t);
            if (targets.Count == 0)
            {
                Debug.LogWarning("Tuile animée : sélectionne dans AnimatedPalette la ou les tuiles animées à supprimer, puis relance.");
                return;
            }

            // Scènes ouvertes seulement : une scène fermée n'est pas vérifiée
            int uses = CountUsesInOpenScenes(targets);
            string message = $"Supprimer {targets.Count} tuile(s) animée(s) ?\n\n" + string.Join("\n", targets.ConvertAll(t => t.name));
            if (uses > 0)
                message += $"\n\nAttention : peinte(s) {uses} fois dans les scènes ouvertes. Ces cases deviendront vides.";
            message += "\n\n(Les scènes fermées ne sont pas vérifiées.)";
            if (!EditorUtility.DisplayDialog("Supprimer des tuiles animées", message, "Supprimer", "Annuler"))
                return;

            RemoveFromPalette(targets);
            brush.Init(Vector3Int.one);
            var paths = targets.ConvertAll(t => AssetDatabase.GetAssetPath(t));
            var failed = new List<string>();
            AssetDatabase.DeleteAssets(paths.ToArray(), failed);
            if (failed.Count > 0)
                Debug.LogWarning("Tuile animée : suppression impossible pour " + string.Join(", ", failed));
            Debug.Log($"Tuile animée : {paths.Count - failed.Count} tuile(s) supprimée(s) ({string.Join(", ", paths)}).");
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

        // Pinceau courant vu comme une image d'objet (bloc entier, cases vides comprises)
        private static Frame SelectedFrame()
        {
            if (!(GridPaintingState.gridBrush is GridBrush brush))
                return null;
            var size = new Vector2Int(brush.size.x, brush.size.y);
            var frame = new Frame { size = size, sprites = new Sprite[size.x * size.y] };
            bool any = false;
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    var cell = brush.cells[brush.GetCellIndex(x, y, 0)];
                    if (cell.tile is Tile t && t.sprite != null)
                    {
                        frame.sprites[y * size.x + x] = t.sprite;
                        any = true;
                    }
                }
            }
            return any ? frame : null;
        }

        // Format : images séparées par « | », chacune « largeur,hauteur: » puis les sprites (GlobalObjectId)
        // séparés par « ; », vide = case vide
        private static List<Frame> LoadPending()
        {
            var frames = new List<Frame>();
            string data = SessionState.GetString(PendingKey, "");
            if (data.Length == 0)
                return frames;
            foreach (string part in data.Split('|'))
            {
                int colon = part.IndexOf(':');
                if (colon < 0)
                    continue;
                string[] dims = part.Substring(0, colon).Split(',');
                string[] ids = part.Substring(colon + 1).Split(';');
                if (dims.Length != 2 || !int.TryParse(dims[0], out int w) || !int.TryParse(dims[1], out int h) || ids.Length != w * h)
                    continue;
                var frame = new Frame { size = new Vector2Int(w, h), sprites = new Sprite[w * h] };
                for (int i = 0; i < ids.Length; i++)
                    if (GlobalObjectId.TryParse(ids[i], out var gid))
                        frame.sprites[i] = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) as Sprite;
                frames.Add(frame);
            }
            return frames;
        }

        private static void SavePending(List<Frame> frames)
        {
            var parts = frames.ConvertAll(f =>
            {
                var ids = new string[f.sprites.Length];
                for (int i = 0; i < ids.Length; i++)
                    ids[i] = f.sprites[i] != null ? GlobalObjectId.GetGlobalObjectIdSlow(f.sprites[i]).ToString() : "";
                return $"{f.size.x},{f.size.y}:{string.Join(";", ids)}";
            });
            SessionState.SetString(PendingKey, string.Join("|", parts));
        }

        private static RandomStartAnimatedTile CreateTile(Sprite[] sprites, Vector2Int blockCell)
        {
            if (!AssetDatabase.IsValidFolder(TileFolder))
                AssetDatabase.CreateFolder("Assets/Tiles", "Animated");
            var tile = ScriptableObject.CreateInstance<RandomStartAnimatedTile>();
            tile.m_AnimatedSprites = sprites;
            tile.m_MinSpeed = Speed;
            tile.m_MaxSpeed = Speed;
            tile.blockCell = blockCell;
            string path = AssetDatabase.GenerateUniqueAssetPath($"{TileFolder}/Anim_{FirstName(sprites)}.asset");
            AssetDatabase.CreateAsset(tile, path);
            AssetDatabase.SaveAssets();
            return tile;
        }

        private static bool SameSprites(Sprite[] a, Sprite[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        private static bool IsEmpty(Sprite[] sprites)
        {
            foreach (var s in sprites)
                if (s != null)
                    return false;
            return true;
        }

        private static string FirstName(Sprite[] sprites)
        {
            foreach (var s in sprites)
                if (s != null)
                    return s.name;
            return "vide";
        }

        // Tuile animée déjà faite avec exactement les mêmes images (et la même place dans son objet),
        // pour ne pas créer de doublon
        private static RandomStartAnimatedTile FindExisting(Sprite[] sprites, Vector2Int blockCell)
        {
            if (!AssetDatabase.IsValidFolder(TileFolder))
                return null;
            foreach (string guid in AssetDatabase.FindAssets("t:RandomStartAnimatedTile", new[] { TileFolder }))
            {
                var other = AssetDatabase.LoadAssetAtPath<RandomStartAnimatedTile>(AssetDatabase.GUIDToAssetPath(guid));
                if (other == null || other.m_AnimatedSprites == null || other.m_AnimatedSprites.Length != sprites.Length ||
                    other.blockCell != blockCell)
                    continue;
                bool same = true;
                for (int i = 0; i < sprites.Length && same; i++)
                    same = other.m_AnimatedSprites[i] == sprites[i];
                if (same)
                    return other;
            }
            return null;
        }

        private static GameObject LoadPalette()
        {
            // Créée au besoin : copie vidée de Tiles1Palette pour garder les mêmes réglages de grille
            if (!File.Exists(PalettePath))
            {
                AssetDatabase.CopyAsset(SourcePalettePath, PalettePath);
                var empty = PrefabUtility.LoadPrefabContents(PalettePath);
                foreach (var map in empty.GetComponentsInChildren<Tilemap>())
                    map.ClearAllTiles();
                PrefabUtility.SaveAsPrefabAsset(empty, PalettePath);
                PrefabUtility.UnloadPrefabContents(empty);
            }
            return PrefabUtility.LoadPrefabContents(PalettePath);
        }

        // Pose le bloc (tuiles rangées depuis le bas à gauche, null = case vide) sur le 1er emplacement libre
        // de AnimatedPalette assez grand, lu de gauche à droite puis de haut en bas ; renvoie le coin haut-gauche
        private static Vector3Int AddToPalette(TileBase[] tiles, Vector2Int size)
        {
            var root = LoadPalette();
            var tilemap = root.GetComponentInChildren<Tilemap>();
            int columns = Mathf.Max(PaletteColumns, size.x);
            Vector3Int corner = Vector3Int.zero;
            for (int i = 0; ; i++)
            {
                int x = i % (columns - size.x + 1), row = i / (columns - size.x + 1);
                corner = new Vector3Int(x, -row, 0);
                if (IsFree(tilemap, corner, size))
                    break;
            }
            for (int y = 0; y < size.y; y++)
                for (int x = 0; x < size.x; x++)
                    if (tiles[y * size.x + x] != null)
                        tilemap.SetTile(new Vector3Int(corner.x + x, corner.y - (size.y - 1 - y), 0), tiles[y * size.x + x]);
            PrefabUtility.SaveAsPrefabAsset(root, PalettePath);
            PrefabUtility.UnloadPrefabContents(root);
            return corner;
        }

        private static bool IsFree(Tilemap tilemap, Vector3Int corner, Vector2Int size)
        {
            for (int y = 0; y < size.y; y++)
                for (int x = 0; x < size.x; x++)
                    if (tilemap.GetTile(new Vector3Int(corner.x + x, corner.y - y, 0)) != null)
                        return false;
            return true;
        }

        private static void RemoveFromPalette(List<RandomStartAnimatedTile> targets)
        {
            if (!File.Exists(PalettePath))
                return;
            var root = PrefabUtility.LoadPrefabContents(PalettePath);
            foreach (var tilemap in root.GetComponentsInChildren<Tilemap>())
            {
                tilemap.CompressBounds();
                foreach (var position in tilemap.cellBounds.allPositionsWithin)
                    if (tilemap.GetTile(position) is RandomStartAnimatedTile t && targets.Contains(t))
                        tilemap.SetTile(position, null);
            }
            PrefabUtility.SaveAsPrefabAsset(root, PalettePath);
            PrefabUtility.UnloadPrefabContents(root);
        }

        private static int CountUsesInOpenScenes(List<RandomStartAnimatedTile> targets)
        {
            int uses = 0;
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;
                foreach (var go in scene.GetRootGameObjects())
                {
                    foreach (var tilemap in go.GetComponentsInChildren<Tilemap>(true))
                    {
                        // Sans CompressBounds : la scène ne doit pas être modifiée par un simple comptage
                        foreach (var tile in tilemap.GetTilesBlock(tilemap.cellBounds))
                            if (tile is RandomStartAnimatedTile t && targets.Contains(t))
                                uses++;
                    }
                }
            }
            return uses;
        }
    }
}
