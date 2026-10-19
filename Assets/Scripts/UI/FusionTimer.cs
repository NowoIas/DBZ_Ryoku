using UnityEngine;
using UIImage = UnityEngine.UI.Image;

namespace DBZ.UI
{
    // Chrono de la fusion (Gotenks) : temps restant en minutes:secondes, chiffres rouges (sprites timer_digits),
    // caché sans fusion. Les images sont créées au lancement ; l'affichage n'est refait que quand la seconde change.
    // À placer sur un objet enfant du Canvas du HUD (RectTransform) : 1 pixel de sprite = 1 unité d'interface
    public class FusionTimer : MonoBehaviour
    {
        [Tooltip("Sprites des chiffres 0 à 9, dans l'ordre")]
        [SerializeField] private Sprite[] digitSprites = new Sprite[10];
        [Tooltip("Sprite des deux-points entre minutes et secondes")]
        [SerializeField] private Sprite colonSprite;
        [Tooltip("Écart entre deux caractères, en pixels (l'ombre des chiffres sert déjà d'espace)")]
        [SerializeField] private int digitSpacing = 0;
        [Tooltip("Joueur dont on affiche la fusion (vide = l'objet tagué Player)")]
        [SerializeField] private PlayerController player;

        // m:ss ou mm:ss
        private const int MaxCharacters = 5;
        private UIImage[] images;
        private int shownSeconds = int.MinValue;

        private void Awake()
        {
            images = new UIImage[MaxCharacters];
            for (int i = 0; i < MaxCharacters; i++)
                images[i] = CreateImage($"Char{i}");
            Show(-1);
        }

        private void Update()
        {
            if (player == null)
            {
                var tagged = GameObject.FindWithTag("Player");
                if (tagged != null)
                    player = tagged.GetComponent<PlayerController>();
            }

            float timeLeft = player != null ? player.FusionTimeLeft : -1f;
            int seconds = timeLeft < 0f ? -1 : Mathf.CeilToInt(timeLeft);
            if (seconds != shownSeconds)
                Show(seconds);
        }

        // seconds < 0 : chrono caché
        private void Show(int seconds)
        {
            shownSeconds = seconds;
            string text = seconds < 0 ? "" : $"{seconds / 60}:{seconds % 60:00}";
            int x = 0;
            for (int i = 0; i < images.Length; i++)
            {
                var image = images[i];
                bool visible = i < text.Length;
                image.enabled = visible;
                if (!visible)
                    continue;

                Sprite sprite = text[i] == ':' ? colonSprite : digitSprites[text[i] - '0'];
                image.sprite = sprite;
                image.rectTransform.sizeDelta = sprite.rect.size;
                image.rectTransform.anchoredPosition = new Vector2(x, 0f);
                x += (int)sprite.rect.width + digitSpacing;
            }
        }

        // Image ancrée en bas à gauche de cet objet (positions en pixels entiers)
        private UIImage CreateImage(string objectName)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var image = go.AddComponent<UIImage>();
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            return image;
        }
    }
}
