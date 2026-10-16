using UnityEngine;
using UnityEngine.Tilemaps;

namespace DBZ.World
{
    // Calque Niveaux, peint avec des LevelTile : dit à quel niveau de hauteur passe celui qui arrive sur une case.
    // Case vide = on garde son niveau (la plupart de la carte, un pont et le dessous du pont).
    // Visible dans l'éditeur, caché en jeu.
    [RequireComponent(typeof(Tilemap))]
    public class LevelMap : MonoBehaviour
    {
        [Tooltip("Cacher les tuiles de niveau en jeu (elles restent visibles dans l'éditeur)")]
        [SerializeField] private bool hideInPlayMode = true;

        // Ordres d'affichage (sorting layer player) réservés à chaque niveau : niveau × 4 + rang dans le niveau
        // (0 joueur, 1 techniques, 2 impacts ; au niveau 0, 3 = Pont, pour passer au-dessus de tout ce qui est au sol).
        // À garder cohérent avec PlayerController.sortingOrderPerLevel (0, 4)
        public const int SortingOrdersPerLevel = 4;

        // Calque Niveaux de la scène (un seul) : les techniques le consultent sans réglage dans l'Inspector
        private static LevelMap active;

        private Tilemap tilemap;
        // Récupéré à la demande : le joueur peut interroger le calque avant son Awake
        private Tilemap Tilemap => tilemap != null ? tilemap : tilemap = GetComponent<Tilemap>();

        private void Awake()
        {
            if (hideInPlayMode && TryGetComponent(out TilemapRenderer tilemapRenderer))
                tilemapRenderer.enabled = false;
        }

        private void OnEnable()
        {
            active = this;
        }

        private void OnDisable()
        {
            if (active == this)
                active = null;
        }

        // Niveau de celui qui arrive sur cette case : celui de la tuile si la case est peinte, sinon le sien
        public static int LevelAfterEntering(Vector3 worldPosition, int currentLevel)
        {
            return active != null && active.TryGetLevel(worldPosition, out int level) ? level : currentLevel;
        }

        public bool TryGetLevel(Vector3 worldPosition, out int level)
        {
            var tile = Tilemap.GetTile<LevelTile>(Tilemap.WorldToCell(worldPosition));
            level = tile != null ? tile.level : 0;
            return tile != null;
        }
    }
}
