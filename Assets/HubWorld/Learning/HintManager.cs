using UnityEngine;

namespace AILearningEcosystem.Learning
{
    /// <summary>
    /// Tracks hint escalation per task, so ConvAIManager can ask for
    /// progressively stronger help without a minigame needing its own
    /// counter. This is the piece that stops the tutor from "just giving
    /// answers too easily" (design doc item 13): each level unlocks more
    /// specific help, but the final level is a worked-through explanation,
    /// never a plain "the answer is X" - the prompt template ConvAIManager
    /// builds at MaxLevel still asks Convai to walk the reasoning, not state
    /// the literal answer, unless the player has failed the SAME task
    /// FailuresBeforeMaxHint+ times (a deliberate last-resort escape valve so
    /// frustration doesn't become a hard wall).
    /// </summary>
    public class HintManager
    {
        public const int MaxLevel = 3;
        public const int FailuresBeforeMaxHint = 4;

        private int _currentLevel;
        private int _failuresThisTask;

        public int CurrentLevel => _currentLevel;
        public int FailuresThisTask => _failuresThisTask;
        public bool ShouldRevealAnswer => _failuresThisTask >= FailuresBeforeMaxHint;

        /// <summary>Call once per wrong answer on the current task, before escalating.</summary>
        public void RecordFailure() => _failuresThisTask++;

        /// <summary>Requests the next hint level up (capped at MaxLevel) and returns it.</summary>
        public int Escalate()
        {
            _currentLevel = Mathf.Min(_currentLevel + 1, MaxLevel);
            return _currentLevel;
        }

        /// <summary>Call when a task is completed or abandoned, before moving to the next one.</summary>
        public void ResetForNewTask()
        {
            _currentLevel = 0;
            _failuresThisTask = 0;
        }

        /// <summary>
        /// Hint-level -> what kind of help that level should contain. ConvAIManager
        /// feeds this label into the prompt so Convai's phrasing matches the
        /// intended strength rather than guessing from hint number alone.
        /// </summary>
        public static string DescribeLevel(int level)
        {
            switch (level)
            {
                case 0: return "no hint yet - if asked, just restate the goal encouragingly";
                case 1: return "a nudge: point at which concept or step applies, without touching numbers";
                case 2: return "a partial worked step: show ONE step of the reasoning using the player's own numbers";
                case 3: return "a full walkthrough of the reasoning up to (but not including) the final answer";
                default: return "reveal the answer and explain why, since the player has struggled repeatedly";
            }
        }
    }
}
