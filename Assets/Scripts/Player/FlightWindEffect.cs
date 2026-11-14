using UnityEngine;

namespace DBZ.Player
{
    // Sillage d'air autour du joueur quand il avance en vol (shader "DBZ/Flight Wind") : crée au lancement un
    // carré centré sur lui (enfant FlightWind) et envoie au shader l'image affichée du joueur (les traits suivent
    // sa silhouette), la direction du vol et l'intensité.
    // Rien en vol sur place, ni pour le fantôme de Gotenks.
    [RequireComponent(typeof(PlayerController))]
    public class FlightWindEffect : MonoBehaviour
    {
        private static readonly int DirectionId = Shader.PropertyToID("_Direction");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int SpriteRectId = Shader.PropertyToID("_SpriteRect");
        private static readonly int SpritePivotId = Shader.PropertyToID("_SpritePivot");
        private static readonly int SizePixelsId = Shader.PropertyToID("_SizePixels");

        [Tooltip("Décoché : pas de sillage d'air")]
        [SerializeField] private bool showsWind = true;
        [Tooltip("Matériau du shader DBZ/Flight Wind (couleur, nombre, longueur, vitesse des traits)")]
        [SerializeField] private Material windMaterial;
        [Tooltip("Côté du carré où passent les traits, centré sur le joueur (cases)")]
        [SerializeField] private float sizeCells = 3f;
        [Tooltip("Durée d'apparition quand le joueur se met à avancer (secondes)")]
        [SerializeField] private float fadeInDuration = 0.1f;
        [Tooltip("Durée de disparition quand il s'arrête (secondes)")]
        [SerializeField] private float fadeOutDuration = 0.15f;
        [SerializeField] private string sortingLayerName = "foreground";
        [Tooltip("Ordre d'affichage : 51 = juste devant le joueur en vol (50)")]
        [SerializeField] private int sortingOrder = 51;

        private const float PixelsPerUnit = 32f;

        private PlayerController controller;
        private SpriteRenderer playerRenderer;
        private MeshRenderer windRenderer;
        private Mesh mesh;
        private MaterialPropertyBlock block;
        private Vector2 lastPosition;
        private Vector2 direction = Vector2.right;
        private float intensity;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            playerRenderer = GetComponent<SpriteRenderer>();
            if (windMaterial == null || playerRenderer == null)
                return;

            var child = new GameObject("FlightWind");
            child.transform.SetParent(transform, false);
            float half = sizeCells * 0.5f;
            mesh = new Mesh { name = "FlightWind" };
            mesh.vertices = new[] { new Vector3(-half, -half), new Vector3(half, -half), new Vector3(-half, half), new Vector3(half, half) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            windRenderer = child.AddComponent<MeshRenderer>();
            windRenderer.sharedMaterial = windMaterial;
            windRenderer.sortingLayerName = sortingLayerName;
            windRenderer.sortingOrder = sortingOrder;
            windRenderer.enabled = false;
            block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            lastPosition = transform.position;
        }

        private void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
        }

        // Après le déplacement du joueur (Update)
        private void LateUpdate()
        {
            if (windRenderer == null)
                return;

            Vector2 position = transform.position;
            Vector2 delta = position - lastPosition;
            lastPosition = position;

            // Plus d'une case d'un coup = déplacé par une porte, pas un vol
            bool isFlyingForward = showsWind && controller.IsFlying && !controller.IsGhost
                && delta.sqrMagnitude > 0.000001f && delta.sqrMagnitude < 1f;
            if (isFlyingForward)
            {
                direction = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
                    ? new Vector2(Mathf.Sign(delta.x), 0f) : new Vector2(0f, Mathf.Sign(delta.y));
            }

            float duration = isFlyingForward ? fadeInDuration : fadeOutDuration;
            float target = isFlyingForward ? 1f : 0f;
            intensity = duration > 0f ? Mathf.MoveTowards(intensity, target, Time.deltaTime / duration) : target;

            Sprite sprite = playerRenderer.sprite;
            windRenderer.enabled = intensity > 0f && sprite != null && playerRenderer.enabled;
            if (!windRenderer.enabled)
                return;
            // Image affichée du joueur : sa case dans la planche et son pivot, en pixels de la planche
            Rect rect = sprite.rect;
            block.SetTexture(MainTexId, sprite.texture);
            block.SetVector(SpriteRectId, new Vector4(rect.xMin, rect.yMin, rect.xMax, rect.yMax));
            block.SetVector(SpritePivotId, new Vector4(rect.xMin + sprite.pivot.x, rect.yMin + sprite.pivot.y));
            block.SetVector(DirectionId, direction);
            block.SetFloat(IntensityId, intensity);
            block.SetFloat(SizePixelsId, sizeCells * PixelsPerUnit);
            windRenderer.SetPropertyBlock(block);
        }
    }
}
