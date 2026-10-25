using System.Collections.Generic;
using UnityEngine;

namespace DBZ.Combat
{
    // États d'un personnage touché (ennemi, cible) : ajouté tout seul au premier état reçu (StatusEffects.Of).
    // Chaque état a son icône, posée sur la case au-dessus du personnage, en bas à gauche, les suivantes à sa droite
    // (prévu pour cumuler plusieurs états).
    // Étourdi (stun) : son Animator est arrêté, il ne bouge plus et n'attaque plus (EnemyController lit IsLocked) ;
    // il reçoit toujours les coups. Ballon (Volleyball de Gotenks) : son image est remplacée par le ballon ;
    // sans ballon (poing de Goten) : il reste figé sur l'image qu'il avait.
    // Saignement (épée de Trunks) / brûlure (Burning Blast) : dégâts répétés, comptés comme des coups du joueur (PlayerCombat.Hit)
    public class StatusEffects : MonoBehaviour
    {
        // Écart entre deux icônes, en pixels
        private const int IconSpacingPixels = 1;
        private const string IconSortingLayer = "foreground";
        private const int IconSortingOrder = 30;

        private class Status
        {
            public float endTime;
            public SpriteRenderer icon;
            // Dégâts par tic (0 = aucun) : saignement, brûlure
            public float damagePerTick;
            public float tickInterval;
            public float nextTickTime;
        }

        // États en cours, par nom (« stun »...), dans l'ordre d'arrivée (ordre des icônes)
        private readonly List<string> order = new List<string>();
        private readonly Dictionary<string, Status> statuses = new Dictionary<string, Status>();

        private SpriteRenderer spriteRenderer;
        private Animator animator;
        private Health health;
        // Ballon : image affichée à la place du personnage (vide = figé sur son image), et son image d'avant (rendue s'il n'a pas d'Animator)
        private Sprite lockSprite;
        private Sprite spriteBeforeLock;
        private bool animatorWasEnabled;
        private bool isLocked;

        public const string Stun = "stun";
        public const string Bleed = "bleed";
        public const string Burn = "burn";

        // Personnage étourdi (figé ou ballon) : ni déplacement ni attaque
        public bool IsLocked => isLocked;

        public static StatusEffects Of(Component target)
        {
            var health = target.GetComponentInParent<Health>();
            var owner = health != null ? health.gameObject : target.gameObject;
            return owner.TryGetComponent(out StatusEffects effects) ? effects : owner.AddComponent<StatusEffects>();
        }

        // Pour les scripts qui ne veulent pas ajouter le composant (EnemyController à chaque image)
        public static bool IsLockedOn(GameObject owner)
        {
            return owner.TryGetComponent(out StatusEffects effects) && effects.IsLocked;
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            animator = GetComponent<Animator>();
            health = GetComponent<Health>();
            if (health != null)
                health.Died += ClearAll;
        }

        private void OnDestroy()
        {
            if (health != null)
                health.Died -= ClearAll;
        }

        public bool Has(string status)
        {
            return statuses.ContainsKey(status);
        }

        // Étourdi pendant duration secondes (relancé : la durée repart de zéro) ; ballSprite vide = figé sur son image
        // (un ballon déjà affiché le reste)
        public void Lock(float duration, Sprite ballSprite, Sprite icon)
        {
            if (!isLocked)
            {
                spriteBeforeLock = spriteRenderer != null ? spriteRenderer.sprite : null;
                animatorWasEnabled = animator != null && animator.enabled;
                if (animator != null)
                    animator.enabled = false;
                isLocked = true;
            }
            if (ballSprite != null)
                lockSprite = ballSprite;
            Add(Stun, duration, icon);
        }

        // Dégâts répétés (saignement, brûlure) : damagePerTick toutes les tickInterval secondes pendant duration ;
        // relancé : la durée repart de zéro, les dégâts ne s'additionnent pas (le rythme des tics continue)
        public void AddDamageOverTime(string status, float duration, float damagePerTick, float tickInterval, Sprite icon)
        {
            bool isNew = !statuses.ContainsKey(status);
            Add(status, duration, icon);
            var entry = statuses[status];
            entry.damagePerTick = damagePerTick;
            entry.tickInterval = Mathf.Max(0.05f, tickInterval);
            if (isNew)
                entry.nextTickTime = Time.time + entry.tickInterval;
        }

        public void Add(string status, float duration, Sprite icon)
        {
            if (!statuses.TryGetValue(status, out var entry))
            {
                entry = new Status();
                if (icon != null)
                {
                    entry.icon = new GameObject("Status_" + status).AddComponent<SpriteRenderer>();
                    entry.icon.transform.SetParent(transform, false);
                    entry.icon.sprite = icon;
                    entry.icon.sortingLayerName = IconSortingLayer;
                    entry.icon.sortingOrder = IconSortingOrder;
                }
                statuses[status] = entry;
                order.Add(status);
            }
            entry.endTime = Time.time + duration;
            PlaceIcons();
        }

        private void Remove(string status)
        {
            if (!statuses.TryGetValue(status, out var entry))
                return;
            if (entry.icon != null)
                Destroy(entry.icon.gameObject);
            statuses.Remove(status);
            order.Remove(status);
            if (status == Stun)
                Unlock();
            PlaceIcons();
        }

        private void Unlock()
        {
            if (!isLocked)
                return;
            isLocked = false;
            if (animator != null)
                animator.enabled = animatorWasEnabled;
            else if (spriteRenderer != null && lockSprite != null)
                spriteRenderer.sprite = spriteBeforeLock;
            lockSprite = null;
        }

        private void ClearAll()
        {
            foreach (var status in new List<string>(order))
                Remove(status);
        }

        private void Update()
        {
            // Copie : un tic peut mettre la cible K.O., ce qui vide la liste (ClearAll)
            foreach (var status in new List<string>(order))
            {
                if (!statuses.TryGetValue(status, out var entry) || entry.damagePerTick <= 0f)
                    continue;
                // Dernier tic compris s'il tombe pile à la fin
                while (Time.time >= entry.nextTickTime && entry.nextTickTime <= entry.endTime && statuses.ContainsKey(status))
                {
                    entry.nextTickTime += entry.tickInterval;
                    if (health != null && !health.IsDead)
                        PlayerCombat.Hit(health, entry.damagePerTick);
                }
            }
            for (int i = order.Count - 1; i >= 0; i--)
            {
                if (Time.time >= statuses[order[i]].endTime)
                    Remove(order[i]);
            }
        }

        // Après l'Animator et les scripts qui changent l'image : le ballon reste affiché ; les icônes suivent la case
        private void LateUpdate()
        {
            if (lockSprite != null && spriteRenderer != null)
                spriteRenderer.sprite = lockSprite;
            PlaceIcons();
        }

        // Case au-dessus du personnage : la 1re icône dans son coin bas gauche, les suivantes à sa droite (pivot des icônes en bas à gauche)
        private void PlaceIcons()
        {
            Vector2 cell = new Vector2(Mathf.Floor(transform.position.x) + 0.5f, Mathf.Floor(transform.position.y) + 0.5f);
            Vector2 corner = cell + new Vector2(-0.5f, 0.5f);
            float x = 0f;
            foreach (var status in order)
            {
                var icon = statuses[status].icon;
                if (icon == null)
                    continue;
                icon.transform.position = new Vector3(corner.x + x / 32f, corner.y, 0f);
                x += icon.sprite.rect.width + IconSpacingPixels;
            }
        }
    }
}
