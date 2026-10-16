using Unity.Cinemachine;
using UnityEngine;

namespace DBZ.CameraSystem
{
    // Donne à la caméra Cinemachine l'objet tagué "Player" comme cible, pour que le prefab CameraRig
    // marche dans toutes les scènes sans réglage (un prefab ne peut pas garder de lien vers un objet de la scène)
    [RequireComponent(typeof(CinemachineCamera))]
    public class CameraFollowPlayer : MonoBehaviour
    {
        private void Awake()
        {
            var cinemachineCamera = GetComponent<CinemachineCamera>();
            if (cinemachineCamera.Target.TrackingTarget != null)
                return;

            var player = GameObject.FindWithTag("Player");
            if (player == null)
            {
                Debug.LogWarning("CameraFollowPlayer : aucun objet tagué « Player » dans la scène.");
                return;
            }

            cinemachineCamera.Target.TrackingTarget = player.transform;
            // Place la caméra directement sur le joueur au lancement, sans glisser depuis la position du prefab
            cinemachineCamera.PreviousStateIsValid = false;
        }
    }
}
