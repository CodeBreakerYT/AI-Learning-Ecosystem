using UnityEngine;
using UnityEngine.SceneManagement;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>Boots Assets/PlatformScenes/Math/SurfaceAreaVolume.unity - Math Topic 2.</summary>
    public class SurfaceAreaVolumeSceneStarter : MonoBehaviour
    {
        public GameObject suzanneModel;
        public Material shapeMaterial;
        public Material blockMaterial;

        private const float WelcomeDelaySeconds = 4.5f;

        private void Start()
        {
            EnsureEventSystem();
            NavTabBar.Build(transform);

            var game = gameObject.AddComponent<SurfaceAreaVolumeGame>();
            game.suzanneModel = suzanneModel;
            game.shapeMaterial = shapeMaterial;
            game.blockMaterial = blockMaterial;
            game.onComplete = (score, total) =>
            {
                ConvaiGuide.Speak($"You correctly judged {score} out of {total} block-out estimates.");
                QuestLog.MarkComplete(SceneManager.GetActiveScene().name);
            };
            ConvaiGuide.Speak("Welcome to Surface Area and Volume. Watch each shape get scanned into little cubes, then tell me whether the block estimate looks right.");
            StartCoroutine(DelayedStart(() => game.StartWith()));
        }

        private System.Collections.IEnumerator DelayedStart(System.Action startGame)
        {
            yield return new WaitForSeconds(WelcomeDelaySeconds);
            startGame();
        }
    }
}
