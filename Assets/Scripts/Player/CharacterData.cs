using UnityEngine;

namespace DBZ.Player
{
    // Fiche d'un personnage jouable (Goku, Vegeta...) : ce qui change d'un personnage à l'autre sur le prefab Player.
    // Le prefab Player lit la fiche choisie au lancement ; ajouter un personnage = créer une fiche (Create > DBZ > Character)
    [CreateAssetMenu(menuName = "DBZ/Character", fileName = "NewCharacter")]
    public class CharacterData : ScriptableObject
    {
        [Tooltip("Nom affiché (messages de la console)")]
        public string displayName;

        [Tooltip("Animations de la forme normale : Player.controller (Goku) ou un Override Controller de Player.controller")]
        public RuntimeAnimatorController animatorController;

        [Tooltip("Un Override Controller par palier de transformation, dans l'ordre (SSJ, SSJ3...). Vide = pas de transformation")]
        public AnimatorOverrideController[] transformedControllers = new AnimatorOverrideController[0];

        [Tooltip("Transformation jouée au rythme du retour (Detransform Speed du PlayerController) au lieu de Transform Frames Per Second "
            + "(Piccolo : tr1 et tr0 ont 4 images, même durée)")]
        public bool transformAtDetransformSpeed;
        [Tooltip("Même chose, palier par palier, dans l'ordre de Transformed Controllers (Gohan ado : seulement SSJ → Sayaman). "
            + "Case décochée ou absente = Transform Frames Per Second (sauf si la case ci-dessus est cochée)")]
        public bool[] transformAtDetransformSpeedPerStage = new bool[0];

        // Vrai si la transformation vers ce palier (1 = premier) se joue au rythme du retour
        public bool TransformsAtDetransformSpeed(int stage)
        {
            if (transformAtDetransformSpeed)
                return true;
            int index = stage - 1;
            return transformAtDetransformSpeedPerStage != null && index >= 0 && index < transformAtDetransformSpeedPerStage.Length
                && transformAtDetransformSpeedPerStage[index];
        }

        [Tooltip("Image affichée avant la première animation (attente, de face)")]
        public Sprite startSprite;

        [Header("Techniques (vide = celle du PlayerController : Kamehameha, Genkidama)")]
        [Tooltip("Rayon de la touche E (Goku : Kamehameha ; Vegeta : Final Flash)")]
        public DBZ.Combat.KamehamehaBeam beamPrefab;
        [Tooltip("Rayon de la touche E propre à chaque palier, dans l'ordre de Transformed Controllers (ex. réglages d'alignement d'une forme). "
            + "Case vide ou absente = Beam Prefab")]
        public DBZ.Combat.KamehamehaBeam[] transformedBeamPrefabs = new DBZ.Combat.KamehamehaBeam[0];
        [Tooltip("Technique chargée de la touche R (Goku : Genkidama ; Vegeta : Big Bang)")]
        public DBZ.Combat.ChargedTechnique chargedTechniquePrefab;
        [Tooltip("Rayon de la touche R, à la place d'une technique chargée (Piccolo : Makankosappo). Vide = Charged Technique Prefab")]
        public DBZ.Combat.KamehamehaBeam chargedBeamPrefab;

        [Header("Techniques payées en PV au lieu du Ki (Piccolo)")]
        [Tooltip("Ki Blast et rayons coûtent des PV ; la jauge de Ki reste vide. Une attaque est refusée si elle ferait tomber à 0 PV")]
        public bool paysWithHealth;
        [Min(0f)] public float kiBlastHealthCost = 2f;
        [Tooltip("Rayon (E ou R) : PV payés au lancement de la charge")]
        [Min(0f)] public float beamHealthCost = 10f;
        [Tooltip("Rayon (E ou R) : PV par seconde tant que la touche est tenue")]
        [Min(0f)] public float beamHealthPerSecond = 5f;
        [Tooltip("Transformation (Piccolo : Noweight) : PV payés au lancement ; ensuite elle ne consomme rien et dure jusqu'au retour volontaire")]
        [Min(0f)] public float transformHealthCost = 10f;

        [Header("Touche E : toupie au lieu du rayon (Piccolo)")]
        [Tooltip("Il tourne sur lui-même (état Animator Spin) et frappe les 4 cases autour de lui (dégâts du coup normal)")]
        public bool spinOnE;
        [Tooltip("PV payés à chaque toupie")]
        [Min(0f)] public float spinHealthCost = 5f;
        [Tooltip("PV regagnés : part du total des dégâts infligés (0,5 = 50 %)")]
        [Min(0f)] public float spinHealRatio = 0.5f;
        [Tooltip("Pause après une toupie (secondes, 0 = aucune)")]
        [Min(0f)] public float spinCooldown = 0f;

        [Header("Touche E : coup d'épée au lieu du rayon (Trunks)")]
        [Tooltip("Frappe la case devant (état Animator Sword, planche s2) ; critique possible comme un coup normal (même chance, dégâts ×2)")]
        public bool swordOnE;
        [Min(0f)] public float swordDamage = 20f;
        [Tooltip("Pause après un coup d'épée (secondes, 0 = aucune)")]
        [Min(0f)] public float swordCooldown = 0f;

        [Header("Touche E désactivée (Trunks enfant)")]
        [Tooltip("La touche E ne fait rien (personnage sans technique E)")]
        public bool noTechniqueOnE;

        [Header("Touche R : tir au lieu d'une technique chargée (Trunks : Burning Blast)")]
        [Tooltip("Projectile lancé comme un Ki Blast (état Animator SpecialShot, planche s3), R tenue = tirs en boucle. "
            + "Dégâts, portée et images réglés dans le prefab. Vide = Charged Technique Prefab")]
        public Projectile specialProjectilePrefab;
        [Tooltip("Ki payé à chaque tir")]
        [Min(0f)] public float specialKiCost = 15f;
        [Tooltip("Pause après un tir (secondes, 0 = aucune)")]
        [Min(0f)] public float specialCooldown = 0.5f;
    }
}
