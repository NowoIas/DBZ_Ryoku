using DBZ.Player;
using UnityEngine;
using UIImage = UnityEngine.UI.Image;

namespace DBZ.UI
{
    // Cases de compétences du HUD : icône du personnage actuel (fiche CharacterData), filtre sombre qui descend
    // pendant la pause de la compétence, et temps restant centré en bas de la case (chiffres ui_digits) :
    // secondes à partir de 10 s (« 14 »), dixièmes en dessous (« 9.4 », « 0.3 »), sans arrondir vers le haut.
    // Chaque case est un RectTransform de la taille de l'intérieur du cadre (ancré en haut à gauche, 1 px = 1 unité)
    public class SkillBar : MonoBehaviour
    {
        [System.Serializable]
        public class Slot
        {
            [Tooltip("Compétence affichée dans cette case")]
            public SkillSlot skill;
            [Tooltip("Intérieur de la case dans le cadre")]
            public RectTransform box;
            [Tooltip("Image de l'icône (placée et dimensionnée par le script)")]
            public UIImage icon;
            [Tooltip("Filtre de la pause : Image Filled verticale, origine en bas")]
            public UIImage filter;
            [Tooltip("Icône calée en bas de la case (peut dépasser vers le haut, ex. passif) au lieu d'être centrée")]
            public bool alignBottom;

            [System.NonSerialized] public UIImage[] chars;
            [System.NonSerialized] public string shownText;
        }

        [SerializeField] private Slot[] slots = new Slot[0];
        [Tooltip("Sprites des chiffres 0 à 9, dans l'ordre (blancs, ombre noire)")]
        [SerializeField] private Sprite[] digitSprites = new Sprite[10];
        [Tooltip("Sprite du point des dixièmes")]
        [SerializeField] private Sprite dotSprite;
        [Tooltip("Joueur suivi (vide = l'objet tagué Player)")]
        [SerializeField] private PlayerController player;

        // « 9.4 » ou « 14 » : 3 caractères au plus (une case fait 14 à 16 px, un chiffre 5)
        private const int MaxChars = 3;
        private CharacterData shownCharacter;

        private void Awake()
        {
            foreach (var slot in slots)
            {
                slot.chars = new UIImage[MaxChars];
                for (int i = 0; i < MaxChars; i++)
                    slot.chars[i] = CreateChar(slot.box, i);
                slot.shownText = null;
                ShowTime(slot, 0f);
                if (slot.filter != null)
                    slot.filter.fillAmount = 0f;
            }
        }

        private void Update()
        {
            if (player == null)
            {
                var tagged = GameObject.FindWithTag("Player");
                if (tagged != null)
                    player = tagged.GetComponent<PlayerController>();
                if (player == null)
                    return;
            }

            // Changement de perso (F2, fusion, séparation) : nouvelles icônes
            if (player.character != shownCharacter)
            {
                shownCharacter = player.character;
                foreach (var slot in slots)
                    ShowIcon(slot, shownCharacter != null ? shownCharacter.GetSkillIcon(slot.skill) : null);
            }

            foreach (var slot in slots)
            {
                float left = player.GetCooldown(slot.skill, out float total);
                if (slot.filter != null)
                    slot.filter.fillAmount = total > 0f ? Mathf.Clamp01(left / total) : 0f;
                ShowTime(slot, left);
            }
        }

        // Icône à sa taille réelle, au pixel : centrée (arrondi vers le haut / la gauche) ou calée en bas
        private static void ShowIcon(Slot slot, Sprite sprite)
        {
            if (slot.icon == null)
                return;
            slot.icon.sprite = sprite;
            slot.icon.enabled = sprite != null;
            if (sprite == null)
                return;

            Vector2 size = sprite.rect.size;
            Vector2 boxSize = slot.box.rect.size;
            int x = Mathf.FloorToInt((boxSize.x - size.x) / 2f);
            int top = Mathf.FloorToInt((boxSize.y - size.y) / 2f);
            int bottom = slot.alignBottom ? 0 : Mathf.RoundToInt(boxSize.y - size.y) - top;

            var rt = slot.icon.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = size;
            rt.anchoredPosition = new Vector2(x, bottom);
        }

        // Temps restant centré en largeur et calé en bas de la case (au pixel) ; 0 = rien d'affiché
        private void ShowTime(Slot slot, float left)
        {
            string text = FormatTime(left);
            if (text == slot.shownText)
                return;
            slot.shownText = text;

            var sprites = new Sprite[text.Length];
            float width = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                sprites[i] = text[i] == '.' ? dotSprite : digitSprites[text[i] - '0'];
                if (sprites[i] == null)
                    continue;
                width += sprites[i].rect.width;
            }

            Vector2 boxSize = slot.box.rect.size;
            float x = Mathf.Floor((boxSize.x - width) / 2f);
            const float y = 0f;
            for (int i = 0; i < MaxChars; i++)
            {
                var image = slot.chars[i];
                Sprite sprite = i < sprites.Length ? sprites[i] : null;
                image.enabled = sprite != null;
                if (sprite == null)
                    continue;
                image.sprite = sprite;
                var rt = image.rectTransform;
                rt.sizeDelta = sprite.rect.size;
                rt.anchoredPosition = new Vector2(x, y);
                x += sprite.rect.width;
            }
        }

        // Tronqué (jamais arrondi vers le haut) : 14,7 s → « 14 », 9,46 s → « 9.4 », 0,38 s → « 0.3 »
        private static string FormatTime(float left)
        {
            if (left <= 0f)
                return "";
            if (left >= 10f)
                return Mathf.Min(Mathf.FloorToInt(left), 99).ToString();
            int tenths = Mathf.FloorToInt(left * 10f);
            return $"{tenths / 10}.{tenths % 10}";
        }

        private static UIImage CreateChar(RectTransform box, int index)
        {
            var go = new GameObject($"Char{index}", typeof(RectTransform));
            go.transform.SetParent(box, false);
            var rt = (RectTransform)go.transform;
            // Placé depuis le coin bas-gauche de la case
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            var image = go.AddComponent<UIImage>();
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }
    }
}
