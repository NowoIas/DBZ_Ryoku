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
    // Boss enragé (BossRage, C17 / C18) : aura de flamme autour du badge et de la barre (2026-11-26, demande util.),
    // couleurs de l'aura de rage (liseré cramoisi, bord bordeaux), langues de feu qui montent ; l'aura reste en place,
    // ce sont le badge et la barre qui tremblent d'1 px pendant la rage (précision util.) ;
    // calculée au pixel sur le processeur (petite image refaite rageFps fois par seconde), derrière badge et barre.
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
        [Tooltip("K.O. : la barre est vide aussitôt (PV à 0) ; elle reste affichée tant que le boss est sur la carte "
            + "(animation de mort, explosions, pierre), puis encore ce temps après sa disparition, en secondes")]
        [SerializeField] private float hideDelayAfterDeath = 0.1f;

        [Header("Tremblement (coup sur un boss en stase)")]
        [Tooltip("Écart maximal du tremblement, en pixels (au début ; il diminue jusqu'à 1 px)")]
        [SerializeField] private int shakePixels = 2;
        [Tooltip("Durée du tremblement, en secondes (relancé à chaque coup)")]
        [SerializeField] private float shakeDuration = 0.2f;
        [Tooltip("Temps entre deux changements de côté, en secondes")]
        [SerializeField] private float shakeStepTime = 0.035f;

        [Header("Aura de rage (boss enragé)")]
        [Tooltip("Liseré de la flamme, contre le badge et la barre")]
        [SerializeField] private Color32 rageRimColor = new Color32(214, 22, 38, 255);
        [Tooltip("Bord extérieur de la flamme (1 px)")]
        [SerializeField] private Color32 rageEdgeColor = new Color32(112, 10, 22, 255);
        [Tooltip("Épaisseur moyenne de la flamme sur les côtés et dessous, en pixels")]
        [SerializeField] private float rageRadius = 2f;
        [Tooltip("Étirement des flammes vers le haut (1 = comme les côtés)")]
        [SerializeField] private float rageUpStretch = 2.2f;
        [Tooltip("Force des langues de feu (0 = contour régulier)")]
        [SerializeField] private float rageTongue = 0.9f;
        [Tooltip("Taille des langues, en pixels (largeur, hauteur)")]
        [SerializeField] private Vector2 rageTongueSize = new Vector2(3f, 4f);
        [Tooltip("Montée des flammes, en pixels par seconde")]
        [SerializeField] private float rageRiseSpeed = 20f;
        [Tooltip("Changements de la flamme par seconde")]
        [SerializeField] private float rageFps = 12f;
        [Tooltip("Tremblement du badge et de la barre pendant la rage, en pixels (l'aura reste en place)")]
        [SerializeField] private int rageShakePixels = 1;
        [Tooltip("Temps entre deux secousses du badge et de la barre, en secondes")]
        [SerializeField] private float rageShakeStepTime = 0.05f;
        [Tooltip("Durée d'apparition / de disparition de l'aura, en secondes")]
        [SerializeField] private float rageFadeDuration = 0.2f;

        // Marges de l'image de l'aura autour du badge + barre (flammes plus hautes en haut)
        private const int RageMarginTop = 9;
        private const int RageMargin = 4;

        // Hauteur de toutes les barres affichées (0 = aucune), lue par le chrono de fusion pour se placer dessous
        public static float VisibleHeight { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            VisibleHeight = 0f;
            spritePixels.Clear();
        }

        // Barre d'un boss
        private class Slot
        {
            public RectTransform root;
            // Badge + barre (tremblent pendant la rage, sans l'aura)
            public RectTransform shaken;
            public UIImage badgeImage, backImage, fillImage, rageImage;
            public Boss boss;
            public BossRage rage;
            // Aura de rage : distance de chaque pixel au badge + barre (étirée vers le haut), image refaite au rythme rageFps
            public float[] rageDistance, rageUpShare;
            public int rageWidth, rageHeight, rageFrame = -1;
            public Texture2D rageTexture;
            public Sprite rageSprite;
            public Color32[] ragePixels;
            public float rageIntensity;
            public Vector2 rageBase;
            // PV du boss (gardés pour se désabonner même si le boss est détruit)
            public Health health;
            public System.Action onBlocked;
            public bool isDown, isGrey, isDone;
            public float ratio, hideTime, shakeStart = -1f;
            public Sprite shownBadge;
            public int height;
        }

        private readonly List<Slot> slots = new List<Slot>();
        // Pixels des sprites lus une fois (badges, fond de barre), même s'ils ne sont pas lisibles à l'import
        private static readonly Dictionary<Sprite, Color32[]> spritePixels = new Dictionary<Sprite, Color32[]>();
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

            // Aura de rage en premier : derrière tout le reste
            slot.rageImage = CreateImage(slot.root, "RageAura", null);
            slot.rageImage.enabled = false;
            var shakenObject = new GameObject("Content", typeof(RectTransform));
            shakenObject.transform.SetParent(slot.root, false);
            slot.shaken = (RectTransform)shakenObject.transform;
            slot.shaken.anchorMin = slot.shaken.anchorMax = slot.shaken.pivot = new Vector2(0f, 1f);
            slot.shaken.anchoredPosition = Vector2.zero;
            slot.backImage = CreateImage(slot.shaken, "Back", backSprite);
            slot.fillImage = CreateImage(slot.shaken, "Fill", fillSprite);
            // Le remplissage se coupe par la droite
            slot.fillImage.type = UIImage.Type.Filled;
            slot.fillImage.fillMethod = UIImage.FillMethod.Horizontal;
            slot.fillImage.fillOrigin = (int)UIImage.OriginHorizontal.Left;
            slot.badgeImage = CreateImage(slot.shaken, "Badge", null);

            slot.boss = boss;
            slot.rage = boss.GetComponent<BossRage>();
            slot.health = boss.Health;
            // Coup sur le boss invulnérable (stase) : badge et barre tremblent
            slot.onBlocked = () => slot.shakeStart = Time.time;
            slot.health.BlockedHit += slot.onBlocked;
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
            DestroyRageImage(slot);
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
            // Boss à 0 PV : barre vide tout de suite (elle suit les PV), gardée pendant sa mort (animation, explosions, pierre),
            // cachée une fois l'objet du boss supprimé
            if (slot.boss == null || slot.boss.Health.IsDead)
            {
                if (!slot.isDown)
                {
                    slot.isDown = true;
                    slot.hideTime = -1f;
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
            UpdateRage(slot);
            // La barre = les PV, à chaque image (pas de rattrapage progressif, demande util. 2026-11-27)
            slot.ratio = slot.isDown ? 0f : target;
            UpdateFill(slot);

            if (slot.isDown && slot.boss == null)
            {
                if (slot.hideTime < 0f)
                    slot.hideTime = Time.time + hideDelayAfterDeath;
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
            if (slot.rage != null)
                BuildRageField(slot, badge, barX, barY, width, height);
        }

        // Distance de chaque pixel de l'image de l'aura au badge + barre : plus « courte » vers le haut (flammes hautes),
        // plus « longue » vers le bas ; calculée une fois par mise en page
        private void BuildRageField(Slot slot, Sprite badge, int barX, int barY, int width, int height)
        {
            int w = width + RageMargin * 2, h = height + RageMarginTop + RageMargin;
            var mask = new bool[w * h];
            Stamp(mask, w, h, badge, RageMargin, RageMarginTop + (height - (badge != null ? (int)badge.rect.height : 0)) / 2);
            Stamp(mask, w, h, backSprite, RageMargin + barX, RageMarginTop + barY);

            slot.rageWidth = w;
            slot.rageHeight = h;
            slot.rageDistance = new float[w * h];
            slot.rageUpShare = new float[w * h];
            int searchUp = RageMarginTop, searchSide = RageMargin, searchDown = 3;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (mask[i])
                    {
                        slot.rageDistance[i] = -1f;
                        continue;
                    }
                    float nearest = 99f, up = 0f;
                    // y vers le bas : dy > 0 = le badge / la barre est sous ce pixel
                    for (int dy = -searchDown; dy <= searchUp; dy++)
                    {
                        int sy = y + dy;
                        if (sy < 0 || sy >= h)
                            continue;
                        float vy = dy > 0 ? dy / rageUpStretch : -dy * 2f;
                        if (vy > nearest)
                            continue;
                        for (int dx = -searchSide; dx <= searchSide; dx++)
                        {
                            int sx = x + dx;
                            if (sx < 0 || sx >= w || !mask[sy * w + sx])
                                continue;
                            float d = Mathf.Sqrt(dx * dx + vy * vy);
                            if (d < nearest)
                            {
                                nearest = d;
                                up = Mathf.Max(0, dy);
                            }
                        }
                    }
                    slot.rageDistance[i] = nearest;
                    slot.rageUpShare[i] = Mathf.Clamp01(up / 6f);
                }

            DestroyRageImage(slot);
            slot.rageTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "BossRageAura"
            };
            slot.ragePixels = new Color32[w * h];
            slot.rageSprite = Sprite.Create(slot.rageTexture, new Rect(0, 0, w, h), new Vector2(0f, 1f), 1f);
            slot.rageImage.sprite = slot.rageSprite;
            slot.rageBase = new Vector2(-RageMargin, RageMarginTop);
            slot.rageImage.rectTransform.anchoredPosition = slot.rageBase;
            slot.rageImage.rectTransform.sizeDelta = new Vector2(w, h);
            slot.rageFrame = -1;
        }

        // Pixels opaques d'un sprite posés dans le masque (coin haut-gauche en x, y)
        private static void Stamp(bool[] mask, int w, int h, Sprite sprite, int left, int top)
        {
            if (sprite == null)
                return;
            Color32[] pixels = ReadSprite(sprite);
            int sw = (int)sprite.rect.width, sh = (int)sprite.rect.height;
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                {
                    // Pixels du sprite rangés du bas vers le haut
                    if (pixels[(sh - 1 - y) * sw + x].a < 128)
                        continue;
                    int mx = left + x, my = top + y;
                    if (mx >= 0 && mx < w && my >= 0 && my < h)
                        mask[my * w + mx] = true;
                }
        }

        // Pixels d'un sprite (copie par la carte graphique si l'image n'est pas lisible à l'import)
        private static Color32[] ReadSprite(Sprite sprite)
        {
            if (spritePixels.TryGetValue(sprite, out Color32[] cached))
                return cached;
            Rect rect = sprite.textureRect;
            Texture2D source = sprite.texture;
            var copy = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
            if (source.isReadable)
                copy.SetPixels(source.GetPixels((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height));
            else
            {
                RenderTexture temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(source, temporary);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = temporary;
                copy.ReadPixels(rect, 0, 0);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
            copy.Apply(false);
            Color32[] pixels = copy.GetPixels32();
            Destroy(copy);
            spritePixels[sprite] = pixels;
            return pixels;
        }

        private static void DestroyRageImage(Slot slot)
        {
            if (slot.rageSprite != null)
                Destroy(slot.rageSprite);
            if (slot.rageTexture != null)
                Destroy(slot.rageTexture);
            slot.rageSprite = null;
            slot.rageTexture = null;
        }

        // Boss enragé : aura de flamme qui apparaît en fondu et ondule au rythme rageFps ; badge + barre tremblent
        private void UpdateRage(Slot slot)
        {
            if (slot.rage == null || slot.rageTexture == null)
                return;
            bool enraged = slot.boss != null && !slot.boss.Health.IsDead && slot.rage.IsEnraged;
            float target = enraged ? 1f : 0f;
            slot.rageIntensity = rageFadeDuration > 0f
                ? Mathf.MoveTowards(slot.rageIntensity, target, Time.deltaTime / rageFadeDuration) : target;
            slot.rageImage.enabled = slot.rageIntensity > 0f;
            // Badge + barre : secousse d'1 px au hasard pendant la rage (tirage fixe par pas de temps), à leur place sinon
            Vector2 shake = Vector2.zero;
            if (enraged)
            {
                int step = Mathf.FloorToInt(Time.time / Mathf.Max(0.01f, rageShakeStepTime));
                shake = new Vector2(Mathf.RoundToInt((Hash(step, 1.7f) * 2f - 1f) * rageShakePixels),
                    Mathf.RoundToInt((Hash(step, 5.3f) * 2f - 1f) * rageShakePixels));
            }
            slot.shaken.anchoredPosition = shake;
            if (!slot.rageImage.enabled)
                return;
            slot.rageImage.color = new Color(1f, 1f, 1f, slot.rageIntensity);

            int frame = Mathf.FloorToInt(Time.time * rageFps);
            if (frame != slot.rageFrame)
            {
                slot.rageFrame = frame;
                DrawRage(slot, frame / Mathf.Max(1f, rageFps));
            }

        }

        // Flamme du moment : bord découpé par un bruit qui monte, langues plus marquées en haut
        private void DrawRage(Slot slot, float time)
        {
            int w = slot.rageWidth, h = slot.rageHeight;
            float rise = time * rageRiseSpeed;
            var clear = new Color32(0, 0, 0, 0);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float distance = slot.rageDistance[i];
                    Color32 color = clear;
                    // Sous le badge et la barre : flamme pleine, pour que leur tremblement ne découvre jamais de vide
                    if (distance < 0f)
                        color = rageRimColor;
                    else if (distance < 98f)
                    {
                        // Motif qui monte : y vers le bas, donc + rise
                        float big = Noise(x / rageTongueSize.x, (y + rise) / rageTongueSize.y);
                        float small = Noise(x / (rageTongueSize.x * 0.5f) + 17f, (y + rise * 1.4f) / (rageTongueSize.y * 0.5f));
                        float n = big * 0.7f + small * 0.3f;
                        float tongue = rageTongue * (1f + 0.6f * slot.rageUpShare[i]);
                        // Au moins l'écart du tremblement tout autour (une langue fine ne laisse pas de trou)
                        float radius = Mathf.Max(rageShakePixels, rageRadius * Mathf.Max(0f, 1f + tongue * (n - 0.5f) * 2f));
                        float margin = radius - distance;
                        if (margin >= 0f)
                            color = margin < 1f ? rageEdgeColor : rageRimColor;
                    }
                    // Texture rangée du bas vers le haut
                    slot.ragePixels[(h - 1 - y) * w + x] = color;
                }
            slot.rageTexture.SetPixels32(slot.ragePixels);
            slot.rageTexture.Apply(false);
        }

        private static float Hash(float x, float y)
        {
            float v = Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        // Bruit de valeur lissé (le même que les shaders d'aura)
        private static float Noise(float x, float y)
        {
            float cx = Mathf.Floor(x), cy = Mathf.Floor(y);
            float fx = x - cx, fy = y - cy;
            float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
            float a = Hash(cx, cy), b = Hash(cx + 1f, cy), c = Hash(cx, cy + 1f), d = Hash(cx + 1f, cy + 1f);
            return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
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
