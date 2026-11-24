using System.Collections.Generic;
using DBZ.Combat;
using UnityEngine;

namespace DBZ.Enemies
{
    // Invocation de tourelles (Dr Gero) : pendant la poursuite, une tourelle toutes les summonInterval secondes,
    // sur une case libre à spawnDistance pas de lui, tant qu'il en a moins de maxTurrets en vie.
    // Pas pendant une pause (stase, ballon de Gotenks, étourdi). À sa mort, ses tourelles explosent aussi
    [RequireComponent(typeof(EnemyController), typeof(Health))]
    public class TurretSummoner : MonoBehaviour
    {
        [SerializeField] private GameObject turretPrefab;
        [Tooltip("Nombre maximal de tourelles en vie en même temps")]
        [SerializeField] private int maxTurrets = 5;
        [Tooltip("Temps entre deux invocations, en secondes")]
        [SerializeField] private float summonInterval = 8f;
        [Tooltip("Délai avant la première invocation, après le repérage de Goku, en secondes")]
        [SerializeField] private float firstDelay = 2f;
        [Tooltip("Distance d'apparition, en pas depuis sa case (de ... à ...) : cases atteignables à pied, libres")]
        [SerializeField] private Vector2Int spawnDistance = new Vector2Int(2, 4);
        [Tooltip("Layer(s) des objets qui bloquent une case (cibles, ennemis, tourelles) : pas d'apparition dessus")]
        [SerializeField] private LayerMask solidObjectsLayer;
        [Tooltip("Ses tourelles explosent à sa mort")]
        [SerializeField] private bool destroyTurretsOnDeath = true;

        private EnemyController controller;
        private Health health;
        private readonly List<Health> turrets = new List<Health>();
        private bool wasChasing;
        private float nextSummonTime;

        private void Awake()
        {
            controller = GetComponent<EnemyController>();
            health = GetComponent<Health>();
        }

        private void OnEnable() => health.Died += OnDied;

        private void OnDisable() => health.Died -= OnDied;

        private void Update()
        {
            bool chasing = controller.IsChasing;
            if (chasing && !wasChasing)
                nextSummonTime = Time.time + firstDelay;
            wasChasing = chasing;
            if (!chasing || health.IsDead || controller.IsPaused || StatusEffects.IsLockedOn(gameObject) || Time.time < nextSummonTime)
                return;

            nextSummonTime = Time.time + summonInterval;
            turrets.RemoveAll(t => t == null || t.IsDead);
            if (turretPrefab == null || turrets.Count >= maxTurrets)
                return;
            List<Vector2Int> cells = SpawnCells.Pick(transform.position, controller.Level, spawnDistance, 1, solidObjectsLayer, transform);
            if (cells.Count == 0)
                return;
            var go = Instantiate(turretPrefab, new Vector3(cells[0].x + 0.5f, cells[0].y + 0.5f, 0f), Quaternion.identity);
            if (go.TryGetComponent(out Health h))
                turrets.Add(h);
        }

        private void OnDied()
        {
            if (!destroyTurretsOnDeath)
                return;
            foreach (var t in turrets)
            {
                if (t == null || t.IsDead)
                    continue;
                t.IsInvulnerable = false;
                t.TakeDamage(t.Current);
            }
            turrets.Clear();
        }
    }
}
