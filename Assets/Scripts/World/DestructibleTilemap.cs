using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DBZ.World
{
    // À poser sur le tilemap Destructibles : toute tuile peinte sur ce calque peut être détruite par les techniques.
    // Les obstacles indestructibles (grands arbres, murs...) restent sur le tilemap Obstacles.
    [RequireComponent(typeof(Tilemap))]
    public class DestructibleTilemap : MonoBehaviour
    {
        [Serializable]
        private class TileHealth
        {
            public TileBase tile;
            public float health = 1f;
        }

        [Tooltip("PV d'une tuile qui n'est pas dans la liste des exceptions")]
        [SerializeField] private float defaultHealth = 1f;

        [Tooltip("Tuiles plus résistantes que la normale (ex. gros rocher = 30 PV)")]
        [SerializeField] private List<TileHealth> healthOverrides = new List<TileHealth>();

        [SerializeField] private Color hitColor = Color.red;
        [SerializeField] private float flashDuration = 0.1f;

        [Header("Explosion à la destruction (vide = aucune)")]
        [Tooltip("Explosion jouée sur la case détruite (prefab DestructibleExplosion)")]
        [SerializeField] private GameObject explosionPrefab;
        [Tooltip("Calque d'affichage de l'explosion")]
        [SerializeField] private string explosionSortingLayer = "player";
        [Tooltip("Rang dans les ordres du niveau (2 = impacts des techniques)")]
        [SerializeField] private int explosionSortingRank = 2;
        [Tooltip("Temps pendant lequel une copie de la tuile reste affichée (2e image = grosse boule, qui la cache), en secondes ; la collision disparaît tout de suite")]
        [SerializeField] private float tileHideDelay = 1f / 12f;

        private Tilemap tilemap;
        private TilemapRenderer tilemapRenderer;
        // PV restants des seules cases déjà abîmées ; les autres ont encore tous leurs PV
        private readonly Dictionary<Vector3Int, float> damagedCells = new Dictionary<Vector3Int, float>();

        private void Awake()
        {
            tilemap = GetComponent<Tilemap>();
            tilemapRenderer = GetComponent<TilemapRenderer>();
        }

        // Explosion sur la case + copie de la tuile gardée tileHideDelay (la vraie tuile, et sa collision, sont déjà retirées)
        private void Explode(Vector3Int cell, Sprite sprite, Matrix4x4 matrix, Color color)
        {
            Vector3 center = tilemap.GetCellCenterWorld(cell);
            var explosion = Instantiate(explosionPrefab, center, Quaternion.identity);
            var explosionRenderer = explosion.GetComponent<SpriteRenderer>();
            if (explosionRenderer != null)
            {
                explosionRenderer.sortingLayerName = explosionSortingLayer;
                explosionRenderer.sortingOrder = HeightLevel.Of(this) * LevelMap.SortingOrdersPerLevel + explosionSortingRank;
            }

            if (sprite == null || tileHideDelay <= 0f)
                return;
            var copy = new GameObject("DestroyedTile");
            copy.transform.SetPositionAndRotation(center + (Vector3)matrix.GetColumn(3), matrix.rotation);
            copy.transform.localScale = matrix.lossyScale;
            var copyRenderer = copy.AddComponent<SpriteRenderer>();
            copyRenderer.sprite = sprite;
            // Alpha remis à 1 : certaines tuiles portent une marque dans leur alpha (lue par le shader des tilemaps seulement)
            color.a = 1f;
            copyRenderer.color = color;
            if (tilemapRenderer != null)
            {
                copyRenderer.sortingLayerID = tilemapRenderer.sortingLayerID;
                copyRenderer.sortingOrder = tilemapRenderer.sortingOrder;
            }
            Destroy(copy, tileHideDelay);
        }

        // worldPoint : un point situé dans la case touchée
        public void TakeDamage(Vector3 worldPoint, float amount)
        {
            Vector3Int cell = tilemap.WorldToCell(worldPoint);
            TileBase tile = tilemap.GetTile(cell);
            if (tile == null || amount <= 0f)
                return;

            if (!damagedCells.TryGetValue(cell, out float health))
                health = GetMaxHealth(tile);

            health -= amount;
            if (health <= 0f)
            {
                Sprite sprite = tilemap.GetSprite(cell);
                Matrix4x4 matrix = tilemap.GetTransformMatrix(cell);
                Color color = tilemap.GetColor(cell);
                // Le TilemapCollider2D se met à jour tout seul : la case devient franchissable
                tilemap.SetTile(cell, null);
                damagedCells.Remove(cell);
                if (explosionPrefab != null)
                    Explode(cell, sprite, matrix, color);
            }
            else
            {
                damagedCells[cell] = health;
                StartCoroutine(Flash(cell));
            }
        }

        private float GetMaxHealth(TileBase tile)
        {
            foreach (var entry in healthOverrides)
                if (entry.tile == tile)
                    return entry.health;
            return defaultHealth;
        }

        private IEnumerator Flash(Vector3Int cell)
        {
            // Par défaut une tuile verrouille sa couleur : on la déverrouille pour pouvoir la teinter
            tilemap.SetTileFlags(cell, TileFlags.None);
            tilemap.SetColor(cell, hitColor);
            yield return new WaitForSeconds(flashDuration);
            tilemap.SetColor(cell, Color.white);
        }
    }
}
