using System;
using UnityEngine;

namespace DBZ.Player
{
    // Jauges de Ki du joueur : recharge, coût des techniques et consommation pendant la transformation.
    // Une jauge par forme (normale, SSJ, SSJ3...) : en se transformant, la jauge de la forme de départ
    // paie le coût puis reste figée ; la nouvelle jauge démarre pleine et baisse seule.
    // Au retour, la jauge normale retrouve la valeur qu'elle avait au départ.
    // Le PlayerController décide des actions ; ce composant ne gère que les valeurs.
    public class PlayerKi : MonoBehaviour
    {
        public enum Form { Normal, Transforming, Transformed, Detransforming }

        // Réglages d'un palier de transformation (SSJ, puis SSJ3...)
        [Serializable]
        public class TransformationStage
        {
            public string name = "SSJ";
            // Ki payé pour atteindre ce palier
            public float cost = 10f;
            // Consommation une fois ce palier atteint (Ki par seconde)
            public float drainPerSecond = 3.33f;
        }

        [SerializeField] private float maxKi = 100f;
        // 1 Ki/s : une jauge vide se remplit en 100 s
        [SerializeField] private float regenPerSecond = 1f;
        // La recharge reprend ce délai (en secondes) après la dernière technique (Ki Blast, Kamehameha)
        [SerializeField] private float regenDelayAfterKiBlast = 0.5f;
        [SerializeField] private float kiBlastCost = 5f;
        // Kamehameha : coût au lancement de la charge, puis consommation tant que le rayon est maintenu
        [SerializeField] private float kamehamehaCost = 15f;
        [SerializeField] private float kamehamehaDrainPerSecond = 10f;
        // Genkidama : coût payé à l'appui (perdu si la charge est annulée)
        [SerializeField] private float genkidamaCost = 50f;
        // Paliers dans l'ordre : chacun n'est accessible que depuis le précédent.
        // SSJ : 3,33 Ki/s = 30 s pour une jauge pleine ; SSJ3 : 5 Ki/s = 20 s
        [SerializeField] private TransformationStage[] transformations =
        {
            new TransformationStage { name = "SSJ", cost = 10f, drainPerSecond = 3.33f },
            new TransformationStage { name = "SSJ3", cost = 20f, drainPerSecond = 5f },
        };

        public float Max => maxKi;
        // Ki de la forme actuelle : c'est lui que paient les techniques et la transformation suivante
        public float Current
        {
            get => stageKi[Level];
            private set => stageKi[Level] = value;
        }
        public Form CurrentForm { get; private set; } = Form.Normal;
        // Palier atteint ou en cours d'atteinte : 0 = forme normale, 1 = SSJ, 2 = SSJ3...
        public int Level { get; private set; }
        public int StageCount => transformations.Length;

        public bool CanFireKiBlast => Current >= kiBlastCost;
        public bool CanFireKamehameha => Current >= kamehamehaCost;
        public bool CanFireGenkidama => Current >= genkidamaCost;
        public bool CanSpend(float cost) => Current >= cost;
        // Depuis la forme normale ou un palier déjà atteint, s'il reste un palier au-dessus
        public bool CanTransform => CanTransformTo(Level + 1);

        // Vers un palier précis au-dessus du palier actuel (Gogeta saute le palier 2 : SSJ → SSJ4) ;
        // le coût est celui du palier visé
        public bool CanTransformTo(int stage)
        {
            return (CurrentForm == Form.Normal || CurrentForm == Form.Transformed)
                   && stage > Level && stage <= transformations.Length
                   && (!UsesKi || Current >= transformations[stage - 1].cost);
        }
        // Faux pour un personnage qui paie en PV (Piccolo) : ses transformations ne coûtent ni ne consomment de Ki,
        // elles durent jusqu'au retour volontaire. Réglé par le PlayerController selon la fiche du personnage
        public bool UsesKi { get; set; } = true;
        // Jamais vide sans Ki (Piccolo) : pas de retour forcé
        public bool IsDepleted => UsesKi && Current <= 0f;

        // Une valeur par forme : [0] = normale (bleue), [1] = SSJ (jaune), [2] = SSJ3 (orange)...
        private float[] stageKi;

        private bool isFiringKiBlast;
        // Kamehameha en cours (charge ou rayon) : la recharge est coupée
        private bool isUsingKamehameha;
        // Rayon sorti : le Ki baisse en continu
        private bool isKamehamehaBeamActive;
        // Genkidama en cours (charge et visée) : la recharge est coupée
        private bool isUsingGenkidama;
        // Fin de la dernière technique (Ki Blast ou Kamehameha) : la recharge reprend après un délai
        private float lastTechniqueEndTime = float.NegativeInfinity;

        private void Awake()
        {
            stageKi = new float[transformations.Length + 1];
            stageKi[0] = maxKi;
        }

        // Ki d'une forme (0 = normale), pour l'affichage des jauges
        public float GetStageKi(int stage)
        {
            return stageKi[stage];
        }

        // Copie du Ki de chaque forme, pour le garder d'une scène à l'autre (PlayerState)
        public float[] GetAllStageKi()
        {
            return (float[])stageKi.Clone();
        }

        // Remet le Ki gardé et la forme (0 = normale) en arrivant dans une nouvelle scène, sans animation.
        // Une porte ne se prend qu'en marchant : jamais en pleine transformation ni pendant une technique
        public void Restore(float[] savedStageKi, int formLevel)
        {
            for (int stage = 0; stage < stageKi.Length && stage < savedStageKi.Length; stage++)
                stageKi[stage] = savedStageKi[stage];
            Level = Mathf.Clamp(formLevel, 0, transformations.Length);
            CurrentForm = Level > 0 ? Form.Transformed : Form.Normal;
        }

        private void Update()
        {
            if (isKamehamehaBeamActive)
                Current = Mathf.Max(0f, Current - kamehamehaDrainPerSecond * Time.deltaTime);

            if (CurrentForm == Form.Transformed && UsesKi)
            {
                Current = Mathf.Max(0f, Current - transformations[Level - 1].drainPerSecond * Time.deltaTime);
            }
            else if (CurrentForm == Form.Normal && !isFiringKiBlast && !isUsingKamehameha && !isUsingGenkidama
                     && Time.time - lastTechniqueEndTime >= regenDelayAfterKiBlast)
            {
                Current = Mathf.Min(maxKi, Current + regenPerSecond * Time.deltaTime);
            }
        }

        // À appeler au début de l'animation de tir : paie le coût et coupe la recharge
        public bool TryStartKiBlast()
        {
            return TryStartKiBlast(kiBlastCost);
        }

        // Tir avec un coût propre (Burning Blast de Trunks) : même recharge coupée que le Ki Blast, EndKiBlast à la fin
        public bool TryStartKiBlast(float cost)
        {
            if (Current < cost)
                return false;

            Current -= cost;
            isFiringKiBlast = true;
            return true;
        }

        // À appeler à la fin de l'animation de tir : le délai avant recharge démarre
        public void EndKiBlast()
        {
            isFiringKiBlast = false;
            lastTechniqueEndTime = Time.time;
        }

        // À appeler au début de la charge du Kamehameha : paie le coût et coupe la recharge
        public bool TryStartKamehameha()
        {
            if (!CanFireKamehameha)
                return false;

            Current -= kamehamehaCost;
            isUsingKamehameha = true;
            return true;
        }

        // À appeler quand le rayon sort : le Ki baisse tant qu'il est maintenu
        public void StartKamehamehaBeam()
        {
            isKamehamehaBeamActive = true;
        }

        // À appeler au relâchement (ou quand le Ki est épuisé) : le délai avant recharge démarre
        public void EndKamehameha()
        {
            isUsingKamehameha = false;
            isKamehamehaBeamActive = false;
            lastTechniqueEndTime = Time.time;
        }

        // À appeler à l'appui : paie le coût et coupe la recharge
        public bool TryStartGenkidama()
        {
            if (!CanFireGenkidama)
                return false;

            Current -= genkidamaCost;
            isUsingGenkidama = true;
            return true;
        }

        // À appeler au relâchement (lancé ou annulé) : le délai avant recharge démarre
        public void EndGenkidama()
        {
            isUsingGenkidama = false;
            lastTechniqueEndTime = Time.time;
        }

        public bool TryStartTransformation()
        {
            return TryStartTransformation(Level + 1);
        }

        public bool TryStartTransformation(int stage)
        {
            if (!CanTransformTo(stage))
                return false;

            // La jauge de la forme de départ paie le coût puis reste figée ;
            // celle du palier atteint démarre pleine
            if (UsesKi)
                Current -= transformations[stage - 1].cost;
            Level = stage;
            Current = maxKi;
            CurrentForm = Form.Transforming;
            return true;
        }

        public void CompleteTransformation()
        {
            CurrentForm = Form.Transformed;
        }

        public void StartDetransformation()
        {
            CurrentForm = Form.Detransforming;
        }

        // Retour direct à la forme normale, quel que soit le palier : la jauge normale reprend
        // la valeur gardée au départ ; celles des paliers sont vidées
        public void CompleteDetransformation()
        {
            for (int stage = 1; stage < stageKi.Length; stage++)
                stageKi[stage] = 0f;
            Level = 0;
            CurrentForm = Form.Normal;
        }
    }
}
