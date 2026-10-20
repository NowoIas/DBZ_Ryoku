using System.Collections;
using System.Collections.Generic;
using DBZ.Combat;
using DBZ.Items;
using DBZ.Player;
using DBZ.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class PlayerController : MonoBehaviour
{
    [FormerlySerializedAs("movespeed")]
    public float moveSpeed = 4f;
    public float runSpeed = 8f;

    [Range(0f, 1f)]
    public float criticalChance = 0.2f;
    public float meleeDamage = 10f;
    // Un coup critique multiplie les dégâts du corps à corps
    public float criticalMultiplier = 2f;
    // Moment du coup dans l'animation d'attaque (0 = début, 1 = fin)
    [Range(0f, 1f)]
    public float meleeHitTime = 0.5f;
    // Rayon de la zone touchée au centre de la case devant le joueur
    public float meleeHitRadius = 0.4f;

    // Pause après une attaque, avant de pouvoir relancer la même (chaque touche a la sienne : un coup ne bloque pas le Ki Blast).
    // 0 = enchaînement direct, comme avant. Goku peut se déplacer pendant la pause (sauf touche en pause tenue : il attend sur place)
    [Header("Pause entre deux attaques (secondes)")]
    [Tooltip("Après un coup normal")]
    [Min(0f)]
    public float meleeCooldown = 0f;
    [Tooltip("Après un coup critique")]
    [Min(0f)]
    public float criticalCooldown = 0f;
    [Tooltip("Après un Ki Blast")]
    [Min(0f)]
    public float kiBlastCooldown = 0f;

    public Projectile projectilePrefab;

    // Le rayon part quand l'Animator passe de la charge (état Kamehameha) au tir (état KamehamehaFire)
    public KamehamehaBeam kamehamehaPrefab;
    // Technique de la touche R (maintenue), par défaut le Genkidama : boule au-dessus de Goku, visée, lancée au relâchement.
    // Kamehameha et Genkidama servent aux personnages dont la fiche ne donne pas les leurs
    [FormerlySerializedAs("genkidamaPrefab")]
    public ChargedTechnique chargedTechniquePrefab;
    // Distance (en cases) devant le joueur où apparaît le projectile
    public float projectileSpawnOffset = 0.75f;

    // Personnage joué : sa fiche donne ses animations et ses transformations (le reste du prefab est commun à tous)
    [Header("Personnage")]
    [Tooltip("Personnage joué au lancement (fiche dans Assets/Characters)")]
    public CharacterData character;
    [Tooltip("Pour les tests : F2 passe au personnage suivant de cette liste (forme normale, au sol), gardé d'une scène à l'autre")]
    public CharacterData[] testCharacters;

    // Un Animator Override Controller par palier de transformation, dans l'ordre (SSJ, SSJ3...), pris dans la fiche du personnage.
    // L'animation de transformation jouée est le clip "transform" de la forme actuelle. Vide = pas de transformation
    private AnimatorOverrideController[] transformedControllers = new AnimatorOverrideController[0];
    // Appui sur la touche de test (F2), appliqué dès que le joueur est libre
    private bool characterSwitchRequested;

    [Tooltip("Vitesse des animations de transformation (normal → SSJ, SSJ → SSJ3), en images par seconde (clips faits à 4)")]
    [Min(0.1f)]
    public float transformFramesPerSecond = 4f;
    // Vitesse à laquelle les clips de transformation ont été créés : transformSpeed = transformFramesPerSecond / celle-ci
    private const float TransformClipFramesPerSecond = 4f;

    [Tooltip("Multiplicateur de vitesse des retours à la forme normale (1 = vitesse des clips : SSJ 6 img/s, SSJ3 4 img/s ; 2 = deux fois plus vite)")]
    [Min(0.1f)]
    public float detransformSpeed = 1f;
    // Vitesse des clips de retour (tr0) : sert à jouer une transformation au même rythme (fiche : transformAtDetransformSpeed)
    private const float DetransformClipFramesPerSecond = 6f;

    // Calques de collision (CollisionMap, un par niveau de hauteur) et calque Niveaux (LevelMap) : trouvés
    // automatiquement dans la scène, pour que le prefab Player marche dans toutes les scènes sans réglage.
    // Seul le calque de collision du niveau du joueur compte pour ses déplacements
    // Ordre d'affichage du joueur (sorting layer "player") par niveau : au sol (0), il passe sous le pont
    // (calque Pont, ordre 3) ; en haut (4), par-dessus. Tranches de LevelMap.SortingOrdersPerLevel ordres par niveau
    public int[] sortingOrderPerLevel = { 0, 4 };

    // Layer(s) physique(s) des objets qui bloquent : cibles (punching-ball, ennemis) et Destructibles.
    // Elles bloquent le passage et reçoivent les coups. Le terrain, lui, vient des calques de collision
    public LayerMask solidObjectsLayer;
    // Rayon de la zone testée au centre de la case visée (1 = une case entière)
    public float collisionCheckRadius = 0.3f;

    // Mémoire des appuis : un appui fait pendant un déplacement ou une attaque (même déjà relâché)
    // est exécuté dès que le joueur est libre, s'il date de moins de inputBufferTime secondes.
    // 0,5 s couvre une case de marche (0,25 s) et un coup (0,5 s)
    [Tooltip("Durée pendant laquelle un appui fait pendant un déplacement ou une attaque reste en mémoire (secondes)")]
    public float inputBufferTime = 0.5f;

    // Vol (touche Fly) : Goku passe au-dessus de tout (collisions, rebords, objets, ennemis), seules les limites de la carte
    // (tilemap Background) l'arrêtent. Il ne peut ni attaquer ni être touché ; il faut atterrir pour combattre
    [Header("Vol")]
    [Tooltip("Vitesse en vol, en cases par seconde")]
    public float flySpeed = 6f;
    [Tooltip("Affichage en vol : sorting layer et ordre (au-dessus de tout, feuillage compris)")]
    public string flyingSortingLayer = "foreground";
    public int flyingSortingOrder = 50;
    [Tooltip("Niveau d'arrivée en atterrissant sur une case du calque Pont (on se pose sur le pont, pas dessous)")]
    public int bridgeLevel = 1;
    [Tooltip("Vol interdit pendant ce temps après avoir infligé ou reçu des dégâts (secondes)")]
    [Min(0f)]
    public float flyCombatLockout = 15f;

    // Mort (PV à 0) : Goku tombe (animation Death) pendant que l'écran s'assombrit, puis part dans l'Autre Monde
    // (même transition qu'une porte, fondu au noir plus long), PV et Ki pleins, forme normale
    [Header("Mort")]
    [Tooltip("Scène où Goku réapparaît après sa mort")]
    public string respawnScene = "OtherWorld";
    [Tooltip("Il ressort à côté de la porte de Respawn Scene qui mène à cette scène (et a ce Link Name)")]
    public string respawnDoorTarget = "Overworld";
    public string respawnDoorLink = "";
    [Tooltip("Durée du fondu au noir de la mort, en secondes : il commence en même temps que la chute (0,5 s)")]
    public float deathFadeDuration = 0.8f;

    [Header("Portes")]
    [Tooltip("Durée de chaque fondu d'une porte (au noir, puis retour de l'image), en secondes ; aussi le retour de l'image après la mort")]
    public float doorFadeDuration = 0.15f;

    // Niveau de hauteur actuel (0 = sol)
    private int level;

    // Pour les ennemis : niveau de hauteur (HeightLevel.Air en vol), et case occupée (celle visée pendant un pas,
    // pour qu'ils n'y entrent pas)
    public int Level => isFlying ? HeightLevel.Air : level;
    // En vol, les ennemis ne le repèrent pas
    public bool IsFlying => isFlying;
    public Vector3 OccupiedCell => isMoving ? moveTarget : transform.position;
    private bool isMoving;
    // Case visée par le déplacement en cours
    private Vector3 moveTarget;
    // Distance restante quand le joueur arrive sur une case au milieu d'une image :
    // reportée sur la case suivante pour garder une vitesse régulière en enchaînant
    private float carriedDistance;
    private bool isRunning;
    private bool isAttacking;
    private bool isDead;
    // Fin de la pause de chaque touche d'attaque : coups (normal ou critique), Ki Blast, technique E, technique R
    private float nextMeleeTime;
    private float nextKiBlastTime;
    private float nextBeamTime;
    private float nextChargedTime;
    private bool isFlying;
    // Sorting layer au sol, retrouvé à l'atterrissage
    private string groundSortingLayer;
    // Limites de la carte pour le vol : une case sans tuile sur ce calque est hors de la carte
    private UnityEngine.Tilemaps.Tilemap mapTilemap;
    // Calque Pont : atterrir sur une de ses cases pose Goku sur le pont (bridgeLevel)
    private UnityEngine.Tilemaps.Tilemap bridgeTilemap;
    // Rayon du Kamehameha en cours (arrêté si Goku meurt pendant le tir)
    private KamehamehaBeam activeBeam;
    // Dernier rayon lancé, gardé jusqu'à sa disparition (traînée comprise) : il règle l'ordre d'affichage du joueur
    private KamehamehaBeam sortingBeam;
    // Coup d'épée en cours dans une direction où il passe devant les ennemis
    private bool isSwordInFront;
    // Technique R en charge (annulée si le joueur meurt)
    private ChargedTechnique activeGenkidama;
    // Techniques E et R du personnage joué (sa fiche, sinon celles du PlayerController)
    private KamehamehaBeam beamPrefab;
    private ChargedTechnique chargedPrefab;
    // Rayon de la touche R à la place de la technique chargée (Piccolo : Makankosappo), sinon null
    private KamehamehaBeam chargedBeamPrefab;
    // Piccolo : techniques payées en PV (fiche du personnage), et toupie sur la touche E
    private bool paysWithHealth;
    private bool spinOnE;
    // Trunks : coup d'épée sur la touche E, tir (Burning Blast) sur la touche R (null = technique chargée ou rayon)
    private bool swordOnE;
    private bool stunPunchOnR;
    private Projectile specialProjectilePrefab;
    // Gogeta : poing du dragon sur la touche R (null sinon), et celui en cours (supprimé si le joueur meurt)
    private DragonFist dragonFistPrefab;
    private DragonFist activeDragonFist;
    // Gotenks : tir Volleyball sur la touche E (null sinon), fantôme sur la touche R (null sinon)
    private Projectile projectileOnE;
    private AnimatorOverrideController ghostController;
    // Fantôme : se déplace comme en vol (isFlying vrai aussi), sans attaquer ; Espace = retour
    private bool isGhost;
    public bool PaysWithHealth => paysWithHealth;
    private Vector2 facing = Vector2.down;
    // Directions actuellement tenues, dans l'ordre d'appui : la dernière de la liste est prioritaire
    private readonly List<Vector2> heldDirections = new List<Vector2>();

    // Dernier appui mémorisé (direction et action à part), avec son heure
    private enum BufferedAction { None, Attack, KiBlast, Kamehameha, Transform, Detransform, Fly, Fusion }
    private Vector2 bufferedDirection;
    private float bufferedDirectionTime;
    private BufferedAction bufferedAction;
    private float bufferedActionTime;

    private static readonly int IdleState = Animator.StringToHash("Idle");
    private static readonly int KamehamehaTrigger = Animator.StringToHash("kamehameha");
    private static readonly int KamehamehaFireState = Animator.StringToHash("KamehamehaFire");
    private static readonly int FlyState = Animator.StringToHash("Fly");
    private static readonly int DeathState = Animator.StringToHash("Death");
    private static readonly int GenkidamaState = Animator.StringToHash("Genkidama");
    private static readonly int SpinState = Animator.StringToHash("Spin");
    private static readonly int SwordState = Animator.StringToHash("Sword");
    private static readonly int SpecialShotState = Animator.StringToHash("SpecialShot");
    private static readonly int FusionState = Animator.StringToHash("Fusion");
    // Nom du clip de base de l'état Fusion (clé des Override Controllers)
    private const string FusionClipName = "fusion";
    private static readonly int TransformSpeed = Animator.StringToHash("transformSpeed");
    private static readonly int DetransformSpeed = Animator.StringToHash("detransformSpeed");
    private static readonly int SpinSpeed = Animator.StringToHash("spinSpeed");

    private Animator animator;
    private SpriteRenderer spriteRenderer;
    // Lame d'énergie de la toupie (Vegeto) : enfant créé au lancement, affiché seulement pendant la toupie
    private SpriteRenderer spinBlade;
    private bool isSpinning;
    // Angle de la 1re image de la toupie (direction où il regardait au lancement)
    private float spinStartAngle;
    private RuntimeAnimatorController baseController;
    private PlayerKi ki;
    private Health health;
    // Niveau de hauteur vu par les autres (ennemis, techniques ennemies) : tenu à jour par SetLevel
    private HeightLevel heightLevel;
    // Actions définies dans Assets/Settings/InputSystem_Actions.inputactions (touches remappables)
    private InputSystem_Actions controls;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        // Personnage choisi par la touche de test (gardé d'une scène à l'autre et à la mort), sinon celui du prefab
        if (PlayerState.Character != null)
            character = PlayerState.Character;
        // Avant ApplyCharacter, qui règle le Ki selon la fiche
        ki = GetComponent<PlayerKi>();
        ApplyCharacter();
        health = GetComponent<Health>();
        heightLevel = GetComponent<HeightLevel>();
        groundSortingLayer = spriteRenderer.sortingLayerName;
        controls = new InputSystem_Actions();

        var blade = new GameObject("SpinBlade");
        blade.transform.SetParent(transform, false);
        spinBlade = blade.AddComponent<SpriteRenderer>();
        spinBlade.enabled = false;

        // On aligne le joueur sur le centre d'une case, pour que les tests de collision tombent juste
        var pos = transform.position;
        transform.position = new Vector3(Mathf.Floor(pos.x) + 0.5f, Mathf.Floor(pos.y) + 0.5f, pos.z);

        SetLevel(0);
    }

    // Dans Start et non Awake : le calque Niveaux et les portes doivent s'être inscrits (leur OnEnable) avant qu'on les interroge
    private void Start()
    {
        // Arrivée par une porte : on ressort de la porte d'arrivée, avec les PV, le Ki et la forme d'avant
        if (SceneTransition.TryTakeArrival(out var arrival))
        {
            PlaceAtArrival(arrival);
            if (PlayerState.TryRestore(health, ki, out int formLevel, out bool wasGhost))
            {
                if (formLevel > 0 && formLevel <= transformedControllers.Length)
                    SwitchController(transformedControllers[formLevel - 1]);
                if (wasGhost && ghostController != null)
                    EnterGhost();
            }
        }

        // Niveau de départ : celui de la case de départ si elle est peinte, sinon le sol
        SetLevel(LevelMap.LevelAfterEntering(transform.position, 0));

        foreach (var candidate in FindObjectsByType<UnityEngine.Tilemaps.Tilemap>())
        {
            if (candidate.gameObject.scene != gameObject.scene)
                continue;
            if (candidate.name == "Background")
                mapTilemap = candidate;
            else if (candidate.name == "Pont")
                bridgeTilemap = candidate;
        }
    }

    // Porte vers un autre endroit de la même scène (grotte...) : appelé par SceneTransition, sans rechargement
    public void ArriveInSameScene(SceneTransition.Arrival arrival)
    {
        PlaceAtArrival(arrival);
        SetLevel(LevelMap.LevelAfterEntering(transform.position, 0));
    }

    // On ressort sur la case juste après la porte d'arrivée, dans le sens où l'on marchait en entrant
    // (ou dans le sens forcé sur cette porte)
    private void PlaceAtArrival(SceneTransition.Arrival arrival)
    {
        isMoving = false;
        carriedDistance = 0f;
        var door = Door.FindArrival(arrival);
        if (door != null)
        {
            Vector2 exit = door.ExitVector(arrival.Direction);
            transform.position = new Vector3(door.CellCenter.x, door.CellCenter.y, transform.position.z) + (Vector3)exit;
            Face(exit);
        }
        else
        {
            string link = string.IsNullOrEmpty(arrival.LinkName) ? "" : $" (lien « {arrival.LinkName} »)";
            Debug.LogWarning($"Aucune porte ne ramène vers « {arrival.FromScene} »{link} dans cette scène : le joueur reste à sa place.");
        }
    }

    private void SetLevel(int newLevel)
    {
        level = newLevel;
        if (heightLevel != null)
            heightLevel.Level = isFlying ? HeightLevel.Air : level;
        if (!isFlying && level < sortingOrderPerLevel.Length)
            spriteRenderer.sortingOrder = sortingOrderPerLevel[level];
    }

    private void OnEnable()
    {
        controls.Player.Enable();
        health.Died += OnDied;
        health.Damaged += OnDamaged;
    }

    private void OnDisable()
    {
        controls.Player.Disable();
        health.Died -= OnDied;
        health.Damaged -= OnDamaged;
    }

    // Coup reçu : compte comme un combat (vol interdit pendant flyCombatLockout secondes)
    private void OnDamaged(float amount, bool isCritical)
    {
        PlayerCombat.RegisterFight();
    }

    private void OnDestroy()
    {
        controls.Dispose();
    }

    // Un pas d'une case dans cette direction : calque de collision du niveau actuel (cases pleines, murs,
    // sens uniques), puis objets qui bloquent (cibles, ennemis, Destructibles) à ce même niveau
    private bool CanMove(Vector2 direction)
    {
        if (!CollisionMap.CanMoveOnLevel(transform.position, direction, level))
            return false;

        var targetPos = transform.position + (Vector3)direction;
        return HeightLevel.FirstOnLevel(Physics2D.OverlapCircleAll(targetPos, collisionCheckRadius, solidObjectsLayer), level) == null;
    }

    private void Update()
    {
        // Mort : plus aucune action, la coroutine Death s'occupe de la suite
        if (isDead)
            return;

        var actions = controls.Player;

        // Mis à jour à chaque image, même pendant un déplacement, pour ne rater aucun appui
        UpdateHeldDirection(actions.MoveUp, Vector2.up);
        UpdateHeldDirection(actions.MoveDown, Vector2.down);
        UpdateHeldDirection(actions.MoveLeft, Vector2.left);
        UpdateHeldDirection(actions.MoveRight, Vector2.right);

        isRunning = actions.Sprint.IsPressed();

        // Chaque appui est mémorisé, même pendant un déplacement ou une attaque : le plus récent l'emporte
        BufferPress(actions.Attack, BufferedAction.Attack);
        BufferPress(actions.KiBlast, BufferedAction.KiBlast);
        BufferPress(actions.Kamehameha, BufferedAction.Kamehameha);
        BufferPress(actions.Transform, BufferedAction.Transform);
        BufferPress(actions.Detransform, BufferedAction.Detransform);
        BufferPress(actions.Fly, BufferedAction.Fly);
        BufferPress(actions.Fusion, BufferedAction.Fusion);
        // Touche de test, hors des contrôles du jeu (pas de remappage) : à retirer avec un vrai choix du personnage
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
            characterSwitchRequested = true;

        // Le déplacement avance ici, avant la décision : en arrivant sur une case, le joueur peut
        // repartir dans la même image, sans repasser par l'Idle (pas de pause entre deux cases)
        if (isMoving)
            Advance((isFlying ? flySpeed : isRunning ? runSpeed : moveSpeed) * Time.deltaTime);

        // Pendant un changement de scène (porte), le joueur ne fait plus rien
        if (!isMoving && !isAttacking && !SceneTransition.IsTransitioning)
        {
            BufferedAction buffered = IsBuffered(bufferedActionTime) ? bufferedAction : BufferedAction.None;

            // IsPressed (et non WasPressedThisFrame) : garder la touche enfoncée enchaîne les attaques ;
            // un appui mémorisé compte comme une touche tenue, une seule fois
            bool attackHeld = actions.Attack.IsPressed() || buffered == BufferedAction.Attack;
            // Sans assez de Ki, la touche de Ki Blast ne fait rien
            bool rangedAttackHeld = (actions.KiBlast.IsPressed() || buffered == BufferedAction.KiBlast) && CanAffordKiBlast();
            // Personnage sans technique E (Trunks enfant) : la touche ne fait rien
            bool kamehamehaHeld = (actions.Kamehameha.IsPressed() || buffered == BufferedAction.Kamehameha)
                && (character == null || !character.noTechniqueOnE)
                && (spinOnE ? CanAffordSpin()
                    : swordOnE || (projectileOnE != null ? ki.CanSpend(character.projectileOnEKiCost) : CanAffordBeam()));
            // Pas de mémoire d'appui : la touche doit être tenue (un appui déjà relâché annulerait la charge et perdrait le Ki)
            bool genkidamaHeld = actions.Genkidama.IsPressed()
                && (stunPunchOnR ? ki.CanSpend(character.stunPunchKiCost)
                    : ghostController != null ? ki.CanSpend(character.ghostKiCost)
                    : dragonFistPrefab != null ? ki.CanSpend(dragonFistPrefab.KiCost)
                    : specialProjectilePrefab != null ? ki.CanSpend(character.specialKiCost)
                    : chargedBeamPrefab != null ? CanAffordBeam() : ki.CanFireGenkidama);
            bool transformPressed = buffered == BufferedAction.Transform;
            bool detransformPressed = buffered == BufferedAction.Detransform;
            bool flyPressed = buffered == BufferedAction.Fly;
            bool fusionPressed = buffered == BufferedAction.Fusion;

            // Une direction tapotée puis relâchée (pendant le déplacement ou l'attaque) passe avant celle tenue, pour un pas
            Vector2 direction = heldDirections.Count > 0 ? heldDirections[heldDirections.Count - 1] : Vector2.zero;
            if (bufferedDirection != Vector2.zero && IsBuffered(bufferedDirectionTime))
                direction = bufferedDirection;

            // Les appuis mémorisés ont servi à cette décision (ou ne sont plus possibles, faute de Ki) : on les oublie.
            // Sauf une attaque appuyée pendant sa propre pause : gardée pour la fin de cette pause
            bool meleeOnCooldown = Time.time < nextMeleeTime;
            bool kiBlastOnCooldown = Time.time < nextKiBlastTime;
            bool beamOnCooldown = Time.time < nextBeamTime;
            bool chargedOnCooldown = Time.time < nextChargedTime;
            bool keepBufferedAttack = (buffered == BufferedAction.Attack && meleeOnCooldown)
                || (buffered == BufferedAction.KiBlast && kiBlastOnCooldown)
                || (buffered == BufferedAction.Kamehameha && beamOnCooldown);
            if (!keepBufferedAttack)
                bufferedAction = BufferedAction.None;
            bufferedDirection = Vector2.zero;

            // Changement de personnage (test) : seulement en forme normale et au sol, sinon l'appui est oublié
            if (characterSwitchRequested)
            {
                characterSwitchRequested = false;
                if (!isFlying && ki.CurrentForm == PlayerKi.Form.Normal)
                    SwitchCharacter();
            }

            // En vol : seulement se déplacer ou atterrir (pas d'attaque, de technique ni de transformation)
            if (isFlying)
            {
                // Fantôme : Espace le fait redevenir Gotenks (la touche de vol ne fait rien)
                if (isGhost ? detransformPressed : flyPressed)
                {
                    if (isGhost)
                        TryEndGhost();
                    else
                        TryLand();
                }
                else if (direction != Vector2.zero)
                {
                    Face(direction);
                    if (CanFlyTo(direction))
                    {
                        moveTarget = transform.position + (Vector3)direction;
                        isMoving = true;
                        Advance(carriedDistance);
                    }
                }
            }
            // Fin de la fusion, en forme normale : Espace ou chrono écoulé (en SSJ, Espace ou le chrono font d'abord
            // revenir en forme normale, ci-dessous)
            else if (IsFused && ki.CurrentForm == PlayerKi.Form.Normal
                     && (detransformPressed || Time.time >= PlayerState.FusionEndTime))
            {
                StartCoroutine(Separation());
            }
            // Retour à la forme normale : volontaire, forcé quand le Ki est épuisé (après l'atterrissage si Goku vole),
            // ou chrono de la fusion écoulé
            else if (ki.CurrentForm == PlayerKi.Form.Transformed
                     && (detransformPressed || ki.IsDepleted || (IsFused && Time.time >= PlayerState.FusionEndTime)))
            {
                StartCoroutine(Detransformation());
            }
            else if (kamehamehaHeld)
            {
                // La direction tenue au moment de l'appui oriente le tir ; elle est ensuite bloquée
                if (direction != Vector2.zero)
                    Face(direction);
                // Pendant la pause de la technique, il attend sur place (comme pour les coups)
                if (!beamOnCooldown)
                    StartCoroutine(spinOnE ? Spin() : swordOnE ? Sword() : projectileOnE != null ? ProjectileOnE()
                        : Kamehameha(CurrentBeamPrefab(), controls.Player.Kamehameha, true));
            }
            else if (genkidamaHeld)
            {
                // Comme le Kamehameha : la direction tenue à l'appui oriente la visée, puis Goku est figé
                if (direction != Vector2.zero)
                    Face(direction);
                if (!chargedOnCooldown)
                    StartCoroutine(stunPunchOnR ? StunPunch()
                        : ghostController != null ? GhostTransformation()
                        : dragonFistPrefab != null ? DragonFistAttack()
                        : specialProjectilePrefab != null ? SpecialShot()
                        : chargedBeamPrefab != null ? Kamehameha(chargedBeamPrefab, controls.Player.Genkidama, false) : Genkidama());
            }
            else if (attackHeld || rangedAttackHeld)
            {
                // Entre deux attaques (et pendant la pause), une direction tenue fait seulement tourner le personnage
                if (direction != Vector2.zero)
                    Face(direction);

                // Le coup passe en premier ; s'il est en pause, le Ki Blast part s'il est tenu et prêt.
                // Sinon (touche tenue en pause), il attend sur place
                if (attackHeld && !meleeOnCooldown)
                {
                    bool isCritical = Random.value < criticalChance;
                    float damage = isCritical ? meleeDamage * criticalMultiplier : meleeDamage;
                    StartCoroutine(MeleeAttack(isCritical, damage));
                }
                else if (rangedAttackHeld && !kiBlastOnCooldown)
                {
                    StartCoroutine(KiBlast());
                }
            }
            else if (transformPressed && NextTransformationStage() > 0 && ki.CanTransformTo(NextTransformationStage())
                     && (!paysWithHealth || health.CanSpend(character.transformHealthCost)))
            {
                StartCoroutine(Transformation());
            }
            // Fusion (Goten → Gotenks, Goku / Vegeta → Gogeta) : en forme normale, ou transformée si la forme a sa propre
            // animation de fusion (Goten SSJ → Gotenks SSJ), au sol
            else if (fusionPressed && character != null && character.fusionCharacter != null
                     && (ki.CurrentForm == PlayerKi.Form.Normal
                         || (ki.CurrentForm == PlayerKi.Form.Transformed && FormHasOwnFusionClip()))
                     && ki.CanSpend(character.fusionKiCost))
            {
                StartCoroutine(Fusion());
            }
            else if (flyPressed && PlayerCombat.SecondsSinceFight >= flyCombatLockout)
            {
                TakeOff();
            }
            else if (direction != Vector2.zero)
            {
                Face(direction);

                // Si la case est bloquée, le personnage se tourne mais ne bouge pas
                if (CanMove(direction))
                {
                    moveTarget = transform.position + (Vector3)direction;
                    isMoving = true;
                    Advance(carriedDistance);
                }
            }
        }

        // Pas enchaîné tout de suite : le reste de distance est perdu (arrêt, attaque...)
        carriedDistance = 0f;

        animator.SetBool("isMoving", isMoving);
        animator.SetBool("isRunning", isRunning);
    }

    // Une direction qui vient d'être appuyée passe en fin de liste (prioritaire) ;
    // une direction relâchée est retirée, ce qui redonne la main à la précédente encore tenue
    private void UpdateHeldDirection(InputAction action, Vector2 direction)
    {
        if (action.WasPressedThisFrame())
        {
            bufferedDirection = direction;
            bufferedDirectionTime = Time.time;
        }

        bool pressed = action.IsPressed();
        if (pressed && (action.WasPressedThisFrame() || !heldDirections.Contains(direction)))
        {
            heldDirections.Remove(direction);
            heldDirections.Add(direction);
        }
        else if (!pressed)
        {
            heldDirections.Remove(direction);
        }
    }

    private void BufferPress(InputAction action, BufferedAction buffered)
    {
        if (action.WasPressedThisFrame())
        {
            bufferedAction = buffered;
            bufferedActionTime = Time.time;
        }
    }

    private bool IsBuffered(float pressTime)
    {
        return Time.time - pressTime <= inputBufferTime;
    }

    private void Face(Vector2 direction)
    {
        facing = direction;
        animator.SetFloat("moveX", direction.x);
        animator.SetFloat("moveY", direction.y);
    }

    // Avance vers la case visée ; à l'arrivée, garde la distance en trop pour la case suivante
    private void Advance(float distance)
    {
        float remaining = Vector3.Distance(transform.position, moveTarget);
        if (distance < remaining)
        {
            transform.position = Vector3.MoveTowards(transform.position, moveTarget, distance);
            return;
        }

        transform.position = moveTarget;
        isMoving = false;
        carriedDistance = distance - remaining;

        // En vol : ni changement de niveau, ni porte (Goku passe au-dessus) ; le fantôme prend les portes (sans ramasser les zénis)
        if (isFlying)
        {
            if (isGhost)
                TakeDoorHere();
            return;
        }

        // Changement de niveau à l'arrivée sur la case (le pas lui-même a été vérifié au niveau de départ)
        int newLevel = LevelMap.LevelAfterEntering(moveTarget, level);
        if (newLevel != level)
            SetLevel(newLevel);

        ArriveOnCell();
    }

    // Arrivée au sol sur la case actuelle (en marchant ou en atterrissant) : zénis, puis porte
    private void ArriveOnCell()
    {
        // Zénis posés sur cette case, à ce niveau : ramassés en arrivant (pas en vol : Goku passe au-dessus)
        ZeniCoin.CollectAt(new Vector3Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y), level));
        TakeDoorHere();
    }

    // Arrivée sur une porte : on garde l'état du joueur pour la scène suivante, puis on y va
    private void TakeDoorHere()
    {
        var door = Door.FindAt(transform.position);
        if (door != null)
        {
            var arrival = new SceneTransition.Arrival
            {
                FromScene = gameObject.scene.name,
                LinkName = door.LinkName,
                EntryCell = door.Cell,
                Direction = facing,
            };
            if (!SceneTransition.IsSameScene(door.TargetScene))
                PlayerState.Save(health, ki, isGhost);
            if (SceneTransition.Go(door.TargetScene, arrival, doorFadeDuration, doorFadeDuration))
                carriedDistance = 0f;
        }
    }

    // trigger : nom du Trigger de l'Animator
    // action : appelée une fois quand l'animation atteint actionTime (0 = début, 1 = fin)
    IEnumerator Attack(string trigger, float actionTime = 0f, System.Action action = null)
    {
        isAttacking = true;

        animator.SetTrigger(trigger);

        // On attend que l'Animator entre dans un état taggé "Attack" (sécurité si l'Animator est mal réglé)
        float timeout = 0.5f;
        while (!IsInAttackState() && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        // Puis on attend qu'il en sorte, ou que l'animation arrive au bout : l'attaque est terminée.
        // transform et detransform n'ont pas de transition de sortie : elles restent figées sur leur
        // dernière image, ce qui laisse changer de controller sans afficher une image de l'ancien Idle
        while (IsInAttackState() && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
        {
            if (action != null && animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= actionTime)
            {
                action();
                action = null;
            }
            yield return null;
        }

        animator.ResetTrigger("attack");
        animator.ResetTrigger("criticalAttack");
        animator.ResetTrigger("rangedAttack");
        animator.ResetTrigger("transform");
        animator.ResetTrigger("detransform");
        animator.ResetTrigger(KamehamehaTrigger);
        isAttacking = false;
    }

    // Coûts payés en Ki, ou en PV pour Piccolo (paysWithHealth)
    private bool CanAffordKiBlast()
    {
        return paysWithHealth ? health.CanSpend(character.kiBlastHealthCost) : ki.CanFireKiBlast;
    }

    private bool CanAffordBeam()
    {
        return paysWithHealth ? health.CanSpend(character.beamHealthCost) : ki.CanFireKamehameha;
    }

    // Charge (animation s2 ; s3 pour le Makankosappo de Piccolo), puis rayon maintenu tant que la touche est tenue et qu'il reste
    // du Ki (ou des PV). Le joueur reste bloqué sur l'animation de tir (en boucle), sans pouvoir se tourner, jusqu'au relâchement.
    // key : touche tenue (E, ou R pour un rayon sur la touche R) ; isBeamKey : pause comptée sur la touche E, sinon sur R
    IEnumerator Kamehameha(KamehamehaBeam prefab, InputAction key, bool isBeamKey)
    {
        if (prefab == null)
        {
            Debug.LogWarning("Aucun rayon (Kamehameha Prefab) pour ce personnage.");
            yield break;
        }

        if (paysWithHealth ? !health.TrySpend(character.beamHealthCost) : !ki.TryStartKamehameha())
            yield break;

        isAttacking = true;
        animator.SetTrigger(KamehamehaTrigger);

        // On attend que l'Animator entre dans l'état Kamehameha (taggé "Attack")
        float timeout = 0.5f;
        while (!IsInAttackState() && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        // Charge : l'Animator passe tout seul à l'état de tir à la fin de l'animation de charge
        while (IsInAttackState() && animator.GetCurrentAnimatorStateInfo(0).shortNameHash != KamehamehaFireState)
            yield return null;

        KamehamehaBeam beam = Instantiate(prefab, transform.position + (Vector3)facing, Quaternion.identity);
        beam.Launch(facing, level);
        activeBeam = beam;
        sortingBeam = beam;
        if (!paysWithHealth)
            ki.StartKamehamehaBeam();

        while (key.IsPressed())
        {
            // Plus assez de Ki (ou de PV : il ne peut pas se tuer avec son propre rayon) : le rayon s'arrête
            if (paysWithHealth ? !health.TrySpend(character.beamHealthPerSecond * Time.deltaTime) : ki.IsDepleted)
                break;
            yield return null;
        }

        activeBeam = null;

        // La traînée efface le rayon toute seule ; le joueur reprend la main tout de suite
        beam.Release();
        if (isBeamKey)
            nextBeamTime = Time.time + beam.Cooldown;
        else
            nextChargedTime = Time.time + beam.Cooldown;
        if (!paysWithHealth)
            ki.EndKamehameha();
        animator.ResetTrigger(KamehamehaTrigger);
        animator.Play(IdleState, 0, 0f);
        isAttacking = false;
    }

    // Coup au corps à corps (normal ou critique), puis pause avant la prochaine attaque
    IEnumerator MeleeAttack(bool isCritical, float damage)
    {
        yield return Attack(isCritical ? "criticalAttack" : "attack", meleeHitTime, () => MeleeHit(damage, isCritical));
        nextMeleeTime = Time.time + (isCritical ? criticalCooldown : meleeCooldown);
    }

    // Toupie payée en PV (Piccolo) ou en Ki (Vegeto), selon le personnage
    private bool CanAffordSpin()
    {
        return paysWithHealth ? health.CanSpend(character.spinHealthCost) : ki.CanSpend(character.spinKiCost);
    }

    // Toupie de Piccolo / Vegeto (touche E) : il tourne sur lui-même (état Spin, planche s2) et frappe les 4 cases autour de lui
    // au milieu de l'animation, une fois par cible ; il regagne une part des dégâts infligés (spinHealRatio, 0 = aucun soin)
    IEnumerator Spin()
    {
        // En Ki : comme un Ki Blast (recharge coupée pendant la toupie)
        if (paysWithHealth ? !health.TrySpend(character.spinHealthCost) : !ki.TryStartKiBlast(character.spinKiCost))
            yield break;

        isAttacking = true;
        // Vitesse propre au personnage (fiche), relue à chaque image pour pouvoir la régler en Play
        animator.SetFloat(SpinSpeed, character.spinSpeed);
        animator.Play(SpinState, 0, 0f);
        // L'Animator n'entre dans l'état qu'à sa prochaine mise à jour (sécurité si l'Animator est mal réglé)
        float timeout = 0.5f;
        while (!IsInAttackState() && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        spinStartAngle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
        isSpinning = true;

        bool hasHit = false;
        while (IsInAttackState() && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
        {
            animator.SetFloat(SpinSpeed, character.spinSpeed);
            if (!hasHit && animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= meleeHitTime)
            {
                SpinHit();
                hasHit = true;
            }
            yield return null;
        }
        // État Spin manquant ou trop court : le coup part quand même
        if (!hasHit)
            SpinHit();

        HideSpinBlade();
        animator.Play(IdleState, 0, 0f);
        nextBeamTime = Time.time + character.spinCooldown;
        isAttacking = false;
        if (!paysWithHealth)
            ki.EndKiBlast();
    }

    // Dans LateUpdate, après l'Animator : la lame suit l'image de la toupie réellement affichée (et la vitesse de l'état Spin)
    private void LateUpdate()
    {
        UpdateBeamSorting();
        if (!isSpinning)
            return;
        if (character == null || character.spinBladeStraight == null || !IsInAttackState())
        {
            spinBlade.enabled = false;
            return;
        }

        var state = animator.GetCurrentAnimatorStateInfo(0);
        var clips = animator.GetCurrentAnimatorClipInfo(0);
        int frameCount = clips.Length > 0 ? Mathf.Max(1, Mathf.RoundToInt(clips[0].clip.length * clips[0].clip.frameRate)) : 8;
        int frame = Mathf.Clamp(Mathf.FloorToInt(state.normalizedTime * frameCount), 0, frameCount - 1);

        // Direction de la lame en huitièmes de tour (0 = droite, 2 = haut...) : droite / haut / gauche / bas = image droite tournée,
        // diagonales = image haut-droite tournée
        float angle = spinStartAngle + character.spinBladeStepDegrees * frame;
        int direction = ((Mathf.RoundToInt(angle / 45f) % 8) + 8) % 8;
        bool diagonal = direction % 2 == 1;
        spinBlade.sprite = diagonal && character.spinBladeDiagonal != null ? character.spinBladeDiagonal : character.spinBladeStraight;
        spinBlade.transform.localRotation = Quaternion.Euler(0f, 0f, (diagonal ? direction - 1 : direction) * 45f);

        Vector2 offset = (Vector2)character.SpinBladeOffsetPixels(direction) / 32f;
        spinBlade.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
        spinBlade.sortingLayerName = spriteRenderer.sortingLayerName;
        // Derrière : juste sous le personnage ; devant : juste au-dessus
        spinBlade.sortingOrder = spriteRenderer.sortingOrder + (character.SpinBladeBehind(direction) ? -1 : 1);
        spinBlade.enabled = true;
    }

    // Tant que son rayon existe, le joueur passe au-dessus de lui (départ derrière ses mains) ou dessous (rayon sous une cible collée) ;
    // ordre normal de son niveau ensuite (rayon détruit : la référence devient nulle)
    private void UpdateBeamSorting()
    {
        if (isFlying || level >= sortingOrderPerLevel.Length)
            return;
        int offset = sortingBeam != null ? sortingBeam.ShooterSortingOffset : 0;
        // Coup d'épée : un cran au-dessus des personnages (rang 0 de la tranche), au rang des techniques
        if (isSwordInFront)
            offset = Mathf.Max(offset, 1);
        spriteRenderer.sortingOrder = sortingOrderPerLevel[level] + offset;
    }

    private static CellSides SideOf(Vector2 direction)
    {
        if (direction == Vector2.up) return CellSides.Up;
        if (direction == Vector2.down) return CellSides.Down;
        if (direction == Vector2.left) return CellSides.Left;
        return CellSides.Right;
    }

    private void HideSpinBlade()
    {
        isSpinning = false;
        spinBlade.enabled = false;
    }

    private static readonly Vector2[] SpinSides = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
    private static readonly Vector2[] SpinSidesWithDiagonals =
    {
        Vector2.up, Vector2.down, Vector2.left, Vector2.right,
        new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(-1f, -1f), new Vector2(1f, -1f),
    };

    // Frappe les 4 cases voisines (8 avec les diagonales, selon la fiche), à son niveau, casse les Destructibles,
    // puis soigne selon les dégâts réellement infligés
    private void SpinHit()
    {
        var hitTargets = new HashSet<Health>();
        float dealt = 0f;
        foreach (var side in character.spinHitsDiagonals ? SpinSidesWithDiagonals : SpinSides)
        {
            Vector2 cell = (Vector2)transform.position + side;
            foreach (var hit in Physics2D.OverlapCircleAll(cell, meleeHitRadius, solidObjectsLayer))
            {
                if (!HeightLevel.IsOnLevel(hit, level))
                    continue;

                var targetHealth = hit.GetComponentInParent<Health>();
                if (targetHealth != null && !targetHealth.IsDead && hitTargets.Add(targetHealth))
                {
                    float before = targetHealth.Current;
                    PlayerCombat.Hit(targetHealth, meleeDamage);
                    dealt += before - targetHealth.Current;
                }

                if (hit.TryGetComponent(out DestructibleTilemap destructibles))
                    destructibles.TakeDamage(cell, meleeDamage);
            }
        }
        health.Heal(dealt * character.spinHealRatio);
    }

    // Coup d'épée de Trunks (touche E) : comme un coup normal sur la case devant (critique possible), état Sword (planche s2)
    IEnumerator Sword()
    {
        bool isCritical = Random.value < criticalChance;
        float damage = isCritical ? character.swordDamage * criticalMultiplier : character.swordDamage;
        // L'épée déborde de sa case : dans ces directions, il passe devant les ennemis pendant le coup (UpdateBeamSorting)
        isSwordInFront = (character.swordInFrontDirections & SideOf(facing)) != 0;
        yield return PlayAttackState(SwordState, meleeHitTime, () => MeleeHit(damage, isCritical));
        isSwordInFront = false;
        nextBeamTime = Time.time + character.swordCooldown;
    }

    // Coup de poing de Goten (touche R) : comme un coup normal sur la case devant (critique possible), état SpecialShot (planche s3) ;
    // la cible touchée est étourdie (StatusEffects, figée sur son image)
    IEnumerator StunPunch()
    {
        if (!ki.TryStartKiBlast(character.stunPunchKiCost))
            yield break;

        bool isCritical = Random.value < criticalChance;
        float damage = isCritical ? character.stunPunchDamage * criticalMultiplier : character.stunPunchDamage;
        yield return PlayAttackState(SpecialShotState, character.stunPunchHitTime,
            () => MeleeHit(damage, isCritical, character.stunDuration, character.stunIcon));
        nextChargedTime = Time.time + character.stunPunchCooldown;
        ki.EndKiBlast();
    }

    // Burning Blast de Trunks (touche R) : comme un Ki Blast, avec son propre prefab, coût et pause, état SpecialShot (planche s3)
    IEnumerator SpecialShot()
    {
        if (!ki.TryStartKiBlast(character.specialKiCost))
            yield break;

        // Le projectile part à la 2e image (bras tendu)
        yield return PlayAttackState(SpecialShotState, 0.5f, () => SpawnProjectile(specialProjectilePrefab));
        nextChargedTime = Time.time + character.specialCooldown;
        ki.EndKiBlast();
    }

    // Poing du dragon de Gogeta (touche R, un appui suffit) : état SpecialShot (planche s3, reste figé sur la dernière image),
    // le poing part à LaunchAnimationTime de l'animation ; Gogeta reste bloqué jusqu'à son retour
    IEnumerator DragonFistAttack()
    {
        if (!ki.TryStartKiBlast(dragonFistPrefab.KiCost))
            yield break;

        isAttacking = true;
        animator.Play(SpecialShotState, 0, 0f);
        float timeout = 0.5f;
        while (!IsInAttackState() && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        while (IsInAttackState() && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < dragonFistPrefab.LaunchAnimationTime)
            yield return null;

        var fist = Instantiate(dragonFistPrefab, transform.position, Quaternion.identity);
        fist.Launch(facing, level);
        activeDragonFist = fist;
        // Détruit à son retour
        while (fist != null && !fist.IsFinished)
            yield return null;

        activeDragonFist = null;
        nextChargedTime = Time.time + dragonFistPrefab.Cooldown;
        ki.EndKiBlast();
        animator.Play(IdleState, 0, 0f);
        isAttacking = false;
    }

    // Volleyball de Gotenks (touche E, un appui) : tir comme un Ki Blast, état SpecialShot (planche s2), pause comptée sur E
    IEnumerator ProjectileOnE()
    {
        if (!ki.TryStartKiBlast(character.projectileOnEKiCost))
            yield break;

        // Le projectile part à la 2e image (bras tendu)
        yield return PlayAttackState(SpecialShotState, 0.5f, () => SpawnProjectile(projectileOnE));
        nextBeamTime = Time.time + character.projectileOnECooldown;
        ki.EndKiBlast();
    }

    // Gotenks (touche R) : paie le Ki, joue s3 (état Genkidama, inutilisé par Gotenks : l'état transform sert au SSJ), puis devient le fantôme
    IEnumerator GhostTransformation()
    {
        if (!ki.TryStartKiBlast(character.ghostKiCost))
            yield break;
        ki.EndKiBlast();

        Face(Vector2.down);
        yield return PlayAttackState(GenkidamaState, 1f, null);
        EnterGhost();
        Face(Vector2.down);
    }

    // Fantôme de la forme actuelle (SSJ, SSJ3...), sinon celui de la forme normale
    private AnimatorOverrideController CurrentGhostController()
    {
        int index = ki.Level - 1;
        if (character != null && index >= 0 && index < character.transformedGhostControllers.Length
            && character.transformedGhostControllers[index] != null)
            return character.transformedGhostControllers[index];
        return ghostController;
    }

    // Animations de la forme actuelle de Gotenks (normale ou palier atteint)
    private RuntimeAnimatorController CurrentFormController()
    {
        int index = ki.Level - 1;
        if (index >= 0 && index < transformedControllers.Length && transformedControllers[index] != null)
            return transformedControllers[index];
        return baseController;
    }

    // Fantôme : animations du fantôme, au-dessus de tout comme en vol (niveau Air : ni touché, ni repéré par les ennemis)
    private void EnterGhost()
    {
        isGhost = true;
        isFlying = true;
        if (heightLevel != null)
            heightLevel.Level = HeightLevel.Air;
        spriteRenderer.sortingLayerName = flyingSortingLayer;
        spriteRenderer.sortingOrder = flyingSortingOrder;
        SwitchController(CurrentGhostController());
        Face(facing);
    }

    // Espace : retour en Gotenks sur la case actuelle, si on peut s'y poser (mêmes règles qu'un atterrissage)
    private void TryEndGhost()
    {
        if (TryGetLandingLevel(out int landingLevel))
            StartCoroutine(GhostReturn(landingLevel));
    }

    // Clip detransform du fantôme (s3 à l'envers), au rythme des retours, puis Gotenks se pose dans sa forme d'avant
    // (SSJ : sa jauge a continué de baisser ; vide, le retour en forme normale suit l'atterrissage, comme après un vol)
    IEnumerator GhostReturn(int landingLevel)
    {
        Face(Vector2.down);
        animator.SetFloat(DetransformSpeed, detransformSpeed);
        yield return Attack("detransform");
        isGhost = false;
        SwitchController(CurrentFormController());
        Land(landingLevel);
        Face(Vector2.down);
    }

    // Joue un état taggé "Attack" lancé directement (sans trigger ni transition) et appelle action à actionTime de l'animation ;
    // état manquant ou trop court : l'action a lieu quand même
    IEnumerator PlayAttackState(int state, float actionTime, System.Action action)
    {
        isAttacking = true;
        animator.Play(state, 0, 0f);
        // L'Animator n'entre dans l'état qu'à sa prochaine mise à jour
        float timeout = 0.5f;
        while (!IsInAttackState() && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        while (IsInAttackState() && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
        {
            if (action != null && animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= actionTime)
            {
                action();
                action = null;
            }
            yield return null;
        }
        action?.Invoke();

        animator.Play(IdleState, 0, 0f);
        isAttacking = false;
    }

    // Touche tenue : Goku figé bras levés, la boule se charge au-dessus de lui puis la cible va et vient devant lui.
    // Au relâchement : la boule est lancée sur la cible si elle est prête, sinon la technique est annulée (Ki perdu)
    IEnumerator Genkidama()
    {
        if (chargedPrefab == null)
        {
            Debug.LogWarning("Aucune technique R (Charged Technique Prefab) pour ce personnage.");
            yield break;
        }

        if (!ki.TryStartGenkidama())
            yield break;

        isAttacking = true;
        animator.Play(GenkidamaState, 0, 0f);

        // La technique se place elle-même (Genkidama au-dessus du joueur, Big Bang devant)
        activeGenkidama = Instantiate(chargedPrefab, transform.position, Quaternion.identity);
        activeGenkidama.Begin(transform.position, facing, level);

        while (controls.Player.Genkidama.IsPressed())
            yield return null;

        activeGenkidama.Release();
        nextChargedTime = Time.time + activeGenkidama.Cooldown;
        activeGenkidama = null;
        ki.EndGenkidama();
        animator.Play(IdleState, 0, 0f);
        isAttacking = false;
    }

    IEnumerator KiBlast()
    {
        if (paysWithHealth ? !health.TrySpend(character.kiBlastHealthCost) : !ki.TryStartKiBlast())
            yield break;

        isAttacking = true;
        // Le projectile part à la moitié de l'animation (2e image : le bras tendu)
        yield return Attack("rangedAttack", 0.5f, SpawnProjectile);
        nextKiBlastTime = Time.time + kiBlastCooldown;
        if (!paysWithHealth)
            ki.EndKiBlast();
    }

    // Prochain palier (1 = premier) ayant un Override dans la fiche ; les cases vides sont des paliers sautés
    // (Gogeta : SSJ → SSJ4, sans palier 2). -1 s'il n'y en a plus
    int NextTransformationStage()
    {
        for (int index = ki.Level; index < transformedControllers.Length; index++)
        {
            if (transformedControllers[index] != null)
                return index + 1;
        }
        return -1;
    }

    IEnumerator Transformation()
    {
        int nextStage = NextTransformationStage();
        if (nextStage < 0)
        {
            Debug.LogWarning($"Aucun Transformed Controller assigné au-dessus du palier {ki.Level} sur le PlayerController.");
            yield break;
        }
        AnimatorOverrideController nextController = transformedControllers[nextStage - 1];

        // Paie le coût (en Ki, ou en PV pour Piccolo) et passe au palier visé ; la jauge du palier commence à se remplir
        if (paysWithHealth && !health.CanSpend(character.transformHealthCost))
            yield break;
        if (!ki.TryStartTransformation(nextStage))
            yield break;
        if (paysWithHealth)
            health.TrySpend(character.transformHealthCost);

        isAttacking = true;
        // Vitesse de l'état transform (paramètre de l'Animator), relue à chaque transformation : réglable en jeu
        // Piccolo : même rythme que son retour (même nombre d'images, donc même durée) ; Gohan ado : vers Sayaman seulement
        // (ki.Level est déjà le palier atteint)
        float framesPerSecond = character != null && character.TransformsAtDetransformSpeed(ki.Level)
            ? DetransformClipFramesPerSecond * detransformSpeed
            : transformFramesPerSecond;
        animator.SetFloat(TransformSpeed, framesPerSecond / TransformClipFramesPerSecond);
        // L'état Transform est taggé "Attack" : on réutilise la même logique d'attente
        yield return Attack("transform");

        // On remplace les animations par celles de la nouvelle forme
        SwitchController(nextController);
        ki.CompleteTransformation();

        // L'animation de transformation se joue de face, donc on garde le personnage tourné vers le bas
        Face(Vector2.down);
    }

    IEnumerator Detransformation()
    {
        ki.StartDetransformation();

        isAttacking = true;
        // Vitesse de l'état detransform (paramètre de l'Animator), relue à chaque retour : réglable en jeu
        animator.SetFloat(DetransformSpeed, detransformSpeed);
        // L'état detransform (sprites tr0) est taggé "Attack", comme la transformation
        yield return Attack("detransform");

        SwitchController(baseController);
        ki.CompleteDetransformation();

        // L'animation de retour se joue de face
        Face(Vector2.down);
    }

    // Animations et transformations du personnage joué. Sans fiche : animations du prefab, pas de transformation
    private void ApplyCharacter()
    {
        if (character == null || character.animatorController == null)
        {
            Debug.LogWarning("Aucune fiche de personnage (Character) sur le PlayerController : animations du prefab, sans transformation.");
            baseController = animator.runtimeAnimatorController;
            transformedControllers = new AnimatorOverrideController[0];
            beamPrefab = kamehamehaPrefab;
            chargedPrefab = chargedTechniquePrefab;
            chargedBeamPrefab = null;
            paysWithHealth = false;
            spinOnE = false;
            swordOnE = false;
            stunPunchOnR = false;
            specialProjectilePrefab = null;
            dragonFistPrefab = null;
            projectileOnE = null;
            ghostController = null;
            ki.UsesKi = true;
            return;
        }

        baseController = character.animatorController;
        transformedControllers = character.transformedControllers ?? new AnimatorOverrideController[0];
        beamPrefab = character.beamPrefab != null ? character.beamPrefab : kamehamehaPrefab;
        chargedPrefab = character.chargedTechniquePrefab != null ? character.chargedTechniquePrefab : chargedTechniquePrefab;
        chargedBeamPrefab = character.chargedBeamPrefab;
        paysWithHealth = character.paysWithHealth;
        // Transformations sans Ki (ni coût, ni consommation) pour un personnage qui paie en PV
        ki.UsesKi = !paysWithHealth;
        spinOnE = character.spinOnE;
        swordOnE = character.swordOnE;
        stunPunchOnR = character.stunPunchOnR;
        specialProjectilePrefab = character.specialProjectilePrefab;
        dragonFistPrefab = character.dragonFistPrefab;
        projectileOnE = character.projectileOnE;
        ghostController = character.ghostController;
        animator.runtimeAnimatorController = baseController;
        if (character.startSprite != null)
            spriteRenderer.sprite = character.startSprite;
    }

    // Rayon de la touche E : celui du palier actuel s'il en a un propre (alignement différent selon la forme), sinon celui du personnage
    private KamehamehaBeam CurrentBeamPrefab()
    {
        int index = ki.Level - 1;
        if (character != null && ki.CurrentForm == PlayerKi.Form.Transformed && character.transformedBeamPrefabs != null
            && index >= 0 && index < character.transformedBeamPrefabs.Length && character.transformedBeamPrefabs[index] != null)
            return character.transformedBeamPrefabs[index];
        return beamPrefab;
    }

    // Touche de test F2 : personnage suivant de testCharacters, gardé pour les prochaines scènes (met fin à une fusion)
    private void SwitchCharacter()
    {
        if (testCharacters == null || testCharacters.Length == 0)
            return;

        PlayerState.FusionOrigin = null;
        int index = System.Array.IndexOf(testCharacters, character);
        SetCharacter(testCharacters[(index + 1) % testCharacters.Length]);
        Debug.Log($"Personnage de test : {character.displayName}");
    }

    // Change de personnage sur place, en forme normale ; PV et Ki gardés
    private void SetCharacter(CharacterData newCharacter)
    {
        character = newCharacter;
        PlayerState.Character = character;
        ApplyCharacter();
        SwitchController(baseController);
        // Le nouvel Animator repart de zéro : on lui redonne la direction actuelle
        Face(facing);
    }

    // Fusion en cours : temps restant au chrono (secondes), -1 sans fusion (lu par le HUD)
    public bool IsFused => PlayerState.FusionOrigin != null;
    public float FusionTimeLeft => IsFused ? Mathf.Max(0f, PlayerState.FusionEndTime - Time.time) : -1f;

    // Goten (touche é) : paie le Ki, joue la fusion (état Fusion, planche tr2 de sa forme, au rythme des transformations), puis
    // devient Gotenks dans le même palier (Goten SSJ → Gotenks SSJ, jauges gardées) ; le personnage d'origine est gardé pour la séparation
    IEnumerator Fusion()
    {
        if (!ki.TryStartKiBlast(character.fusionKiCost))
            yield break;
        ki.EndKiBlast();

        Face(Vector2.down);
        animator.SetFloat(TransformSpeed, transformFramesPerSecond / TransformClipFramesPerSecond);
        yield return PlayAttackState(FusionState, 1f, null);

        PlayerState.FusionOrigin = character;
        PlayerState.FusionEndTime = Time.time + character.fusionDuration;
        SetCharacter(character.fusionCharacter);
        if (ki.Level > 0 && ki.Level <= transformedControllers.Length)
            SwitchController(transformedControllers[ki.Level - 1]);
        Face(Vector2.down);
    }

    // Séparation animée (forme normale) : le personnage fusionné n'a pas de fusion, son état Fusion porte donc la séparation.
    // Elle dépend du personnage d'origine (Gogeta : tr0goku ou tr0vegeta) : separationClip de sa fiche, posé sur un
    // Override Controller temporaire (remplacé par celui du personnage d'origine à la fin)
    IEnumerator Separation()
    {
        Face(Vector2.down);
        var separationClip = PlayerState.FusionOrigin.separationClip;
        if (separationClip != null && animator.runtimeAnimatorController is AnimatorOverrideController current)
        {
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            current.GetOverrides(overrides);
            for (int i = 0; i < overrides.Count; i++)
                if (overrides[i].Key.name == FusionClipName)
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, separationClip);
            var separation = new AnimatorOverrideController(current.runtimeAnimatorController);
            separation.ApplyOverrides(overrides);
            SwitchController(separation);
            Face(Vector2.down);
        }
        animator.SetFloat(TransformSpeed, transformFramesPerSecond / TransformClipFramesPerSecond);
        yield return PlayAttackState(FusionState, 1f, null);
        EndFusion();
    }

    // Forme transformée : a-t-elle sa propre animation de fusion (clip fusion remplacé dans son Override Controller) ?
    // Sinon elle jouerait celle de la forme normale (Goku SSJ : fusion de Goku normal)
    private bool FormHasOwnFusionClip()
    {
        if (!(animator.runtimeAnimatorController is AnimatorOverrideController current))
            return true;
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        current.GetOverrides(overrides);
        foreach (var pair in overrides)
            if (pair.Key.name == FusionClipName)
                return pair.Value != null;
        return false;
    }

    // Retour au personnage d'origine en forme normale (sans animation : la mort ou F2 passent par ailleurs)
    private void EndFusion()
    {
        if (ki.CurrentForm != PlayerKi.Form.Normal)
            ki.CompleteDetransformation();
        var origin = PlayerState.FusionOrigin;
        PlayerState.FusionOrigin = null;
        SetCharacter(origin);
        Face(Vector2.down);
    }

    // Entre deux controllers qui partagent la même base (Player.controller et ses Override Controllers),
    // Unity garde l'état en cours : on renvoie donc l'Animator sur Idle, sinon il resterait figé
    // sur la dernière image de transform / detransform (ces états n'ont pas de sortie)
    private void SwitchController(RuntimeAnimatorController controller)
    {
        animator.runtimeAnimatorController = controller;
        animator.Play(IdleState, 0, 0f);
    }

    // Décollage : Goku passe au-dessus de tout (niveau Air, affiché au-dessus du feuillage)
    private void TakeOff()
    {
        isFlying = true;
        if (heightLevel != null)
            heightLevel.Level = HeightLevel.Air;
        spriteRenderer.sortingLayerName = flyingSortingLayer;
        spriteRenderer.sortingOrder = flyingSortingOrder;
        animator.Play(FlyState, 0, 0f);
    }

    // Vol : seules les limites de la carte arrêtent Goku (case sans tuile sur le calque Background)
    private bool CanFlyTo(Vector2 direction)
    {
        if (mapTilemap == null)
            return true;
        var target = transform.position + (Vector3)direction;
        return mapTilemap.HasTile(mapTilemap.WorldToCell(target));
    }

    // Atterrissage sur la case actuelle, si c'est possible : pas d'objet qui bloque à ce niveau (ennemi, cible, rocher),
    // et case pas pleine au niveau d'arrivée. Niveau d'arrivée : celui de la tuile Niveaux de la case ; sinon, au-dessus
    // d'un pont, sur le pont (bridgeLevel) ; sinon au sol (0). Un pont impossible (rambarde, ennemi dessus) : on essaie dessous.
    // Jamais le niveau du décollage : le niveau 1 n'existe que sur les ponts et les cases Niveaux ; parti d'un pont, Goku se
    // retrouvait « en haut » sur le sol (collisions du sol et coups sans effet). Sinon il reste en vol (arbre, mur...)
    private void TryLand()
    {
        if (TryGetLandingLevel(out int landingLevel))
            Land(landingLevel);
    }

    // Niveau où l'on peut se poser sur la case actuelle (faux : impossible, on reste en l'air)
    private bool TryGetLandingLevel(out int landingLevel)
    {
        // -1 renvoyé = case sans tuile Niveaux (une tuile Niveaux passe avant le pont)
        landingLevel = LevelMap.LevelAfterEntering(transform.position, -1);
        if (landingLevel >= 0)
            return CanLandOnLevel(landingLevel);

        bool overBridge = bridgeTilemap != null && bridgeTilemap.HasTile(bridgeTilemap.WorldToCell(transform.position));
        landingLevel = bridgeLevel;
        if (overBridge && CanLandOnLevel(bridgeLevel))
            return true;
        landingLevel = 0;
        return CanLandOnLevel(0);
    }

    private bool CanLandOnLevel(int landingLevel)
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, collisionCheckRadius, solidObjectsLayer);
        if (HeightLevel.FirstOnLevel(hits, landingLevel) != null)
            return false;
        return !CollisionMap.IsSolidCell(transform.position, landingLevel);
    }

    private void Land(int landingLevel)
    {
        isFlying = false;
        spriteRenderer.sortingLayerName = groundSortingLayer;
        SetLevel(landingLevel);
        animator.Play(IdleState, 0, 0f);
        // Atterrissage sur une porte : on la prend (sortie dans le sens où il regarde)
        ArriveOnCell();
    }

    private void OnDied()
    {
        if (isDead)
            return;
        isDead = true;
        // Coupe les attaques, la transformation et le Kamehameha en cours
        StopAllCoroutines();
        HideSpinBlade();
        isSwordInFront = false;
        // La mort sépare la fusion : on revient avec le personnage d'origine (Goten), dans la scène de réapparition
        if (PlayerState.FusionOrigin != null)
        {
            PlayerState.Character = PlayerState.FusionOrigin;
            PlayerState.FusionOrigin = null;
        }
        StartCoroutine(Death());
    }

    // Tout s'arrête (attaque, technique, pas en cours), Goku tombe pendant le fondu au noir, puis part dans l'Autre Monde.
    // La scène d'arrivée est chargée à neuf : Goku y a ses PV et son Ki pleins, en forme normale (rien n'est gardé)
    IEnumerator Death()
    {
        // Rayon (Kamehameha, Makankosappo...) coupé en plein tir : il s'efface tout seul
        if (activeBeam != null)
        {
            activeBeam.Release();
            activeBeam = null;
        }
        // Genkidama en charge ou en visée : il disparaît sans être lancé
        if (activeGenkidama != null)
        {
            activeGenkidama.Cancel();
            activeGenkidama = null;
        }
        // Poing du dragon en cours : il disparaît
        if (activeDragonFist != null)
        {
            activeDragonFist.Cancel();
            activeDragonFist = null;
        }
        isMoving = false;
        isAttacking = true;
        animator.SetBool("isMoving", false);

        // Goku tombe (d0 de sa forme), puis reste allongé sur la dernière image (état sans sortie),
        // pendant que l'écran s'assombrit (fondu lancé ci-dessous, en même temps)
        animator.Play(DeathState, 0, 0f);

        var arrival = new SceneTransition.Arrival
        {
            FromScene = respawnDoorTarget,
            LinkName = respawnDoorLink,
            EntryCell = new Vector3Int(int.MinValue, int.MinValue, 0),
            Direction = Vector2.down,
        };
        // Mort pendant un passage de porte : on attend la fin de la transition en cours
        while (SceneTransition.IsBusy)
            yield return null;
        SceneTransition.Go(respawnScene, arrival, deathFadeDuration, doorFadeDuration, reload: true);
    }

    // Frappe la case devant le joueur (seulement ce qui est à son niveau de hauteur) ;
    // stunDuration > 0 : la cible (ni alliée, ni K.O.) est aussi étourdie (poing de Goten)
    private void MeleeHit(float damage, bool isCritical, float stunDuration = 0f, Sprite stunIcon = null)
    {
        Vector2 target = (Vector2)transform.position + facing;
        var hitTargets = new HashSet<Health>();
        foreach (var hit in Physics2D.OverlapCircleAll(target, meleeHitRadius, solidObjectsLayer))
        {
            var targetHealth = hit.GetComponentInParent<Health>();
            // Une même cible peut avoir plusieurs colliders : on ne la frappe qu'une fois
            if (targetHealth != null && HeightLevel.IsOnLevel(hit, level) && hitTargets.Add(targetHealth))
            {
                PlayerCombat.Hit(targetHealth, damage, isCritical);
                if (stunDuration > 0f && !targetHealth.IsAlly && !targetHealth.IsDead)
                    StatusEffects.Of(targetHealth).Lock(stunDuration, null, stunIcon);
            }
        }
    }

    private bool IsInAttackState()
    {
        return animator.GetCurrentAnimatorStateInfo(0).IsTag("Attack");
    }

    private void SpawnProjectile()
    {
        SpawnProjectile(projectilePrefab);
    }

    private void SpawnProjectile(Projectile prefab)
    {
        if (prefab == null)
        {
            Debug.LogWarning("Aucun Projectile Prefab assigné sur le PlayerController.");
            return;
        }

        Vector3 spawnPos = transform.position + (Vector3)(facing * projectileSpawnOffset);
        Projectile projectile = Instantiate(prefab, spawnPos, Quaternion.identity);
        projectile.FiredByPlayer = true;
        projectile.Launch(facing, level);
    }
}
