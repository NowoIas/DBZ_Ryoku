using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

namespace DBZ.World
{
    // Carte des pixels de sol (neige, sable…) des tuiles qui n'en sont qu'en partie : bords de neige (roche, vide),
    // plages animées (sable + vagues). Générée par Claude depuis les planches ; lue par les traces de pas (Footprints).
    // Une entrée dit aussi de quel sol il s'agit (marque) : les plages, déjà marquées « eau », n'ont pas besoin
    // d'une 2e marque dans leur fichier. Tuile marquée sans entrée ici = ce sol sur toute la case.
    // Entrée de marque 0 = « couvercle » : tuile de décor (herbe, rocher…) posée sur un sol à traces ; pixel dessiné =
    // pas de trace, pixel vide = on regarde le calque du dessous (menu DBZ > Mettre à jour les masques de traces).
    [CreateAssetMenu(menuName = "DBZ/Ground Mask Set")]
    public class GroundMaskSet : ScriptableObject
    {
        public enum Pixel { Ground, Empty, Solid }

        [Serializable]
        public class Entry
        {
            public TileBase tile;
            [Tooltip("Sol de la tuile : marque (10 neige, 11 sable clair, 12 sable foncé ; 0 = décor qui couvre le sol)")]
            public int mark = 10;
            [Tooltip("32 rangées (0 = haut de la case), bit x = colonne x depuis la gauche : pixel de ce sol")]
            [FormerlySerializedAs("snowRows")]
            public int[] groundRows = new int[32];
            [Tooltip("Même format : pixel transparent (on voit le calque du dessous)")]
            public int[] emptyRows = new int[32];
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private Dictionary<TileBase, Entry> lookup;

        public List<Entry> Entries => entries;

        public bool TryGetEntry(TileBase tile, out Entry entry)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<TileBase, Entry>();
                foreach (Entry e in entries)
                    if (e.tile != null)
                        lookup[e.tile] = e;
            }
            return lookup.TryGetValue(tile, out entry);
        }

        // x, y = pixel dans la case (0-31), y depuis le haut
        public static Pixel GetPixel(Entry entry, int x, int y)
        {
            x = Mathf.Clamp(x, 0, 31);
            y = Mathf.Clamp(y, 0, 31);
            int bit = 1 << x;
            return (entry.groundRows[y] & bit) != 0 ? Pixel.Ground : (entry.emptyRows[y] & bit) != 0 ? Pixel.Empty : Pixel.Solid;
        }

        // Après une modification par l'éditeur
        private void OnValidate()
        {
            lookup = null;
        }

        // Après un ajout d'entrées par un outil de l'éditeur
        public void ClearCache()
        {
            lookup = null;
        }
    }
}
