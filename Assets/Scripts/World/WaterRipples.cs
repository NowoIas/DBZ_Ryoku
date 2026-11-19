using UnityEngine;

namespace DBZ.World
{
    // Sillage des rayons et des projectiles sur l'eau : garde ceux en cours (ou tout juste finis) et les envoie au shader
    // "DBZ/Tilemap Effects" des tilemaps, qui dessine les vaguelettes sur les pixels d'eau autour d'eux.
    // Rien à poser dans les scènes : les rayons appellent SetBeam / EndBeam, les projectiles SetProjectile / EndProjectile,
    // les explosions AddRing.
    // Variables globales de shader : aucun fichier de matériau n'est modifié en jeu.
    public static class WaterRipples
    {
        // Même nombre que WATER_BEAM_COUNT dans le shader
        private const int MaxBeams = 8;

        private static readonly int BeamsId = Shader.PropertyToID("_WaterBeams");
        private static readonly int BeamTimesId = Shader.PropertyToID("_WaterBeamTimes");
        private static readonly int BeamExtrasId = Shader.PropertyToID("_WaterBeamExtras");

        // xy = début, zw = fin du rayon (monde, en cases)
        private static readonly Vector4[] beams = new Vector4[MaxBeams];
        // x = instant du départ, z = 0 place libre / 1 rayon / 2 projectile / 3 explosion / 4 tache brûlée, w = 1 si fini ;
        // y = instant de la fin (rayon), ou instant où le projectile était au point de fin
        private static readonly Vector4[] times = new Vector4[MaxBeams];
        // x = distance de départ (px), y = distance maximale (px), z = durée (s), w = nombre d'anneaux
        // (projectile : y = 0 → réglages du matériau ; rayon : x = vitesse d'avancée en cases par seconde)
        private static readonly Vector4[] extras = new Vector4[MaxBeams];
        // Rayon qui tient chaque place tant qu'il tire (null : place libre, ou vagues en train de s'éteindre)
        private static readonly Object[] owners = new Object[MaxBeams];

        // Le temps du jeu repart de 0 à chaque Play, et les statiques sont gardés sans rechargement du domaine :
        // sans ce vidage, de vieilles vagues réapparaîtraient
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            for (int i = 0; i < MaxBeams; i++)
            {
                beams[i] = Vector4.zero;
                times[i] = Vector4.zero;
                extras[i] = Vector4.zero;
                owners[i] = null;
            }
            Upload();
        }

        // Appelé à chaque image par un rayon qui tire : son tracé actuel, du départ au bout
        // cellsPerSecond : vitesse d'avancée du rayon, pour le sillage en V qui suit son bout (0 = pas de V)
        public static void SetBeam(Object owner, Vector2 start, Vector2 end, float cellsPerSecond = 0f)
        {
            int slot = SlotOf(owner);
            if (slot < 0)
            {
                slot = FreeSlot();
                if (slot < 0)
                    return;
                owners[slot] = owner;
                times[slot] = new Vector4(Time.time, 0f, 1f, 0f);
                extras[slot] = new Vector4(cellsPerSecond, 0f, 0f, 0f);
            }
            beams[slot] = new Vector4(start.x, start.y, end.x, end.y);
            Upload();
        }

        // Fin du tir : plus de nouvelle vague, celles déjà parties finissent de s'éloigner
        public static void EndBeam(Object owner)
        {
            int slot = SlotOf(owner);
            if (slot < 0)
                return;
            owners[slot] = null;
            times[slot].y = Time.time;
            times[slot].w = 1f;
            Upload();
        }

        // Appelé à chaque image par un projectile en vol : son trajet, du départ à sa position actuelle.
        // Sillage propre au tir (grosse boule) : la vague part à startPixels de la ligne du tir (bord de la boule),
        // s'écarte jusqu'à rangePixels en duration secondes ; rangePixels = 0 : réglages du matériau (Ki Blast)
        public static void SetProjectile(Object owner, Vector2 start, Vector2 current,
            float startPixels = 0f, float rangePixels = 0f, float duration = 0f)
        {
            int slot = SlotOf(owner);
            if (slot < 0)
            {
                slot = FreeSlot();
                if (slot < 0)
                    return;
                owners[slot] = owner;
                times[slot] = new Vector4(Time.time, Time.time, 2f, 0f);
            }
            extras[slot] = new Vector4(startPixels, rangePixels, duration, 0f);
            beams[slot] = new Vector4(start.x, start.y, current.x, current.y);
            times[slot].y = Time.time;
            Upload();
        }

        // Projectile disparu : son trajet reste tel quel, le sillage finit de s'écarter
        public static void EndProjectile(Object owner)
        {
            int slot = SlotOf(owner);
            if (slot < 0)
                return;
            owners[slot] = null;
            times[slot].w = 1f;
            Upload();
        }

        // Explosion : ringCount anneaux partent à startPixels du centre et s'élargissent jusqu'à maxPixels en duration secondes
        public static void AddRing(Vector2 center, float startPixels, float maxPixels, float duration, int ringCount = 3)
        {
            int slot = FreeSlot();
            if (slot < 0)
                return;
            owners[slot] = null;
            beams[slot] = new Vector4(center.x, center.y, center.x, center.y);
            // Déjà « finie » : la place peut être reprise par un tir suivant, les plus anciennes d'abord
            times[slot] = new Vector4(Time.time, Time.time, 3f, 1f);
            extras[slot] = new Vector4(startPixels, maxPixels, duration, ringCount);
            Upload();
        }

        // Explosion sur la terre : tache brûlée ronde de radiusPixels autour du centre, qui s'efface en duration secondes
        // (dessinée par le shader sur le sol hors eau ; occupe une place comme une onde)
        public static void AddScorch(Vector2 center, float radiusPixels, float duration)
        {
            int slot = FreeSlot();
            if (slot < 0)
                return;
            owners[slot] = null;
            beams[slot] = new Vector4(center.x, center.y, center.x, center.y);
            // Déjà « finie » : la place peut être reprise par un tir suivant, les plus anciennes d'abord
            times[slot] = new Vector4(Time.time, Time.time, 4f, 1f);
            extras[slot] = new Vector4(radiusPixels, 0f, duration, 0f);
            Upload();
        }

        private static int SlotOf(Object owner)
        {
            for (int i = 0; i < MaxBeams; i++)
            {
                // Même objet exactement (le == d'Unity confondrait deux objets détruits)
                if (!ReferenceEquals(owner, null) && ReferenceEquals(owners[i], owner))
                    return i;
            }
            return -1;
        }

        // Place jamais utilisée, sinon celle du tir fini depuis le plus longtemps ; -1 si tous sont encore en cours
        private static int FreeSlot()
        {
            int best = -1;
            for (int i = 0; i < MaxBeams; i++)
            {
                if (times[i].z < 0.5f)
                    return i;
                if (times[i].w > 0.5f && (best < 0 || times[i].y < times[best].y))
                    best = i;
            }
            return best;
        }

        private static void Upload()
        {
            Shader.SetGlobalVectorArray(BeamsId, beams);
            Shader.SetGlobalVectorArray(BeamTimesId, times);
            Shader.SetGlobalVectorArray(BeamExtrasId, extras);
        }
    }
}
