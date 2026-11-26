using UnityEngine;

namespace DBZ.Combat
{
    // Pierre posée sur un sprite (2026-11-26, Dabura) : remplace son matériau par une copie du matériau « DBZ/Stone »
    // et fait monter la pierre des pieds à la tête (SetProgress 0 → 1). End rend le matériau d'origine.
    // Classe simple (pas un composant), utilisée par Petrification (Goku) et DisappearOnDeath (mort de Dabura)
    public class StoneSkin
    {
        private static readonly int StoneLineId = Shader.PropertyToID("_StoneLine");
        private static readonly int OriginId = Shader.PropertyToID("_Origin");

        private readonly SpriteRenderer renderer;
        private readonly Material original;
        private readonly Material instance;

        public Material Material => instance;

        public StoneSkin(SpriteRenderer renderer, Material stoneMaterial)
        {
            this.renderer = renderer;
            original = renderer.sharedMaterial;
            instance = new Material(stoneMaterial);
            instance.SetVector(OriginId, renderer.transform.position);
            renderer.sharedMaterial = instance;
            SetProgress(0f);
        }

        // 0 = rien en pierre, 1 = tout ; la ligne suit le sprite affiché (bas = bas de son image)
        public void SetProgress(float progress)
        {
            Bounds bounds = renderer.bounds;
            float line = progress <= 0f ? bounds.min.y - 1f
                : progress >= 1f ? bounds.max.y + 1f
                : Mathf.Floor((bounds.min.y + bounds.size.y * progress) * 32f) / 32f;
            instance.SetFloat(StoneLineId, line);
        }

        public void End()
        {
            if (renderer != null)
                renderer.sharedMaterial = original;
            Object.Destroy(instance);
        }
    }
}
