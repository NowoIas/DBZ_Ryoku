using System.Collections.Generic;
using DBZ.Combat;
using DBZ.Enemies;
using UnityEngine;
using UIImage = UnityEngine.UI.Image;

namespace DBZ.UI
{
    // Barres de vie des boss (en haut à droite du HUD) : une par boss qui poursuit Goku (Boss.Shown), l'une sous l'autre
    // dans l'ordre de repérage ; badge du boss à gauche, qui chevauche le bout de la barre.
    // À son K.O., la barre d'un boss se vide puis disparaît ; celles du dessous remontent.
    // Les images sont créées au lancement ; 1 pixel de sprite = 1 unité d'interface (comme le reste du HUD).
    // À placer sur un objet enfant du Canvas du HUD, ancré en haut à droite, pivot en haut à droite
    public class BossBar : MonoBehaviour
    {
        [Tooltip("Fond de la barre (cadre + creux noir)")]
        [SerializeField] private Sprite backSprite;
        [Tooltip("Remplissage rouge, posé dans le creux du fond")]
        [SerializeField] private Sprite fillSprite;
        [Tooltip("Matériau gris (shader DBZ/UI Grayscale) posé sur le remplissage et le badge quand le boss est invulnérable (stase)")]
        [SerializeField] private Material stasisMaterial;
        [Tooltip("Chevauchement du badge sur le bout gauche de la barre, en pixels")]
        [SerializeField] private int badgeOverlap = 2;
        [Tooltip("Position du remplissage dans le fond, en pixels depuis son coin haut-gauche")]
        [SerializeField] private Vector2Int fillOffset = new Vector2Int(3, 2);
        [Tooltip("Écart entre deux barres de boss, en pixels")]
        [SerializeField] private int barSpacing = 2;
        [Tooltip("Vitesse à laquelle la barre rattrape les PV (plus grand = plus rapide ; 10 ≈ 0,3 s)")]
        [SerializeField] private float drainSpeed = 10f;
        [Tooltip("Durée du vidage de la barre au K.O. d'un boss sans animation de mort, en secondes (avec : durée de l'animation)")]
        [SerializeField] private float deathDrainDuration = 0.15f;
        [Tooltip("Temps pendant lequel la barre vide reste affichée après le K.O. d'un boss sans animation de mort (avec : son temps couché), en secondes")]
        [SerializeField] private float hideDelayAfterDeath = 0.1f;

        [Header("Tremblement (coup sur un boss en stase)")]
        [Tooltip("Écart maximal du tremblement, en pixels (au début ; il diminue jusqu'à 1 px)")]
        [SerializeField] private int shakePixels = 2;
        [Tooltip("Durée du tremblement, en secondes (relancé à chaque coup)")]
        [SerializeField] private float shakeDuration = 0.2f;
        [Tooltip("Temps entre deux changements de côté, en secondes")]
        [SerializeField] private float shakeStepTime = 0.035f;

        // Hauteur de toutes les barres affichées (0 = aucune), lue par le chrono de fusion pour se placer dessous
        public static float VisibleHeight { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => VisibleHeight = 0f;

        // Barre d'un boss
        private class Slot
        {
            public RectTransform root;
            public UIImage badgeImage, backImage, fillImage;
            public Boss boss;
            // PV du boss (gardés pour se désabonner même si le boss est détruit)
            public Health health;
            public System.Action onBlocked;
            public bool isDown, isGrey, isDone;
            public float ratio, hideTime, deathStartRatio, shakeStart = -1f;
            // Durées de la mort (lues à l'affichage : l'objet peut être détruit au K.O.)
            public float drainDuration, hideDelay;
            public Sprite shownBadge;
            public int height;
        }

        private readonly List<Slot> slots = new List<Slot>();
        private Health playerHealth;

        private void OnDestroy()
        {
            foreach (var slot in slots)
                Unsubscribe(slot);
            slots.Clear();
            VisibleHeight = 0f;
        }

        private void Update()
        {
            if (playerHealth == null)
            {
                var tagged = GameObject.FindWithTag("Player");
                if (tagged != null)
                    playerHealth = tagged.GetComponent<Health>();
            }

            // Goku K.O. : barres cachées tout de suite
            if (playerHealth != null && playerHealth.IsDead)
            {
                for (int i = slots.Count - 1; i >= 0; i--)
                    Remove(i);
                VisibleHeight = 0f;
                return;
            }

            foreach (var boss in Boss.Shown)
                if (boss != null && FindSlot(boss) == null)
                    slots.Add(CreateSlot(boss));

            int y = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                Slot slot = slots[i];
                UpdateSlot(slot);
                if (slot.isDone)
                {
                    Remove(i);
                    i--;
                    continue;
                }
                slot.root.anchoredPosition = new Vector2(ShakeOffset(slot), -y);
                y += slot.height + barSpacing;
            }
            VisibleHeight = slots.Count > 0 ? y - barSpacing : 0f;
        }

        private Slot FindSlot(Boss boss)
        {
            foreach (var slot in slots)
                if (slot.boss == boss)
                    return slot;
            return null;
        }

        private Slot CreateSlot(Boss boss)
        {
            var slot = new Slot();
            var go = new GameObject("Bar " + boss.DisplayName, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            slot.root = (RectTransform)go.transform;
            slot.root.anchorMin = slot.root.anchorMax = slot.root.pivot = Vector2.one;

            slot.backImage = CreateImage(slot.root, "Back", backSprite);
            slot.fillImage = CreateImage(slot.root, "Fill", fillSprite);
            // Le remplissage se coupe par la droite
            slot.fillImage.type = UIImage.Type.Filled;
            slot.fillImage.fillMethod = UIImage.FillMethod.Horizontal;
            slot.fillImage.fillOrigin = (int)UIImage.OriginHorizontal.Left;
            slot.badgeImage = CreateImage(slot.root, "Badge", null);

            slot.boss = boss;
            slot.health = boss.Health;
            // Coup sur le boss invulnérable (stase) : badge et barre tremblent
            slot.onBlocked = () => slot.shakeStart = Time.time;
            slot.health.BlockedHit += slot.onBlocked;
            // Mort mise en scène (animation, explosions) : la barre se vide pendant l'animation (ou les explosions) et disparaît avec le boss
            var death = boss.GetComponent<DisappearOnDeath>();
            bool staged = death != null && death.HasDeathSequence;
            slot.drainDuration = staged ? death.DeathDrainDuration : deathDrainDuration;
            slot.hideDelay = staged ? death.DeathHideDelay : hideDelayAfterDeath;
            slot.ratio = boss.Health.Max > 0f ? boss.Health.Current / boss.Health.Max : 0f;

            Layout(slot, boss.Badge);
            UpdateFill(slot);
            return slot;
        }

        private void Remove(int index)
        {
            Slot slot = slots[index];
            Unsubscribe(slot);
            if (slot.root != null)
                Destroy(slot.root.gameObject);
            slots.RemoveAt(index);
        }

        private static void Unsubscribe(Slot slot)
        {
            if (slot.health != null)
                slot.health.BlockedHit -= slot.onBlocked;
            slot.health = null;
        }

        private void UpdateSlot(Slot slot)
        {
            float target;
            // Boss détruit (objet supprimé au K.O.) ou à 0 PV : la barre se vide, puis disparaît
            if (slot.boss == null || slot.boss.Health.IsDead)
            {
                if (!slot.isDown)
                {
                    slot.isDown = true;
                    slot.hideTime = -1f;
                    slot.deathStartRatio = slot.ratio;
                }
                target = 0f;
            }
            else if (!Contains(slot.boss))
            {
                // Poursuite abandonnée
                slot.isDone = true;
                return;
            }
            else
                target = slot.boss.Health.Current / slot.boss.Health.Max;

            if (slot.boss != null && slot.boss.Badge != slot.shownBadge)
                Layout(slot, slot.boss.Badge);
            UpdateStasis(slot);
            if (slot.isDown)
                // K.O. : vidage rapide à vitesse constante
                slot.ratio = Mathf.MoveTowards(slot.ratio, 0f, slot.deathStartRatio * Time.deltaTime / Mathf.Max(0.01f, slot.drainDuration));
            else
            {
                slot.ratio = Mathf.Lerp(slot.ratio, target, 1f - Mathf.Exp(-drainSpeed * Time.deltaTime));
                if (Mathf.Abs(slot.ratio - target) < 0.002f)
                    slot.ratio = target;
            }
            UpdateFill(slot);

            if (slot.isDown && slot.ratio <= 0f)
            {
                if (slot.hideTime < 0f)
                    slot.hideTime = Time.time + slot.hideDelay;
                else if (Time.time >= slot.hideTime)
                    slot.isDone = true;
            }
        }

        private static bool Contains(Boss boss)
        {
            foreach (var shown in Boss.Shown)
                if (shown == boss)
                    return true;
            return false;
        }

        // Tremblement gauche / droite en pixels entiers, de shakePixels à 1 px
        private float ShakeOffset(Slot slot)
        {
            if (slot.shakeStart < 0f)
                return 0f;
            float t = Time.time - slot.shakeStart;
            if (t >= shakeDuration)
            {
                slot.shakeStart = -1f;
                return 0f;
            }
            int amplitude = Mathf.Max(1, Mathf.RoundToInt(shakePixels * (1f - t / shakeDuration)));
            int step = (int)(t / Mathf.Max(0.01f, shakeStepTime));
            return step % 2 == 0 ? amplitude : -amplitude;
        }

        // Badge + barre placés au pixel ; relancé quand le boss change de badge (nouvelle forme)
        private void Layout(Slot slot, Sprite badge)
        {
            slot.shownBadge = badge;
            slot.badgeImage.enabled = badge != null;
            int badgeWidth = 0, badgeHeight = 0;
            if (badge != null)
            {
                slot.badgeImage.sprite = badge;
                badgeWidth = (int)badge.rect.width;
                badgeHeight = (int)badge.rect.height;
            }
            int backWidth = backSprite != null ? (int)backSprite.rect.width : 0;
            int backHeight = backSprite != null ? (int)backSprite.rect.height : 0;

            // Positions depuis le coin haut-gauche de la barre (y vers le bas), pixels entiers
            int barX = badge != null ? badgeWidth - badgeOverlap : 0;
            int height = Mathf.Max(badgeHeight, backHeight);
            int barY = (height - backHeight) / 2;
            int width = barX + backWidth;

            slot.root.sizeDelta = new Vector2(width, height);
            Place(slot.badgeImage, 0, (height - badgeHeight) / 2, badgeWidth, badgeHeight);
            Place(slot.backImage, barX, barY, backWidth, backHeight);
            if (fillSprite != null)
                Place(slot.fillImage, barX + fillOffset.x, barY + fillOffset.y, (int)fillSprite.rect.width, (int)fillSprite.rect.height);
            slot.height = height;
        }

        // Boss invulnérable (stase) : remplissage et badge grisés
        private void UpdateStasis(Slot slot)
        {
            bool invulnerable = slot.boss != null && !slot.boss.Health.IsDead && slot.boss.Health.IsInvulnerable;
            if (invulnerable == slot.isGrey)
                return;
            slot.isGrey = invulnerable;
            Material material = invulnerable ? stasisMaterial : null;
            slot.fillImage.material = material;
            slot.badgeImage.material = material;
        }

        // Remplissage arrondi au pixel entier (au moins 1 pixel tant que le boss a des PV)
        private void UpdateFill(Slot slot)
        {
            if (fillSprite == null)
                return;
            int width = (int)fillSprite.rect.width;
            int pixels = Mathf.RoundToInt(slot.ratio * width);
            if (pixels == 0 && slot.ratio > 0f)
                pixels = 1;
            slot.fillImage.fillAmount = (float)pixels / width;
        }

        // Image ancrée en haut à gauche de sa barre
        private static UIImage CreateImage(RectTransform parent, string objectName, Sprite sprite)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<UIImage>();
            image.raycastTarget = false;
            image.sprite = sprite;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            return image;
        }

        private static void Place(UIImage image, int x, int y, int width, int height)
        {
            image.rectTransform.anchoredPosition = new Vector2(x, -y);
            image.rectTransform.sizeDelta = new Vector2(width, height);
        }
    }
}
