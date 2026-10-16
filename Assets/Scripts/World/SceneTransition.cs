using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DBZ.World
{
    // Passage d'une porte : fondu au noir, chargement de la scène cible (ou simple déplacement si la porte mène
    // ailleurs dans la même scène), puis retour de l'image.
    // Créé tout seul au premier passage de porte et gardé d'une scène à l'autre (rien à poser dans les scènes).
    // Dans une nouvelle scène, le joueur vient chercher son arrivée (TryTakeArrival) à son Start.
    public class SceneTransition : MonoBehaviour
    {
        // D'où vient le joueur : de quoi retrouver la porte d'arrivée et le sens dans lequel il ressort
        public struct Arrival
        {
            // Scène de départ et nom du passage : la porte d'arrivée est celle qui y ramène
            public string FromScene;
            public string LinkName;
            // Case de la porte prise, pour l'exclure quand la porte mène ailleurs dans la même scène
            public Vector3Int EntryCell;
            // Sens de la marche en entrant : on ressort dans ce même sens (sauf sortie forcée sur la porte d'arrivée)
            public Vector2 Direction;
        }

        private static SceneTransition instance;
        // Arrivée attendue dans la scène chargée (null = pas d'arrivée par une porte)
        private static Arrival? pendingArrival;

        // Vrai du début du fondu au noir au début du retour de l'image : le joueur est bloqué.
        // Il reprend la main dès que l'image commence à revenir (moins d'attente)
        public static bool IsTransitioning { get; private set; }
        // Vrai jusqu'à la fin du retour de l'image : pas de nouvelle transition avant (un seul fondu à la fois)
        private static bool isBusy;
        public static bool IsBusy => isBusy;

        private Image fadeImage;

        // Remise à zéro à chaque lancement du mode Play : avec « Disable Domain Reload » (Enter Play Mode Options),
        // les valeurs statiques sont gardées d'un lancement à l'autre. Un Play arrêté en plein fondu laissait
        // IsTransitioning à vrai, et le joueur restait bloqué au lancement suivant
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            pendingArrival = null;
            IsTransitioning = false;
            isBusy = false;
        }

        // Passe par une porte vers targetScene. Faux si une transition est en cours ou si la scène n'existe pas.
        // fadeOutDuration / fadeInDuration : durées du fondu au noir et du retour de l'image, en secondes
        // (réglées sur le PlayerController ; la mort allonge le fondu au noir pour que la chute se joue pendant ce temps).
        // reload : recharge la scène même si c'est celle où l'on est (mort de Goku : ennemis et rochers remis en place)
        public static bool Go(string targetScene, Arrival arrival, float fadeOutDuration, float fadeInDuration, bool reload = false)
        {
            if (isBusy)
                return false;

            if (string.IsNullOrEmpty(targetScene) || !Application.CanStreamedLevelBeLoaded(targetScene))
            {
                Debug.LogWarning($"Porte : la scène « {targetScene} » n'existe pas ou n'est pas dans la liste des scènes du build.");
                return false;
            }

            if (instance == null)
                instance = Create();

            IsTransitioning = true;
            isBusy = true;
            instance.StartCoroutine(instance.Transition(targetScene, arrival, reload, fadeOutDuration, fadeInDuration));
            return true;
        }

        // Porte vers la même scène : pas de rechargement (les cibles et les rochers cassés restent comme ils sont)
        public static bool IsSameScene(string sceneName)
        {
            return string.Equals(sceneName, SceneManager.GetActiveScene().name, System.StringComparison.OrdinalIgnoreCase);
        }

        // Arrivée dans la scène qui vient d'être chargée, donnée une seule fois
        public static bool TryTakeArrival(out Arrival arrival)
        {
            arrival = pendingArrival.GetValueOrDefault();
            bool hasArrival = pendingArrival.HasValue;
            pendingArrival = null;
            return hasArrival;
        }

        // Objet gardé entre les scènes, avec un écran noir par-dessus toute l'interface
        private static SceneTransition Create()
        {
            var go = new GameObject("SceneTransition");
            DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var imageObject = new GameObject("Fade");
            imageObject.transform.SetParent(go.transform, false);
            var image = imageObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var transition = go.AddComponent<SceneTransition>();
            transition.fadeImage = image;
            return transition;
        }

        private IEnumerator Transition(string targetScene, Arrival arrival, bool reload, float fadeOutDuration, float fadeInDuration)
        {
            yield return Fade(0f, 1f, fadeOutDuration);

            if (IsSameScene(targetScene) && !reload)
            {
                var player = FindAnyObjectByType<PlayerController>();
                if (player != null)
                    player.ArriveInSameScene(arrival);
                // La caméra se place directement sur le joueur, sans traverser la carte
                foreach (var cinemachineCamera in FindObjectsByType<CinemachineCamera>())
                    cinemachineCamera.PreviousStateIsValid = false;
            }
            else
            {
                pendingArrival = arrival;
                yield return SceneManager.LoadSceneAsync(targetScene);
            }

            // Une image pour que le joueur et la caméra se placent avant de montrer la scène
            yield return null;

            IsTransitioning = false;
            yield return Fade(1f, 0f, fadeInDuration);
            isBusy = false;
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                fadeImage.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / duration));
                yield return null;
            }
            fadeImage.color = new Color(0f, 0f, 0f, to);
        }
    }
}
