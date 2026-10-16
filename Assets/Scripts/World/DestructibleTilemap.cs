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

        private Tilemap tilemap;
        // PV restants des seules cases déjà abîmées ; les autres ont encore tous leurs PV
        private readonly Dictionary<Vector3Int, float> damagedCells = new Dictionary<Vector3Int, float>();

        private void Awake()
        {
            tilemap = GetComponent<Tilemap>();
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
                // Le TilemapCollider2D se met à jour tout seul : la case devient franchissable
                tilemap.SetTile(cell, null);
                damagedCells.Remove(cell);
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
