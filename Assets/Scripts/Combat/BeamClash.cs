using DBZ.World;
using UnityEngine;

namespace DBZ.Combat
{
    // Duel de deux rayons face à face (KamehamehaBeam) : ils s'arrêtent au point de contact, que le plus puissant
    // repousse vers l'autre tireur. Quand le perdant n'a plus de longueur, son rayon est battu (relâché) et le gagnant
    // reprend sa course. Pas un MonoBehaviour : créé par le premier rayon qui voit l'autre, avancé par le gagnant
    public class BeamClash
    {
        public KamehamehaBeam A { get; }
        public KamehamehaBeam B { get; }
        public KamehamehaBeam Winner { get; }
        public KamehamehaBeam Loser { get; }
        // Distance entre les départs des deux rayons (bord arrière de leur 1re case), en cases
        public float Distance { get; }
        // Point de contact, compté depuis le départ de A, en cases
        public float Point { get; private set; }

        // Boule d'énergie au point de contact (null si aucun des deux rayons n'en a) : elle avance au pixel près,
        // par-dessus les bouts des rayons (qui, eux, avancent par morceaux), et cache l'écart de hauteur entre les deux
        private SpriteRenderer ball;
        private readonly Sprite[] ballSprites;
        private readonly float ballFramesPerSecond;

        public BeamClash(KamehamehaBeam a, KamehamehaBeam b, float distance, float point)
        {
            A = a;
            B = b;
            Distance = distance;
            Point = Mathf.Clamp(point, 0f, distance);
            // Le plus puissant gagne ; à égalité, le rayon du joueur
            bool aWins = a.ClashPower > b.ClashPower || (Mathf.Approximately(a.ClashPower, b.ClashPower) && !a.FiredByEnemy);
            Winner = aWins ? a : b;
            Loser = aWins ? b : a;

            KamehamehaBeam owner = a.ClashSprites.Length > 0 ? a : b;
            ballSprites = owner.ClashSprites;
            ballFramesPerSecond = owner.ClashFramesPerSecond;
            if (ballSprites.Length > 0)
            {
                ball = new GameObject("BeamClashBall").AddComponent<SpriteRenderer>();
                ball.sortingLayerName = a.SortingLayerName;
                // Au-dessus des deux rayons (rang des impacts), au niveau de leur départ
                ball.sortingOrder = Mathf.Max(a.SortingOrder, b.SortingOrder) + 1 + a.LaunchLevel * LevelMap.SortingOrdersPerLevel;
            }
        }

        // Boule au point de contact, à mi-hauteur entre les axes des deux rayons, arrondie au pixel
        public void UpdateBall()
        {
            if (ball == null)
                return;
            Vector2 dirA = A.Direction;
            Vector3 startA = A.transform.position - (Vector3)(dirA * 0.5f);
            Vector3 pos = startA + (Vector3)(dirA * Point) + (A.AxisOffset + B.AxisOffset) / 2f;
            ball.transform.position = new Vector3(Mathf.Round(pos.x * 32f) / 32f, Mathf.Round(pos.y * 32f) / 32f, 0f);
            ball.sprite = ballSprites[(int)(Time.time * ballFramesPerSecond) % ballSprites.Length];
        }

        // Fin du duel : la boule disparaît
        public void Dispose()
        {
            if (ball != null)
                Object.Destroy(ball.gameObject);
            ball = null;
        }

        public KamehamehaBeam Other(KamehamehaBeam beam)
        {
            return beam == A ? B : A;
        }

        // Longueur laissée à ce rayon, en cases, depuis son départ
        public float LengthOf(KamehamehaBeam beam)
        {
            return beam == A ? Point : Distance - Point;
        }

        // Le gagnant pousse le point de contact vers le tireur du perdant
        public void Advance(float deltaTime)
        {
            float push = Winner.ClashPushCellsPerSecond * deltaTime;
            Point = Mathf.Clamp(Point + (Winner == A ? push : -push), 0f, Distance);
        }
    }
}
