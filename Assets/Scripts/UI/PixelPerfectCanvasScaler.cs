using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace DBZ.UI
{
    // Agrandit l'interface du même facteur entier que la Pixel Perfect Camera (×4 en 1920×1080),
    // pour que les pixels de l'interface aient la même taille que ceux du jeu
    [RequireComponent(typeof(CanvasScaler))]
    public class PixelPerfectCanvasScaler : MonoBehaviour
    {
        // Vide = celle de la caméra principale (le prefab HUD marche ainsi dans toutes les scènes sans réglage)
        [SerializeField] private PixelPerfectCamera pixelPerfectCamera;

        private CanvasScaler canvasScaler;

        private void Awake()
        {
            canvasScaler = GetComponent<CanvasScaler>();
            if (pixelPerfectCamera == null && Camera.main != null)
                pixelPerfectCamera = Camera.main.GetComponent<PixelPerfectCamera>();
        }

        private void LateUpdate()
        {
            if (pixelPerfectCamera != null)
                canvasScaler.scaleFactor = Mathf.Max(1, pixelPerfectCamera.pixelRatio);
        }
    }
}
