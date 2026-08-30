using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Tracks which minigame scenes have been completed as a quest in
    /// World.unity, so a quest-giver NPC (QuestGiver.cs) can give a different
    /// line once you've already solved their problem. Backed by PlayerPrefs
    /// so it survives scene loads and sessions - completing "Addition" once
    /// stays remembered. Keyed by scene name, the same name QuestGiver's
    /// targetScene and SceneManager.LoadScene already use everywhere else in
    /// this project.
    /// </summary>
    public static class QuestLog
    {
        private const string KeyPrefix = "QuestLog_Complete_";

        /// <summary>
        /// In-memory (not PlayerPrefs) - the scene name most recently marked
        /// complete, so World.unity's WorldMusicDirector can play a one-time
        /// victory cue on the trip back and then consume/clear the flag,
        /// instead of replaying it on every future visit.
        /// </summary>
        private static string _justCompletedSceneName;

        public static void MarkComplete(string sceneName)
        {
            PlayerPrefs.SetInt(KeyPrefix + sceneName, 1);
            PlayerPrefs.Save();
            _justCompletedSceneName = sceneName;
        }

        public static bool IsComplete(string sceneName)
        {
            return PlayerPrefs.GetInt(KeyPrefix + sceneName, 0) == 1;
        }

        public static bool ConsumeJustCompleted()
        {
            if (_justCompletedSceneName == null) return false;
            _justCompletedSceneName = null;
            return true;
        }
    }
}
