namespace AILearningEcosystem.Learning
{
    /// <summary>
    /// Tracks which subject/minigame is currently active - the small piece of
    /// shared state DifficultyManager/PlayerProgressManager key off of and
    /// that ConvAIManager's context composition reads for phrasing ("as your
    /// chemistry teacher..."). Deliberately just a static holder, not a
    /// MonoBehaviour - each minigame scene's bootstrap sets this once in its
    /// own Start(), the same moment it already sets ConvAIManager.ActiveTutor.
    /// </summary>
    public static class SubjectManager
    {
        public static string CurrentSubject { get; private set; } = "";
        public static string CurrentMinigameId { get; private set; } = "";

        public static void SetActive(string subject, string minigameId)
        {
            CurrentSubject = subject;
            CurrentMinigameId = minigameId;
        }
    }
}
