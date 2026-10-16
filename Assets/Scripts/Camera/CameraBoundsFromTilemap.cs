using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DBZ.CameraSystem
{
    // Ajuste le PolygonCollider2D de cet objet à la taille du tilemap de sol,
    // pour que le Confiner de la caméra suive automatiquement la taille de la carte
    [RequireComponent(typeof(PolygonCollider2D))]
    public class CameraBoundsFromTilemap : MonoBehaviour
    {
        // Nom du tilemap cherché dans la scène quand aucun n'est assigné : chaque carte a un calque Background
        private const string DefaultTilemapName = "Background";

        // Tilemap qui définit les limites de la carte (le sol). Vide = le tilemap "Background" de la scène
        // (le prefab CameraRig marche ainsi dans toutes les scènes sans réglage)
        [SerializeField] private Tilemap tilemap;
        // Confiner à prévenir quand les limites changent
        [SerializeField] private CinemachineConfiner2D confiner;

        private void Awake()
        {
            UpdateBounds();
        }

        // Aussi accessible depuis l'Inspector (clic droit sur le composant) pour voir le résultat hors du mode Play
        [ContextMenu("Mettre à jour les limites")]
        public void UpdateBounds()
        {
            if (tilemap == null)
                tilemap = FindDefaultTilemap();
            if (tilemap == null)
            {
                Debug.LogWarning($"CameraBoundsFromTilemap : aucun tilemap assigné ni nommé « {DefaultTilemapName} » dans la scène.");
                return;
            }

            // Les limites d'un tilemap ne rétrécissent pas toutes seules quand on efface des tuiles
            tilemap.CompressBounds();
            Bounds bounds = tilemap.localBounds;

            // Coins du tilemap convertis dans l'espace local de ce collider
            Vector2[] corners =
            {
                ToLocal(new Vector3(bounds.min.x, bounds.min.y)),
                ToLocal(new Vector3(bounds.min.x, bounds.max.y)),
                ToLocal(new Vector3(bounds.max.x, bounds.max.y)),
                ToLocal(new Vector3(bounds.max.x, bounds.min.y)),
            };

            var polygon = GetComponent<PolygonCollider2D>();
            polygon.pathCount = 1;
            polygon.SetPath(0, corners);

            if (confiner != null)
                confiner.InvalidateBoundingShapeCache();
        }

        private Tilemap FindDefaultTilemap()
        {
            foreach (var candidate in FindObjectsByType<Tilemap>())
            {
                if (candidate.name == DefaultTilemapName && candidate.gameObject.scene == gameObject.scene)
                    return candidate;
            }
            return null;
        }

        private Vector2 ToLocal(Vector3 tilemapLocalPoint)
        {
            return transform.InverseTransformPoint(tilemap.transform.TransformPoint(tilemapLocalPoint));
        }
    }
}
