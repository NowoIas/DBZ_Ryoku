using UnityEngine;

namespace DBZ.Combat
{
    // Nombre de dégâts affiché au-dessus d'une cible : il monte, s'efface puis se détruit.
    // Les chiffres sont des sprites en pixel art (pas de police) pour rester nets avec la Pixel Perfect Camera.
    public class DamagePopup : MonoBehaviour
    {
        [Tooltip("Sprites des chiffres 0 à 9, dans l'ordre (pivot en bas à gauche)")]
        [SerializeField] private Sprite[] digitSprites = new Sprite[10];

        [Tooltip("Sprites des chiffres 0 à 9 des coups critiques, dans l'ordre (vide = chiffres normaux)")]
        [SerializeField] private Sprite[] criticalDigitSprites = new Sprite[10];

        [Tooltip("Sprites des chiffres 0 à 9 des soins (verts), dans l'ordre (vide = chiffres normaux)")]
        [SerializeField] private Sprite[] healDigitSprites = new Sprite[10];

        [Tooltip("Écart entre deux chiffres, en pixels (négatif = le chiffre de droite recouvre le bord du précédent)")]
        [SerializeField] private int digitSpacing = -1;

        [Tooltip("Teinte des chiffres (blanc = couleurs de la planche)")]
        [SerializeField] private Color normalColor = Color.white;

        [Tooltip("Durée totale d'affichage, en secondes")]
        [SerializeField] private float duration = 0.8f;

        [Tooltip("Hauteur de montée, en cases")]
        [SerializeField] private float riseDistance = 0.5f;
        [SerializeField] private float criticalRiseDistance = 0.75f;

        [Tooltip("Moment (0 à 1) où le nombre commence à s'effacer")]
        [Range(0f, 1f)]
        [SerializeField] private float fadeStart = 0.5f;

        [SerializeField] private string sortingLayerName = "foreground";
        [SerializeField] private int sortingOrder = 100;

        private SpriteRenderer[] digitRenderers;
        private Vector3 startPosition;
        private float rise;
        private float elapsed;
        private Color color;
        private float pixelsPerUnit = 32f;

        // isHeal : PV rendus, chiffres verts (même mouvement qu'un coup normal)
        public void Show(int amount, bool isCritical, bool isHeal = false)
        {
            startPosition = transform.position;
            rise = isCritical ? criticalRiseDistance : riseDistance;
            color = normalColor;

            // Critique et soin : leur propre planche de chiffres (plus de teinte)
            Sprite[] sprites = digitSprites;
            if (isHeal && HasAllDigits(healDigitSprites))
                sprites = healDigitSprites;
            else if (isCritical && HasAllDigits(criticalDigitSprites))
                sprites = criticalDigitSprites;

            string text = Mathf.Max(0, amount).ToString();
            digitRenderers = new SpriteRenderer[text.Length];

            if (sprites.Length > 0 && sprites[0] != null)
                pixelsPerUnit = sprites[0].pixelsPerUnit;

            // Largeur totale en pixels, pour centrer le nombre sur la cible
            int totalWidth = 0;
            foreach (char c in text)
                totalWidth += (int)sprites[c - '0'].rect.width + digitSpacing;
            totalWidth -= digitSpacing;

            // Positions en pixels entiers : chaque chiffre tombe pile sur la grille de pixels
            int x = -totalWidth / 2;
            for (int i = 0; i < text.Length; i++)
            {
                Sprite sprite = sprites[text[i] - '0'];

                var digit = new GameObject($"Digit{i}");
                digit.transform.SetParent(transform, false);
                digit.transform.localPosition = new Vector3(x / pixelsPerUnit, 0f, 0f);

                var sr = digit.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = color;
                sr.sortingLayerName = sortingLayerName;
                // Chaque chiffre au-dessus du précédent : celui de droite recouvre toujours le bord du chiffre de gauche
                sr.sortingOrder = sortingOrder + i;
                digitRenderers[i] = sr;

                x += (int)sprite.rect.width + digitSpacing;
            }
        }

        private static bool HasAllDigits(Sprite[] sprites)
        {
            return sprites != null && sprites.Length == 10 && sprites[0] != null;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Montée rapide au début puis ralentie (ease-out)
            float offset = rise * (1f - (1f - t) * (1f - t));
            Vector3 position = startPosition + Vector3.up * offset;
            // Arrondi au pixel pour éviter que les chiffres tremblent en montant
            position.x = Mathf.Round(position.x * pixelsPerUnit) / pixelsPerUnit;
            position.y = Mathf.Round(position.y * pixelsPerUnit) / pixelsPerUnit;
            transform.position = position;

            float alpha = t < fadeStart ? 1f : 1f - (t - fadeStart) / (1f - fadeStart);
            Color c = color;
            c.a *= alpha;
            foreach (var sr in digitRenderers)
                sr.color = c;

            if (t >= 1f)
                Destroy(gameObject);
        }
    }
}
