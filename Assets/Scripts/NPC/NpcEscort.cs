using System.Collections.Generic;
using DBZ.Combat;
using DBZ.Enemies;
using UnityEngine;

namespace DBZ.NPC
{
    // NPC qui arrivent avec Cell imparfait (2026-11-26, choix util.) : au lancement, un NPC de chaque prefab de la liste
    // apparaît sur une case libre autour de lui et le suit ; à sa mort, ceux qui restent disparaissent aussi
    [RequireComponent(typeof(EnemyController), typeof(Health))]
    public class NpcEscort : MonoBehaviour
    {
        [Tooltip("Un NPC créé par prefab de la liste")]
        [SerializeField] private Npc[] npcPrefabs;
        [Tooltip("Distance d'apparition, en pas depuis sa case (de ... à ...)")]
        [SerializeField] private Vector2Int spawnDistance = new Vector2Int(1, 2);
        [Tooltip("Layer(s) des objets qui bloquent une case : pas d'apparition dessus")]
        [SerializeField] private LayerMask solidObjectsLayer;

        private readonly List<Npc> npcs = new List<Npc>();
        private Health health;

        private void Awake() => health = GetComponent<Health>();

        private void OnEnable() => health.Died += OnDied;

        private void OnDisable() => health.Died -= OnDied;

        // Dans Start : les calques de collision et de niveaux sont inscrits
        private void Start()
        {
            if (npcPrefabs == null || npcPrefabs.Length == 0)
                return;
            int level = GetComponent<EnemyController>().Level;
            List<Vector2Int> cells = SpawnCells.Pick(transform.position, level, spawnDistance, npcPrefabs.Length, solidObjectsLayer, transform);
            for (int i = 0; i < npcPrefabs.Length && i < cells.Count; i++)
            {
                if (npcPrefabs[i] == null)
                    continue;
                Npc npc = Instantiate(npcPrefabs[i], new Vector3(cells[i].x + 0.5f, cells[i].y + 0.5f, 0f), Quaternion.identity);
                npc.Leader = transform;
                npcs.Add(npc);
            }
        }

        private void OnDied()
        {
            foreach (var npc in npcs)
                if (npc != null)
                    npc.Vanish();
            npcs.Clear();
        }
    }
}
