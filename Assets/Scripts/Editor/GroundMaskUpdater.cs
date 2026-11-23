using System.Collections.Generic;
using System.IO;
using DBZ.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace DBZ.EditorTools
{
    // Menu DBZ > Mettre à jour les masques de traces : dans les scènes ouvertes, cherche les tuiles de décor
    // (herbe, rochers…) peintes au-dessus d'un sol à traces de pas (neige, sable) et ajoute leur « couvercle » à
    // GroundMasks.asset : pixel dessiné = pas de trace, pixel vide (ou ombre semi-transparente) = sol du dessous.
    // À relancer après avoir peint du décor sur du sable ou de la neige. Les entrées existantes ne sont pas touchées.
    // Éditeur seulement (dossier Editor).
    public static class GroundMaskUpdater
    {
        private const string MasksPath = "Assets/Tiles/GroundMasks.asset";
        // Mêmes calques que Footprints.groundLayers (prefab Player)
        private static readonly string[] Layers = { "Background", "Decor 1", "Decor 2", "Decor 3" };
        // Marques des sols à traces (alpha de Tile.color = 255 − marque) : 10 neige, 11 sable clair, 12 sable foncé…
        private const int MinGroundMark = 10;
        private const int MaxGroundMark = 19;
        // En dessous : pixel vide (ombres dessinées en noir semi-transparent comprises)
        private const byte OpaqueAlpha = 128;

        [MenuItem("DBZ/Mettre à jour les masques de traces")]
        private static void UpdateMasksFromMenu()
        {
            UpdateMasks(true);
        }

        // showDialog faux : message dans la console seulement (lancement par script)
        public static void UpdateMasks(bool showDialog)
        {
            var masks = AssetDatabase.LoadAssetAtPath<GroundMaskSet>(MasksPath);
            if (masks == null)
            {
                Debug.LogWarning("[Masques de traces] Introuvable : " + MasksPath);
                if (showDialog)
                    EditorUtility.DisplayDialog("Masques de traces", "Introuvable : " + MasksPath, "OK");
                return;
            }

            var known = new Dictionary<TileBase, GroundMaskSet.Entry>();
            foreach (var entry in masks.Entries)
                if (entry.tile != null)
                    known[entry.tile] = entry;

            // Tuiles de décor posées au-dessus d'un sol à traces, pas encore dans les masques
            var covers = new List<TileBase>();
            var seen = new Dictionary<TileBase, bool>();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;
                var maps = new List<Tilemap>();
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var map in root.GetComponentsInChildren<Tilemap>(true))
                        if (System.Array.IndexOf(Layers, map.name) >= 0)
                            maps.Add(map);
                maps.Sort((a, b) => SortKey(b).CompareTo(SortKey(a)));   // le plus haut affiché d'abord
                if (maps.Count < 2)
                    continue;

                BoundsInt bounds = maps[0].cellBounds;
                foreach (var map in maps)
                {
                    BoundsInt b = map.cellBounds;
                    bounds.SetMinMax(Vector3Int.Min(bounds.min, b.min), Vector3Int.Max(bounds.max, b.max));
                }
                var above = new List<TileBase>();
                foreach (var cell in bounds.allPositionsWithin)
                {
                    above.Clear();
                    foreach (var map in maps)
                    {
                        TileBase tile = map.GetTile(cell);
                        if (tile == null)
                            continue;
                        if (IsGround(tile, known))
                        {
                            foreach (var cover in above)
                                if (!known.ContainsKey(cover) && !seen.ContainsKey(cover))
                                {
                                    seen[cover] = true;
                                    covers.Add(cover);
                                }
                            break;
                        }
                        above.Add(tile);
                    }
                }
            }

            int added = 0;
            var skipped = new List<string>();
            var textures = new Dictionary<string, Texture2D>();
            foreach (var tile in covers)
            {
                GroundMaskSet.Entry entry = MakeCover(tile, textures, out string problem);
                if (entry == null)
                {
                    skipped.Add(tile.name + " (" + problem + ")");
                    continue;
                }
                masks.Entries.Add(entry);
                added++;
            }
            foreach (var texture in textures.Values)
                Object.DestroyImmediate(texture);

            masks.ClearCache();
            EditorUtility.SetDirty(masks);
            AssetDatabase.SaveAssetIfDirty(masks);

            string message = added + " tuile(s) de décor ajoutée(s) aux masques de traces.";
            if (skipped.Count > 0)
                message += "\nIgnorées : " + string.Join(", ", skipped);
            Debug.Log("[Masques de traces] " + message);
            if (showDialog)
                EditorUtility.DisplayDialog("Masques de traces", message, "OK");
        }

        private static bool IsGround(TileBase tile, Dictionary<TileBase, GroundMaskSet.Entry> known)
        {
            if (known.TryGetValue(tile, out var entry))
                return entry.mark != 0;
            if (tile is Tile t)
            {
                int mark = Mathf.RoundToInt((1f - t.color.a) * 255f);
                return mark >= MinGroundMark && mark <= MaxGroundMark;
            }
            return false;
        }

        // Pixel dessiné sur au moins une image = couvre le sol ; vide sur toutes = sol du dessous visible
        private static GroundMaskSet.Entry MakeCover(TileBase tile, Dictionary<string, Texture2D> textures, out string problem)
        {
            problem = null;
            Sprite[] sprites = tile is Tile t ? new[] { t.sprite }
                : tile is AnimatedTile animated ? animated.m_AnimatedSprites : null;
            if (sprites == null || sprites.Length == 0 || sprites[0] == null)
            {
                problem = "type de tuile non lu";
                return null;
            }

            var entry = new GroundMaskSet.Entry { tile = tile, mark = 0 };
            var opaque = new bool[32, 32];
            foreach (var sprite in sprites)
            {
                if (sprite == null)
                    continue;
                Rect rect = sprite.rect;
                if (rect.width != 32 || rect.height != 32)
                {
                    problem = "image de " + rect.width + "×" + rect.height;
                    return null;
                }
                Texture2D texture = ReadTexture(sprite.texture, textures);
                if (texture == null)
                {
                    problem = "planche illisible";
                    return null;
                }
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                        if (texture.GetPixel((int)rect.x + x, (int)rect.y + 31 - y).a * 255f >= OpaqueAlpha)
                            opaque[x, y] = true;
            }
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    if (!opaque[x, y])
                        entry.emptyRows[y] |= 1 << x;
            return entry;
        }

        // Planche lue depuis le disque (les textures importées ne sont pas lisibles)
        private static Texture2D ReadTexture(Texture2D source, Dictionary<string, Texture2D> textures)
        {
            string path = AssetDatabase.GetAssetPath(source);
            if (textures.TryGetValue(path, out Texture2D texture))
                return texture;
            texture = null;
            if (File.Exists(path))
            {
                texture = new Texture2D(2, 2);
                // Taille différente de l'import (planche réduite) : coordonnées des images fausses
                if (!texture.LoadImage(File.ReadAllBytes(path)) || texture.width != source.width || texture.height != source.height)
                {
                    Object.DestroyImmediate(texture);
                    texture = null;
                }
            }
            textures[path] = texture;
            return texture;
        }

        private static long SortKey(Tilemap map)
        {
            var renderer = map.GetComponent<TilemapRenderer>();
            if (renderer == null)
                return long.MinValue;
            return (long)SortingLayer.GetLayerValueFromID(renderer.sortingLayerID) * 100000L + renderer.sortingOrder;
        }
    }
}
