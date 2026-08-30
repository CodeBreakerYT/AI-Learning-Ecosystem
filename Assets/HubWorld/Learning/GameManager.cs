using UnityEngine;

namespace AILearningEcosystem.Learning
{
    /// <summary>
    /// Top-level orchestrator: the pipeline every minigame's SubmitAnswer
    /// ultimately runs through. This is "how gameplay data gets to ConvAI"
    /// from the design doc - a minigame never talks to ConvAIManager
    /// directly, it calls GameManager.Instance.ReportAnswer(data) and this
    /// class fans that out to ScoreManager/DifficultyManager/
    /// PlayerProgressManager/ConvAIManager consistently, so every minigame
    /// gets hints/difficulty/progress tracking "for free" just by filling in
    /// a LearningTaskData and calling this one method.
    ///
    /// Persists across scene loads (DontDestroyOnLoad) so PlayerProgressManager
    /// writes survive moving between minigames in one sitting; StartMinigameSession
    /// resets the per-session pieces (Hints/Score/Difficulty-for-this-key stays,
    /// since difficulty SHOULD carry forward within a subject) explicitly so
    /// scene transitions never leave stale hint/score state behind.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public readonly HintManager Hints = new HintManager();
        public readonly DifficultyManager Difficulty = new DifficultyManager();
        public readonly ScoreManager Score = new ScoreManager();

        private IMinigame _activeMinigame;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // Nothing in this project ever placed a GameManager (or ConvAIManager)
        // in a scene or instantiated one from code - every minigame's
        // GameManager.Instance?.ReportAnswer(...) call has been silently
        // no-op'ing project-wide (the null-conditional hid the failure
        // completely: no error, just dead hints/score/progress tracking and
        // a ConvAIManager that never ran). BeforeSceneLoad guarantees this
        // runs no matter which scene is entered first - a developer jumping
        // straight into a minigame scene during testing gets both managers
        // just as reliably as going through StartScene.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootstrapManagers()
        {
            if (Instance == null)
                new GameObject("Game Manager (Auto)").AddComponent<GameManager>();
            if (AILearningEcosystem.Learning.ConvAIManager.Instance == null)
                new GameObject("ConvAI Manager (Auto)").AddComponent<AILearningEcosystem.Learning.ConvAIManager>();
        }

        /// <summary>Call once from a minigame's InitializeGame/StartGame.</summary>
        public void StartMinigameSession(IMinigame minigame)
        {
            _activeMinigame = minigame;
            Score.Reset();
            SubjectManager.SetActive(minigame.Subject, minigame.MinigameId);
        }

        /// <summary>
        /// Refreshes the tutor's live-context text from whatever the active
        /// minigame's current round state is right now. Call this whenever a
        /// new round/problem starts (ReportAnswer already covers the
        /// post-attempt moment) so a player who asks the teacher about the
        /// very first question before attempting it still gets a grounded
        /// answer instead of stale/empty context.
        /// </summary>
        public void RefreshContext()
        {
            if (_activeMinigame == null || ConvAIManager.Instance == null) return;
            ConvAIManager.Instance.UpdateGameContext(_activeMinigame.GetLearningData());
        }

        /// <summary>
        /// The one call every minigame makes after resolving SubmitAnswer.
        /// Fans the result out to every other manager and decides whether
        /// this was a "give a hint" moment or a "celebrate" moment.
        /// </summary>
        public void ReportAnswer(LearningTaskData data)
        {
            PlayerProgressManager.RecordAttempt(data.concept, data.wasCorrect);
            Difficulty.RecordResult(data.subject, data.minigameId, data.wasCorrect, data.taskTimeSeconds, data.hintLevel);
            Score.RecordRound(data.wasCorrect, speedBonus: data.wasCorrect && data.taskTimeSeconds < 6f ? 5 : 0);

            if (ConvAIManager.Instance == null) return;

            ConvAIManager.Instance.UpdateGameContext(data);

            if (data.wasCorrect)
            {
                ConvAIManager.Instance.CelebrateSuccess(data);
                Hints.ResetForNewTask();
            }
            else
            {
                // First wrong answer just gets encouragement, not a hint dump -
                // matches the design doc's "progressively stronger hints" ask
                // rather than front-loading help on the very first miss.
                if (data.mistakeCount <= 1)
                    ConvAIManager.Instance.Encourage(data);
                else
                    ConvAIManager.Instance.RequestHint(data, Hints);
            }
        }

        /// <summary>For a minigame's explicit "ask for a hint" interaction (e.g. a Convai push-to-talk request), bypassing the mistake-count gate.</summary>
        public void RequestHintDirectly(LearningTaskData data) => ConvAIManager.Instance?.RequestHint(data, Hints);

        public void EndMinigameSession()
        {
            _activeMinigame = null;
            Hints.ResetForNewTask();
        }
    }
}
