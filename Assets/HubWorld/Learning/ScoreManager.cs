namespace AILearningEcosystem.Learning
{
    /// <summary>
    /// Per-session score/accuracy bookkeeping for whichever minigame is
    /// currently running. Deliberately separate from PlayerProgressManager
    /// (session-scoped vs cross-session persistent) and from DifficultyManager
    /// (raw tally vs adaptive level) - each manager owns exactly one concern.
    /// </summary>
    public class ScoreManager
    {
        public int Score { get; private set; }
        public int RoundsCompleted { get; private set; }
        public int RoundsCorrect { get; private set; }

        public float Accuracy => RoundsCompleted == 0 ? 1f : (float)RoundsCorrect / RoundsCompleted;

        public void RecordRound(bool correct, int pointsIfCorrect = 10, int speedBonus = 0)
        {
            RoundsCompleted++;
            if (correct)
            {
                RoundsCorrect++;
                Score += pointsIfCorrect + speedBonus;
            }
        }

        public void Reset()
        {
            Score = 0;
            RoundsCompleted = 0;
            RoundsCorrect = 0;
        }
    }
}
