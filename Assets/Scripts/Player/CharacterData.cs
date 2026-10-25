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

        [Tooltip("Un Override Controller par palier de transformation, dans l'ordre (SSJ, SSJ3...). Liste vide = pas de transformation ; "
            + "case vide = palier sauté (Gogeta : SSJ, vide, SSJ4, Blue)")]
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
        [Tooltip("Frappe aussi les 4 cases en diagonale (8 cases autour de lui au lieu de 4 ; Vegeto)")]
        public bool spinHitsDiagonals;
        [Tooltip("PV payés à chaque toupie (personnage qui paie en PV, Piccolo)")]
        [Min(0f)] public float spinHealthCost = 5f;
        [Tooltip("Ki payé à chaque toupie (personnage qui paie en Ki, Vegeto)")]
        [Min(0f)] public float spinKiCost = 5f;
        [Tooltip("PV regagnés : part du total des dégâts infligés (0,5 = 50 %)")]
        [Min(0f)] public float spinHealRatio = 0.5f;
        [Tooltip("Pause après une toupie (secondes, 0 = aucune)")]
        [Min(0f)] public float spinCooldown = 0f;
        [Tooltip("Vitesse de la toupie (multiplie celle des clips spin_* : 1 = 12 img/s, 1,25 = 15 img/s). Modifiable en Play ; la lame suit")]
        [Min(0.05f)] public float spinSpeed = 1.25f;

        [Header("Toupie : lame d'énergie par-dessus le personnage (Vegeto)")]
        [Tooltip("Lame pointée vers la droite, poignée au centre de l'image (tournée par code pour haut / gauche / bas). Vide = pas de lame")]
        public Sprite spinBladeStraight;
        [Tooltip("Lame pointée en haut à droite (tournée par code pour les 3 autres diagonales)")]
        public Sprite spinBladeDiagonal;
        [Tooltip("Rotation entre deux images de la toupie (45 = un huitième de tour dans le sens inverse des aiguilles d'une montre ; "
            + "-45 = sens des aiguilles). La 1re image pointe dans la direction où il regarde")]
        public float spinBladeStepDegrees = 45f;
        [Tooltip("Directions où la lame passe derrière le personnage (bras vers le fond)")]
        public BladeDirections spinBladeBehindDirections = BladeDirections.Up | BladeDirections.UpLeft | BladeDirections.UpRight;
        [Tooltip("Décalages d'affichage en pixels, par direction de la lame (modifiables en Play, gardés après l'arrêt)")]
        public Vector2Int spinBladeRightOffsetPixels;
        public Vector2Int spinBladeUpRightOffsetPixels;
        public Vector2Int spinBladeUpOffsetPixels;
        public Vector2Int spinBladeUpLeftOffsetPixels;
        public Vector2Int spinBladeLeftOffsetPixels;
        public Vector2Int spinBladeDownLeftOffsetPixels;
        public Vector2Int spinBladeDownOffsetPixels;
        public Vector2Int spinBladeDownRightOffsetPixels;

        // Direction 0 = droite, puis un huitième de tour à chaque fois dans le sens inverse des aiguilles d'une montre
        public Vector2Int SpinBladeOffsetPixels(int direction)
        {
            switch (direction)
            {
                case 0: return spinBladeRightOffsetPixels;
                case 1: return spinBladeUpRightOffsetPixels;
                case 2: return spinBladeUpOffsetPixels;
                case 3: return spinBladeUpLeftOffsetPixels;
                case 4: return spinBladeLeftOffsetPixels;
                case 5: return spinBladeDownLeftOffsetPixels;
                case 6: return spinBladeDownOffsetPixels;
                default: return spinBladeDownRightOffsetPixels;
            }
        }

        public bool SpinBladeBehind(int direction)
        {
            return (spinBladeBehindDirections & (BladeDirections)(1 << direction)) != 0;
        }

        [Header("Touche E : coup d'épée au lieu du rayon (Trunks)")]
        [Tooltip("Frappe la case devant (état Animator Sword, planche s2) ; critique possible comme un coup normal (même chance, dégâts ×2)")]
        public bool swordOnE;
        [Min(0f)] public float swordDamage = 20f;
        [Tooltip("Pause après un coup d'épée (secondes, 0 = aucune)")]
        [Min(0f)] public float swordCooldown = 0f;
        [Tooltip("Directions où le personnage passe devant les ennemis pendant le coup d'épée (l'épée déborde de sa case). "
            + "Ailleurs (bas) : ordre normal, l'ennemi en dessous reste devant")]
        public DBZ.World.CellSides swordInFrontDirections = DBZ.World.CellSides.Up | DBZ.World.CellSides.Left | DBZ.World.CellSides.Right;
        [Tooltip("Saignement infligé par l'épée (secondes, 0 = aucun ; un nouveau coup la fait repartir de zéro)")]
        [Min(0f)] public float bleedDuration = 0f;
        [Tooltip("Dégâts de chaque tic de saignement")]
        [Min(0f)] public float bleedDamage = 3f;
        [Tooltip("Temps entre deux tics (secondes)")]
        [Min(0.05f)] public float bleedInterval = 0.5f;
        [Tooltip("Icône de l'état, sur la case au-dessus de la cible")]
        public Sprite bleedIcon;

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

        [Header("Touche R : poing du dragon (Gogeta)")]
        [Tooltip("Appui sur R : le poing part, mord au bout puis revient (état Animator SpecialShot, planche s3). "
            + "Coût, dégâts, pause et images réglés dans le prefab. Vide = autre technique R")]
        public DBZ.Combat.DragonFist dragonFistPrefab;

        [Header("Touche é : fusion (Goten → Gotenks, Goku / Vegeta → Gogeta)")]
        [Tooltip("Personnage obtenu par la fusion (animation fusion, planche tr2), depuis une forme qui a son clip fusion. "
            + "Espace (en forme normale) ou fin du chrono : retour à ce personnage-ci. Vide = pas de fusion")]
        public CharacterData fusionCharacter;
        [Tooltip("Ki payé pour fusionner")]
        [Min(0f)] public float fusionKiCost = 50f;
        [Tooltip("Durée de la fusion (secondes), affichée par un chrono dans le HUD")]
        [Min(1f)] public float fusionDuration = 180f;
        [Tooltip("Séparation jouée par le personnage fusionné avant de revenir à ce personnage-ci (planche tr0<perso> du fusionné). "
            + "Vide = clip fusion du fusionné")]
        public AnimationClip separationClip;

        [Header("Touche R : coup de poing qui étourdit (Goten)")]
        [Tooltip("Frappe la case devant (état Animator SpecialShot, planche s3), critique possible ; la cible est étourdie "
            + "(immobile, n'attaque plus, figée sur son image, icône au-dessus)")]
        public bool stunPunchOnR;
        [Min(0f)] public float stunPunchDamage = 15f;
        [Tooltip("Durée de l'étourdissement (secondes ; un nouveau coup la fait repartir de zéro)")]
        [Min(0f)] public float stunDuration = 2f;
        [Tooltip("Icône de l'état, sur la case au-dessus de la cible")]
        public Sprite stunIcon;
        [Min(0f)] public float stunPunchKiCost = 10f;
        [Tooltip("Pause après le coup (secondes, 0 = aucune)")]
        [Min(0f)] public float stunPunchCooldown = 1f;
        [Tooltip("Moment du coup dans l'animation (0 = début, 1 = fin ; 0,25 = 2e image sur 4)")]
        [Range(0f, 1f)] public float stunPunchHitTime = 0.25f;

        [Header("Touche E : tir au lieu du rayon (Gotenks : Volleyball)")]
        [Tooltip("Un appui : projectile lancé comme un Ki Blast (état Animator SpecialShot, planche s2). "
            + "Dégâts, portée, images et effet (ballon) réglés dans le prefab. Vide = rayon (Beam Prefab)")]
        public Projectile projectileOnE;
        [Tooltip("Ki payé à chaque tir")]
        [Min(0f)] public float projectileOnEKiCost = 15f;
        [Tooltip("Pause après un tir (secondes, 0 = aucune)")]
        [Min(0f)] public float projectileOnECooldown = 1f;

        [Header("Touche R : devient un fantôme (Gotenks : Ghost)")]
        [Tooltip("Animations du fantôme (Override Controller). La touche R joue la transformation (clip transform, planche s3), "
            + "puis le joueur devient le fantôme : il se déplace comme en vol, à la vitesse du vol, sans attaquer. "
            + "Espace : retour (clip detransform du fantôme). Vide = autre technique R")]
        public AnimatorOverrideController ghostController;
        [Tooltip("Fantôme propre à chaque palier, dans l'ordre de Transformed Controllers (son retour joue le s3 de la forme). "
            + "Case vide ou absente = Ghost Controller")]
        public AnimatorOverrideController[] transformedGhostControllers = new AnimatorOverrideController[0];
        [Tooltip("Ki payé en devenant fantôme (ensuite, aucune consommation ni limite de durée)")]
        [Min(0f)] public float ghostKiCost = 20f;
    }

    // Les 8 directions de la lame de la toupie (même ordre que CharacterData.SpinBladeOffsetPixels), combinables
    [System.Flags]
    public enum BladeDirections
    {
        None = 0,
        Right = 1 << 0,
        UpRight = 1 << 1,
        Up = 1 << 2,
        UpLeft = 1 << 3,
        Left = 1 << 4,
        DownLeft = 1 << 5,
        Down = 1 << 6,
        DownRight = 1 << 7,
    }
}
