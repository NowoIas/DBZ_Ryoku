using DBZ.World;
using UnityEngine;

namespace DBZ.Player
{
    // Sillage en V sur l'eau survolée en vol : chaque ligne droite du vol est envoyée à WaterRipples comme un
    // projectile (même dessin que le Ki Blast, shader des tilemaps). Le shader suppose une vitesse constante sur
    // un tronçon : il est donc fini à chaque arrêt ou changement de direction, et un nouveau part ensuite.
    [RequireComponent(typeof(PlayerController))]
    public class FlightWaterWake : MonoBehaviour
    {
        [Tooltip("Le vol laisse un sillage en V sur l'eau survolée (WaterRipples, shader des tilemaps)")]
        [SerializeField] private bool makesWaterRipples = true;
        [Tooltip("Distance de départ de la vague, depuis la ligne du vol (pixels) : bord du corps")]
        [SerializeField] private float wakeStartPixels = 6f;
        [Tooltip("Écartement maximal de la vague (pixels)")]
        [SerializeField] private float wakeRangePixels = 32f;
        [Tooltip("Durée de vie de la vague (secondes)")]
        [SerializeField] private float wakeDuration = 0.8f;

        private PlayerController controller;
        private Vector2 lastPosition;
        private bool hasLeg;
        private Vector2 legStart;
        private Vector2 legDirection;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
        }

        private void OnEnable()
        {
            lastPosition = transform.position;
        }

        // Changement de scène, mort : les vagues déjà parties finissent de s'écarter
        private void OnDisable()
        {
            EndLeg();
        }

        // Après le déplacement du joueur (Update)
        private void LateUpdate()
        {
            Vector2 position = transform.position;
            Vector2 delta = position - lastPosition;
            lastPosition = position;

            // Plus d'une case d'un coup = déplacé par une porte, pas un vol
            bool isFlyingForward = makesWaterRipples && controller.IsFlying && !controller.IsGhost
                && delta.sqrMagnitude > 0.000001f && delta.sqrMagnitude < 1f;
            if (!isFlyingForward)
            {
                EndLeg();
                return;
            }

            bool isHorizontal = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y);
            Vector2 direction = isHorizontal ? new Vector2(Mathf.Sign(delta.x), 0f) : new Vector2(0f, Mathf.Sign(delta.y));
            if (!hasLeg || direction != legDirection)
            {
                EndLeg();
                hasLeg = true;
                legDirection = direction;
                // Départ sur la ligne du nouveau tronçon (virage pris dans la même image : pas de biais)
                Vector2 previous = position - delta;
                legStart = isHorizontal ? new Vector2(previous.x, position.y) : new Vector2(position.x, previous.y);
            }
            WaterRipples.SetProjectile(this, legStart, position, wakeStartPixels, wakeRangePixels, wakeDuration);
        }

        private void EndLeg()
        {
            if (!hasLeg)
                return;
            hasLeg = false;
            WaterRipples.EndProjectile(this);
        }
    }
}
