using DBZ.Items;
using UnityEngine;
using UIImage = UnityEngine.UI.Image;

namespace DBZ.UI
{
    // Compteur de zénis du HUD : une petite pièce suivie du total, en chiffres pixel art (sprites damage_digits).
    // Les images sont créées au lancement ; seul le changement de total (ZeniWallet.Changed) met l'affichage à jour.
    // À placer sur un objet enfant du Canvas du HUD (RectTransform) : 1 pixel de sprite = 1 unité d'interface
    public class ZeniCounter : MonoBehaviour
    {
        [Tooltip("Image de la pièce devant le nombre (32×32, pièce en bas de l'image)")]
        [SerializeField] private Sprite coinIcon;
        [Tooltip("Décalage de l'image de la pièce, en pixels, pour aligner la pièce visible sur le bas des chiffres")]
        [SerializeField] private Vector2Int coinIconOffset = new Vector2Int(-13, 0);
        [Tooltip("Largeur de la pièce visible, en pixels (les chiffres commencent après)")]
        [SerializeField] private int coinIconWidth = 6;

        [Tooltip("Sprites des chiffres 0 à 9, dans l'ordre")]
        [SerializeField] private Sprite[] digitSprites = new Sprite[10];
        [Tooltip("Écart entre deux chiffres, en pixels (négatif = les contours se chevauchent)")]
        [SerializeField] private int digitSpacing = -1;
        [Tooltip("Écart entre la pièce et le premier chiffre, en pixels")]
        [SerializeField] private int iconSpacing = 2;
        [Tooltip("Nombre maximal de chiffres affichés")]
        [SerializeField] private int maxDigits = 6;

        private UIImage[] digitImages;

        private void Awake()
        {
            var icon = CreateImage("Coin", coinIcon);
            icon.rectTransform.anchoredPosition = coinIconOffset;

            digitImages = new UIImage[maxDigits];
            for (int i = 0; i < maxDigits; i++)
                digitImages[i] = CreateImage($"Digit{i}", digitSprites[0]);
        }

        private void OnEnable()
        {
            ZeniWallet.Changed += Show;
            Show(ZeniWallet.Total);
        }

        private void OnDisable()
        {
            ZeniWallet.Changed -= Show;
        }

        private void Show(int total)
        {
            string text = Mathf.Clamp(total, 0, (int)Mathf.Pow(10, maxDigits) - 1).ToString();
            int x = coinIconWidth + iconSpacing;
            for (int i = 0; i < digitImages.Length; i++)
            {
                var image = digitImages[i];
                bool visible = i < text.Length;
                image.enabled = visible;
                if (!visible)
                    continue;

                Sprite sprite = digitSprites[text[i] - '0'];
                image.sprite = sprite;
                image.rectTransform.sizeDelta = sprite.rect.size;
                image.rectTransform.anchoredPosition = new Vector2(x, 0f);
                x += (int)sprite.rect.width + digitSpacing;
            }
        }

        // Image ancrée en bas à gauche de cet objet, à la taille du sprite (positions en pixels entiers)
        private UIImage CreateImage(string objectName, Sprite sprite)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var image = go.AddComponent<UIImage>();
            image.sprite = sprite;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            if (sprite != null)
                rect.sizeDelta = sprite.rect.size;
            return image;
        }
    }
}
