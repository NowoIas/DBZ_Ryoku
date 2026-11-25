using UnityEngine;

namespace DBZ.Enemies
{
    // Anime par script une aura dessinée d'une seule image (aura de rage de C17 / C18, dessinée par l'util.) :
    // les bosses des côtés montent en continu, au pixel. Pour chaque rangée animée, on recopie les vrais pixels du bord
    // (liseré et ses crochets compris) d'une rangée plus basse de l'image, décalés pour garder l'enveloppe lisse de
    // la rangée d'arrivée ; l'intérieur est rempli de la couleur du milieu de la rangée. La pointe (rangées du haut)
    // et le bas ne bougent pas. Défilement sur toute la hauteur animée : boucle sans à-coup.
    // Pas un composant : utilisé par BossRage. L'image doit être lisible (Read/Write coché à l'import).
    public class DrawnAuraAnimator
    {
        private readonly Color32[] source;
        private readonly Color32[] pixels;
        private readonly int width, height, fixedTop, fixedBottom, band;
        private readonly int[] left, right;
        private readonly float[] smoothLeft, smoothRight;
        private int lastOffset = -1;

        public Texture2D Texture { get; }

        // fixedTopRows / fixedBottomRows : rangées jamais déplacées (comptées depuis le haut / le bas de l'image) ;
        // bandPixels : largeur du bord recopiée de chaque côté
        public DrawnAuraAnimator(Texture2D image, int fixedTopRows, int fixedBottomRows, int bandPixels)
        {
            width = image.width;
            height = image.height;
            fixedTop = fixedTopRows;
            fixedBottom = height - fixedBottomRows;
            band = bandPixels;
            source = new Color32[width * height];
            // Rangées rangées du haut vers le bas (Unity lit du bas vers le haut)
            Color32[] raw = image.GetPixels32();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    source[y * width + x] = raw[(height - 1 - y) * width + x];
            pixels = new Color32[width * height];

            left = new int[height];
            right = new int[height];
            for (int y = 0; y < height; y++)
            {
                left[y] = width;
                right[y] = -1;
                for (int x = 0; x < width; x++)
                {
                    if (source[y * width + x].a == 0)
                        continue;
                    left[y] = Mathf.Min(left[y], x);
                    right[y] = Mathf.Max(right[y], x);
                }
            }
            smoothLeft = new float[height];
            smoothRight = new float[height];
            for (int y = fixedTop; y < fixedBottom; y++)
            {
                float sumLeft = 0f, sumRight = 0f;
                int count = 0;
                for (int v = y - 4; v <= y + 4; v++)
                {
                    if (v < fixedTop || v >= fixedBottom || right[v] < 0)
                        continue;
                    sumLeft += left[v];
                    sumRight += right[v];
                    count++;
                }
                smoothLeft[y] = count > 0 ? sumLeft / count : left[y];
                smoothRight[y] = count > 0 ? sumRight / count : right[y];
            }

            Texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = image.name + " (animée)"
            };
            SetOffset(0);
        }

        // offset : de combien de pixels les bosses sont montées (boucle sur la hauteur animée)
        public void SetOffset(int offset)
        {
            int span = Mathf.Max(1, fixedBottom - fixedTop);
            offset = ((offset % span) + span) % span;
            if (offset == lastOffset)
                return;
            lastOffset = offset;

            System.Array.Clear(pixels, 0, pixels.Length);
            for (int y = 0; y < height; y++)
            {
                if (y < fixedTop || y >= fixedBottom || offset == 0)
                {
                    System.Array.Copy(source, y * width, pixels, y * width, width);
                    continue;
                }
                int src = fixedTop + (y - fixedTop + offset) % span;
                if (right[src] < 0)
                    continue;
                int dl = Mathf.RoundToInt(smoothLeft[y] - smoothLeft[src]);
                int dr = Mathf.RoundToInt(smoothRight[y] - smoothRight[src]);
                Color32 fill = source[src * width + (left[src] + right[src]) / 2];
                for (int x = left[src] + dl; x <= right[src] + dr; x++)
                    Put(x, y, fill);
                for (int x = left[src]; x < left[src] + band; x++)
                    if (source[src * width + x].a > 0)
                        Put(x + dl, y, source[src * width + x]);
                for (int x = right[src] - band + 1; x <= right[src]; x++)
                    if (x >= 0 && source[src * width + x].a > 0)
                        Put(x + dr, y, source[src * width + x]);
            }

            // Retour dans le sens de Unity (bas vers le haut)
            var flipped = new Color32[pixels.Length];
            for (int y = 0; y < height; y++)
                System.Array.Copy(pixels, y * width, flipped, (height - 1 - y) * width, width);
            Texture.SetPixels32(flipped);
            Texture.Apply(false);
        }

        private void Put(int x, int y, Color32 color)
        {
            if (x >= 0 && x < width)
                pixels[y * width + x] = color;
        }
    }
}
