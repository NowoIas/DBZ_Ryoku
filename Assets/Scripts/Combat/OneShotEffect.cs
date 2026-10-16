using UnityEngine;

namespace DBZ.Combat
{
    // Effet visuel joué une seule fois (impact, explosion...) : fait défiler ses sprites puis se détruit
    [RequireComponent(typeof(SpriteRenderer))]
    public class OneShotEffect : MonoBehaviour
    {
        [SerializeField] private Sprite[] frames;
        [SerializeField] private float framesPerSecond = 12f;

        private SpriteRenderer spriteRenderer;
        private float elapsed;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (frames.Length > 0)
                spriteRenderer.sprite = frames[0];
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            int index = (int)(elapsed * framesPerSecond);
            if (index >= frames.Length)
            {
                Destroy(gameObject);
                return;
            }
            spriteRenderer.sprite = frames[index];
        }
    }
}
