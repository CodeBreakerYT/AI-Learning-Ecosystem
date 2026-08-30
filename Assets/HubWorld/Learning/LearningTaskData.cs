namespace AILearningEcosystem.Learning
{
    /// <summary>
    /// Everything a minigame knows about the task the player is currently on.
    /// This is the single payload every IMinigame reports up to ConvAIManager -
    /// it's the "gameplay state" the whole design doc's ConvAI-context system
    /// is built around. Minigames fill this in fresh each time they call
    /// GameManager.Report*() - it is not persisted itself (PlayerProgressManager
    /// is the persistent side).
    /// </summary>
    [System.Serializable]
    public struct LearningTaskData
    {
        public string subject;       // "Mathematics" | "Chemistry" | "Physics"
        public string minigameId;    // e.g. "EquationEscapeRoom"
        public int level;            // 1-based difficulty tier within the minigame
        public string concept;       // e.g. "linear equations", "covalent bonding"
        public string taskDescription; // human-readable statement of the current task
        public string playerAnswer;    // what the player just attempted (may be partial)
        public string correctAnswer;
        public bool wasCorrect;
        public int mistakeCount;     // mistakes on THIS task so far
        public int hintLevel;        // 0 = no hint given yet
        public float taskTimeSeconds; // time spent on this task so far
        public float sessionAccuracy; // 0-1, rolling accuracy for this minigame session
    }
}
