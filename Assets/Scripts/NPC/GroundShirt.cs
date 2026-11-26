using System.Collections;
using UnityEngine;

namespace DBZ.NPC
{
    // T-shirt laissé au sol par un NPC aspiré (2026-11-26, sprites util.) : reste entier un moment, puis s'efface
    // par grains (pixels retirés au hasard, comme les traces de pas), puis l'objet est détruit.
    // Les pixels sont lus sur une copie faite par la carte graphique : l'image n'a pas besoin d'être lisible à l'import
    public class GroundShirt : MonoBehaviour
    {
        private const string SortingLayer = "background";
        // Au-dessus des Decor 1-3 et des Destructibles (4), sous les zénis (10)
        private const int SortingOrder = 9;

        public static void Spawn(Sprite sprite, Vector3 position, float stayDuration, float fadeDuration)
        {
            var go = new GameObject("NpcShirt");
            go.transform.position = position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = SortingLayer;
            sr.sortingOrder = SortingOrder;
            go.AddComponent<GroundShirt>().StartCoroutine(Run(go, sr, sprite, stayDuration, fadeDuration));
        }

        private static IEnumerator Run(GameObject go, SpriteRenderer sr, Sprite sprite, float stayDuration, float fadeDuration)
        {
            yield return new WaitForSeconds(stayDuration);

            Texture2D copy = ReadPixels(sprite);
            if (copy == null || fadeDuration <= 0f)
            {
                Object.Destroy(go);
                yield break;
            }
            Color32[] pixels = copy.GetPixels32();
            // Pixels visibles, dans un ordre tiré au hasard
            var order = new System.Collections.Generic.List<int>();
            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i].a > 0)
                    order.Add(i);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            Vector2 pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
            sr.sprite = Sprite.Create(copy, new Rect(0, 0, copy.width, copy.height), pivot, sprite.pixelsPerUnit);

            int removed = 0;
            for (float t = 0f; t < fadeDuration; t += Time.deltaTime)
            {
                int target = Mathf.FloorToInt(order.Count * t / fadeDuration);
                if (target > removed)
                {
                    for (; removed < target; removed++)
                        pixels[order[removed]] = new Color32(0, 0, 0, 0);
                    copy.SetPixels32(pixels);
                    copy.Apply();
                }
                yield return null;
            }
            Object.Destroy(sr.sprite);
            Object.Destroy(copy);
            Object.Destroy(go);
        }

        private static Texture2D ReadPixels(Sprite sprite)
        {
            Rect r = sprite.textureRect;
            int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            var rt = RenderTexture.GetTemporary(sprite.texture.width, sprite.texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(sprite.texture, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            copy.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }
    }
}
