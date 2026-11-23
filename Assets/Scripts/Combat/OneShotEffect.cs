using UnityEngine;

namespace DBZ.Combat
{
    // Effet visuel joué une seule fois (impact, explosion...) : fait défiler ses sprites puis se détruit
    // Mode « une image par direction » : frames = haut, bas, gauche, droite ; seule l'image du sens du tir
    // s'affiche (SetDirection), pendant displayDuration, sans animation
    [RequireComponent(typeof(SpriteRenderer))]
    public class OneShotEffect : MonoBehaviour
    {
        [SerializeField] private Sprite[] frames;
        [SerializeField] private float framesPerSecond = 12f;

        [Tooltip("Coché : frames = haut, bas, gauche, droite ; une seule image, celle du sens du tir")]
        [SerializeField] private bool oneFramePerDirection;
        [Tooltip("Mode une image par direction : durée d'affichage (s)")]
        [SerializeField] private float displayDuration = 0.1f;

        private SpriteRenderer spriteRenderer;
        private float elapsed;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (frames.Length > 0)
                spriteRenderer.sprite = frames[0];
        }

        // Sens du tir qui a provoqué l'effet (diagonale : l'axe dominant, horizontal à égalité)
        public void SetDirection(Vector2 direction)
        {
            if (!oneFramePerDirection || frames.Length < 4)
                return;
            int index;
            if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
                index = direction.x < 0f ? 2 : 3;
            else
                index = direction.y > 0f ? 0 : 1;
            spriteRenderer.sprite = frames[index];
        }

        private void Update()
        {
            elapsed += Time.deltaTime;

            if (oneFramePerDirection)
            {
                if (elapsed >= displayDuration)
                    Destroy(gameObject);
                return;
            }

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
