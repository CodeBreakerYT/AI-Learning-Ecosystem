namespace AILearningEcosystem.Learning
{
    /// <summary>
    /// Common contract every educational minigame implements, so GameManager/
    /// MinigameManager can drive any of them uniformly and ConvAIManager can
    /// always get a LearningTaskData snapshot regardless of which minigame is
    /// running. A minigame's own MonoBehaviour implements this directly
    /// (Unity doesn't need a separate wrapper class per game).
    /// </summary>
    public interface IMinigame
    {
        string MinigameId { get; }
        string Subject { get; }

        /// <summary>Builds the scene content (props, targets, UI) for a fresh session. Idempotent.</summary>
        void InitializeGame(int startingLevel);

        /// <summary>Begins the first task/round after InitializeGame.</summary>
        void StartGame();

        /// <summary>Called by the minigame's own interaction code (grab/select/socket) when the player commits an answer.</summary>
        void SubmitAnswer(string playerAnswer);

        /// <summary>Fired by the minigame after SubmitAnswer resolves as correct.</summary>
        void HandleSuccess();

        /// <summary>Fired by the minigame after SubmitAnswer resolves as incorrect.</summary>
        void HandleFailure();

        /// <summary>Ends the session (all rounds complete, or the player leaves). Reports final data to PlayerProgressManager.</summary>
        void EndGame();

        /// <summary>Current task snapshot - the payload GameManager forwards to ConvAIManager for tutor dialogue.</summary>
        LearningTaskData GetLearningData();
    }
}
