using System.Collections.Generic;
using DBZ.Combat;
using UnityEngine;

namespace DBZ.Enemies
{
    // Rage des boss en duo (C17 / C18) : quand l'écart de PV entre les deux dépasse enterGap (en part des PV max),
    // celui qui en a le plus devient enragé (il protège son partenaire) : bonus de dégâts et de vitesse, aura rouge.
    // La rage cesse quand l'écart redescend sous exitGap (plus bas que enterGap : pas d'allumage / extinction
    // en boucle autour du seuil). Partenaire K.O. : le survivant est enragé jusqu'à la fin.
    // Le partenaire est trouvé tout seul : l'autre BossRage de la scène avec le même pairName (le plus proche).
    // Aura en 2 couches :
    //  - derrière (2026-11-26, demande util.) : flamme rouge calculée par le shader "DBZ/Rage Flame" depuis l'image
    //    affichée (bouge entièrement, suit la marche et les coups), bas coupé au bas de l'ombre ;
    //    l'ancienne aura dessinée animée par script (drawnAura + DrawnAuraAnimator) reste possible si drawnAura est rempli ;
    //  - devant elle : shader "DBZ/Rage Aura" sur un carré centré sur le boss, qui lit l'image affichée (sa silhouette) :
    //    aura fine violette collée aux pixels autour du sprite + traits de Ki et étincelles qui montent.
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(EnemyController))]
    public class BossRage : MonoBehaviour
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int SpriteRectId = Shader.PropertyToID("_SpriteRect");
        private static readonly int SpritePivotId = Shader.PropertyToID("_SpritePivot");
        private static readonly int SizePixelsId = Shader.PropertyToID("_SizePixels");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int FlipId = Shader.PropertyToID("_Flip");

        private static readonly List<BossRage> active = new List<BossRage>();

        [Tooltip("Nom du duo : deux boss avec le même nom sont partenaires")]
        [SerializeField] private string pairName = "C17C18";
        [Tooltip("Écart de PV (part des PV max) au-delà duquel le plus en forme devient enragé")]
        [SerializeField, Range(0f, 1f)] private float enterGap = 0.3f;
        [Tooltip("Écart sous lequel la rage cesse (plus bas que enterGap)")]
        [SerializeField, Range(0f, 1f)] private float exitGap = 0.25f;
        [Tooltip("Dégâts × cette valeur pendant la rage (coups et tirs)")]
        [SerializeField] private float damageMultiplier = 1.5f;
        [Tooltip("Vitesse de poursuite × cette valeur pendant la rage")]
        [SerializeField] private float speedMultiplier = 1.3f;

        [Header("Aura")]
        [Tooltip("Matériau du shader DBZ/Rage Aura (couleurs, hauteur des flammes, rythme)")]
        [SerializeField] private Material auraMaterial;
        [Tooltip("Côté du carré de l'aura, centré sur le boss (cases)")]
        [SerializeField] private float auraSizeCells = 2f;
        [Tooltip("Durée d'apparition / de disparition de l'aura (secondes)")]
        [SerializeField] private float auraFadeDuration = 0.2f;

        [Header("Flamme (derrière)")]
        [Tooltip("Matériau du shader DBZ/Rage Flame (couleurs, taille et vitesse des flammes) ; vide = pas de flamme")]
        [SerializeField] private Material flameMaterial;
        [Tooltip("Côté du carré de la flamme, centré sur le boss (cases)")]
        [SerializeField] private float flameSizeCells = 3f;

        [Header("Aura dessinée (ancienne, derrière)")]
        [Tooltip("Image de l'aura (une seule), Read/Write coché à l'import ; vide = pas d'aura dessinée")]
        [SerializeField] private Texture2D drawnAura;
        [Tooltip("Montée des bosses des côtés (pixels par seconde)")]
        [SerializeField] private float drawnRiseSpeed = 12f;
        [Tooltip("Rangées du haut de l'image jamais déplacées (pointe, cornes)")]
        [SerializeField] private int drawnFixedTopRows = 7;
        [Tooltip("Rangées du bas de l'image jamais déplacées")]
        [SerializeField] private int drawnFixedBottomRows = 3;
        [Tooltip("Largeur du bord recopiée de chaque côté (pixels)")]
        [SerializeField] private int drawnBandPixels = 6;
        [Tooltip("Hauteur du bas de l'ombre du boss sous son centre (pixels) : le bas de l'aura y est calé")]
        [SerializeField] private int drawnBottomPixels = 16;

        private const float PixelsPerUnit = 32f;

        private Health health;
        private EnemyController controller;
        private SpriteRenderer spriteRenderer;
        private BossRage partner;
        private bool hadPartner;
        private bool isEnraged;
        private MeshRenderer auraRenderer;
        private MeshRenderer flameRenderer;
        private Mesh mesh;
        private Mesh flameMesh;
        private MaterialPropertyBlock block;
        private float auraIntensity;
        private DrawnAuraAnimator drawnAnimator;
        private SpriteRenderer drawnRenderer;
        private Sprite drawnSprite;

        public bool IsEnraged => isEnraged;
        public Health Health => health;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => active.Clear();

        private void Awake()
        {
            health = GetComponent<Health>();
            controller = GetComponent<EnemyController>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            CreateAura();
        }

        private void OnEnable() => active.Add(this);
        private void OnDisable()
        {
            active.Remove(this);
            // DisappearOnDeath arrête ce composant à la mort : sans ça l'aura resterait figée sur la dernière image
            // vivante pendant l'animation de mort
            HideAura();
        }

        private void HideAura()
        {
            auraIntensity = 0f;
            if (auraRenderer != null)
                auraRenderer.enabled = false;
            if (flameRenderer != null)
                flameRenderer.enabled = false;
            if (drawnRenderer != null)
                drawnRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
            if (flameMesh != null)
                Destroy(flameMesh);
            if (drawnSprite != null)
                Destroy(drawnSprite);
            if (drawnAnimator != null)
                Destroy(drawnAnimator.Texture);
        }

        private void Update()
        {
            if (partner == null && !hadPartner)
                FindPartner();

            bool wanted = isEnraged;
            if (health.IsDead)
                wanted = false;
            else if (hadPartner && (partner == null || partner.Health.IsDead))
                wanted = true;
            else if (partner != null)
            {
                // Écart en part des PV max (les deux n'ont pas forcément le même maximum)
                float gap = Ratio - partner.Ratio;
                wanted = isEnraged ? gap > exitGap : gap > enterGap;
            }
            SetEnraged(wanted);
        }

        private float Ratio => health.Max > 0f ? health.Current / health.Max : 0f;

        private void FindPartner()
        {
            float bestDistance = float.MaxValue;
            foreach (BossRage other in active)
            {
                if (other == this || other.pairName != pairName || (other.partner != null && other.partner != this))
                    continue;
                float distance = ((Vector2)(other.transform.position - transform.position)).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    partner = other;
                }
            }
            if (partner != null)
            {
                hadPartner = true;
                partner.partner = this;
                partner.hadPartner = true;
            }
        }

        private void SetEnraged(bool value)
        {
            if (value == isEnraged)
                return;
            isEnraged = value;
            controller.DamageMultiplier = value ? damageMultiplier : 1f;
            controller.SpeedMultiplier = value ? speedMultiplier : 1f;
        }

        private void CreateAura()
        {
            if (spriteRenderer == null)
                return;
            if (drawnAura != null)
            {
                if (!drawnAura.isReadable)
                    Debug.LogWarning($"{name} : l'aura dessinée {drawnAura.name} doit avoir Read/Write coché à l'import");
                else
                {
                    drawnAnimator = new DrawnAuraAnimator(drawnAura, drawnFixedTopRows, drawnFixedBottomRows, drawnBandPixels);
                    // Pivot au milieu du bas : le bas de l'aura sur le bas de l'ombre
                    drawnSprite = Sprite.Create(drawnAnimator.Texture, new Rect(0, 0, drawnAura.width, drawnAura.height),
                        new Vector2(0.5f, 0f), PixelsPerUnit);
                    var back = new GameObject("RageAuraDrawn");
                    back.transform.SetParent(transform, false);
                    back.transform.localPosition = new Vector3(0f, -drawnBottomPixels / PixelsPerUnit, 0f);
                    drawnRenderer = back.AddComponent<SpriteRenderer>();
                    drawnRenderer.sprite = drawnSprite;
                    drawnRenderer.enabled = false;
                }
            }
            block = new MaterialPropertyBlock();
            if (flameMaterial != null)
                flameRenderer = CreateQuad("RageFlame", flameSizeCells, flameMaterial, out flameMesh);
            if (auraMaterial != null)
                auraRenderer = CreateQuad("RageAura", auraSizeCells, auraMaterial, out mesh);
        }

        // Carré centré sur le boss, qui porte un shader lisant l'image affichée
        private MeshRenderer CreateQuad(string objectName, float sizeCells, Material material, out Mesh quad)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            float half = sizeCells * 0.5f;
            quad = new Mesh { name = objectName };
            quad.vertices = new[] { new Vector3(-half, -half), new Vector3(half, -half), new Vector3(-half, half), new Vector3(half, half) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            child.AddComponent<MeshFilter>().sharedMesh = quad;
            var quadRenderer = child.AddComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = material;
            quadRenderer.enabled = false;
            return quadRenderer;
        }

        // Après l'Animator : l'aura suit l'image affichée
        private void LateUpdate()
        {
            if (auraRenderer == null && drawnRenderer == null && flameRenderer == null)
                return;
            if (health.IsDead)
            {
                HideAura();
                return;
            }
            float target = isEnraged ? 1f : 0f;
            auraIntensity = auraFadeDuration > 0f
                ? Mathf.MoveTowards(auraIntensity, target, Time.deltaTime / auraFadeDuration) : target;

            Sprite sprite = spriteRenderer.sprite;
            if (drawnRenderer != null)
            {
                drawnRenderer.enabled = auraIntensity > 0f && spriteRenderer.enabled;
                if (drawnRenderer.enabled)
                {
                    // Sous l'aura fine (ordre − 1) et le boss
                    drawnRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
                    drawnRenderer.sortingOrder = spriteRenderer.sortingOrder - 2;
                    drawnRenderer.color = new Color(1f, 1f, 1f, auraIntensity);
                    drawnAnimator.SetOffset(Mathf.FloorToInt(Time.time * drawnRiseSpeed));
                }
            }
            // Flamme sous l'aura fine (ordre − 2), aura fine juste derrière le boss (ordre − 1)
            UpdateQuad(flameRenderer, sprite, flameSizeCells, 2);
            UpdateQuad(auraRenderer, sprite, auraSizeCells, 1);
        }

        // Envoie l'image affichée au shader : l'effet suit chaque image (marche, coups) ; jamais dessiné sur le boss
        private void UpdateQuad(MeshRenderer quadRenderer, Sprite sprite, float sizeCells, int ordersBehind)
        {
            if (quadRenderer == null)
                return;
            quadRenderer.enabled = auraIntensity > 0f && sprite != null && spriteRenderer.enabled;
            if (!quadRenderer.enabled)
                return;
            quadRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
            quadRenderer.sortingOrder = spriteRenderer.sortingOrder - ordersBehind;

            Rect rect = sprite.rect;
            block.Clear();
            block.SetTexture(MainTexId, sprite.texture);
            block.SetVector(SpriteRectId, new Vector4(rect.xMin, rect.yMin, rect.xMax, rect.yMax));
            block.SetVector(SpritePivotId, new Vector4(rect.xMin + sprite.pivot.x, rect.yMin + sprite.pivot.y));
            block.SetFloat(SizePixelsId, sizeCells * PixelsPerUnit);
            block.SetFloat(IntensityId, auraIntensity);
            block.SetFloat(FlipId, spriteRenderer.flipX ? 1f : 0f);
            quadRenderer.SetPropertyBlock(block);
        }
    }
}
