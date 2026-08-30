using UnityEngine;
using UnityEngine.SceneManagement;
using AILearningEcosystem.Learning;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Boots one of the standalone Math minigame scenes (Assets/PlatformScenes/
    /// Math/Addition.unity, Subtraction.unity, Multiplication.unity). Each
    /// scene now hosts a DIFFERENT minigame (matching the design doc's "3
    /// distinct Math minigames", not 3 flavors of the same one) - the field
    /// is still typed as EquationEscapeRoomGame.Track purely so each scene's
    /// already-serialized enum index (0/1/2) needs no scene edit; Addition=0
    /// selects Equation Escape Room, Subtraction=1 selects Math Cannon,
    /// Multiplication=2 selects Geometry Builder.
    /// </summary>
    public class MathTopicSceneStarter : MonoBehaviour
    {
        public EquationEscapeRoomGame.Track topic = EquationEscapeRoomGame.Track.Addition;

        [Header("Math Cannon cannon art (ported from ref/VR-Mathipia's RayGun)")]
        public GameObject cannonRayGunModel;
        public Material cannonRayGunMaterial;

        private void Start()
        {
            EnsureEventSystem();
            NavTabBar.Build(transform);

            switch (topic)
            {
                case EquationEscapeRoomGame.Track.Subtraction:
                    StartMathCannon();
                    break;
                case EquationEscapeRoomGame.Track.Multiplication:
                    StartGeometryBuilder();
                    break;
                default:
                    StartEscapeRoom();
                    break;
            }
        }

        private void StartEscapeRoom()
        {
            var game = gameObject.AddComponent<EquationEscapeRoomGame>();
            game.onComplete = (score, total) =>
            {
                ConvaiGuide.Speak($"You escaped! You scored {score} out of {total}.");
                QuestLog.MarkComplete(SceneManager.GetActiveScene().name);
            };
            // Instructions first, game start delayed - StartWith() immediately
            // speaks its own round-intro line, which overwrote this welcome/
            // how-to-play message the same frame before it could ever be read
            // (confirmed live - "no instructions nothing"). See the delay
            // constant's doc comment on WelcomeDelaySeconds below.
            ConvaiGuide.Speak("Welcome to the Equation Escape Room. Grab weight stones and load them onto the left pan until the scale balances against the target on the right, then pull the lever to open the door.");
            StartCoroutine(DelayedStart(() => game.StartWith(EquationEscapeRoomGame.Track.Addition)));
        }

        private void StartMathCannon()
        {
            // MathCannonGame now builds its set-dressing once and persists it
            // in the scene (see its own [ExecuteAlways] Awake) so it's
            // editable in the Scene view - reuse the pre-placed component on
            // this same GameObject if one's already there instead of always
            // spawning a fresh instance, which would silently discard any
            // in-Editor position edits every time this scene starts.
            var game = GetComponent<MathCannonGame>();
            if (game == null) game = gameObject.AddComponent<MathCannonGame>();
            if (game.rayGunModel == null) game.rayGunModel = cannonRayGunModel;
            if (game.rayGunMaterial == null) game.rayGunMaterial = cannonRayGunMaterial;
            game.onComplete = (score, total) =>
            {
                ConvaiGuide.Speak($"Nice shooting! You hit {score} out of {total} targets.");
                QuestLog.MarkComplete(SceneManager.GetActiveScene().name);
            };
            ConvaiGuide.Speak("Welcome to the Math Cannon - today it's trigonometry. Grab the cannon, set its angle, and pull the trigger to answer.");
            StartCoroutine(DelayedStart(() => game.StartWith()));
        }

        private void StartGeometryBuilder()
        {
            var game = gameObject.AddComponent<GeometryBuilderGame>();
            game.onComplete = (score, total) =>
            {
                ConvaiGuide.Speak($"Great building! You matched {score} out of {total} shapes.");
                QuestLog.MarkComplete(SceneManager.GetActiveScene().name);
            };
            ConvaiGuide.Speak("Welcome to Geometry Builder. Drag the vertices to match the target angles, then pull the lever to check your shape.");
            StartCoroutine(DelayedStart(() => game.StartWith()));
        }

        // Gives the welcome/how-to-play line time to actually be read before
        // the minigame's own round-intro line replaces it in the same caption
        // bubble.
        private const float WelcomeDelaySeconds = 4.5f;

        private System.Collections.IEnumerator DelayedStart(System.Action startGame)
        {
            yield return new WaitForSeconds(WelcomeDelaySeconds);
            startGame();
        }
    }
}
