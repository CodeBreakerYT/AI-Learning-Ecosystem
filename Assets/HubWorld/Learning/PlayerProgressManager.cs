using System;
using System.Collections.Generic;
using AILearningEcosystem.Hub;
using UnityEngine;

namespace AILearningEcosystem.Learning
{
    /// <summary>
    /// Persistent (PlayerPrefs-backed, same pattern QuestLog.cs already uses
    /// in this project) per-concept mastery tracking - the data DifficultyManager
    /// and ConvAIManager both draw on to answer "what does this player
    /// actually struggle with" across sessions, not just within one round.
    /// Deliberately simple (JSON blob in one PlayerPrefs key) rather than a
    /// real save-file system - matches the project's existing persistence
    /// approach (QuestLog, HuggingFaceChatConfig) rather than introducing a
    /// new one.
    ///
    /// Scoped by UserSession.CurrentUserId rather than one fixed key - this
    /// is meant to be personalized per learner, not a single shared blob
    /// every player on the build overwrites. The cache is rebuilt whenever
    /// the resolved key changes (e.g. a different user logs in this session).
    /// </summary>
    public static class PlayerProgressManager
    {
        private const string PrefsKeyPrefix = "LearningProgress_v1_";
        private static string PrefsKey => PrefsKeyPrefix + UserSession.CurrentUserId;

        [Serializable]
        private class ConceptRecord
        {
            public string concept;
            public int attempts;
            public int correct;
            public float lastSeenUnixTime;
        }

        [Serializable]
        private class ProgressBlob
        {
            public List<ConceptRecord> records = new List<ConceptRecord>();
        }

        private static ProgressBlob _cache;
        private static string _cacheKey;

        private static ProgressBlob Data
        {
            get
            {
                var key = PrefsKey;
                if (_cache != null && _cacheKey == key) return _cache;

                var json = PlayerPrefs.GetString(key, "");
                _cache = string.IsNullOrEmpty(json) ? new ProgressBlob() : JsonUtility.FromJson<ProgressBlob>(json);
                _cache ??= new ProgressBlob();
                _cacheKey = key;
                return _cache;
            }
        }

        public static void RecordAttempt(string concept, bool correct)
        {
            if (string.IsNullOrEmpty(concept)) return;

            var record = Data.records.Find(r => r.concept == concept);
            if (record == null)
            {
                record = new ConceptRecord { concept = concept };
                Data.records.Add(record);
            }
            record.attempts++;
            if (correct) record.correct++;
            record.lastSeenUnixTime = (float)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            Save();
        }

        /// <summary>0..1, or -1 if the concept has never been attempted.</summary>
        public static float MasteryOf(string concept)
        {
            var record = Data.records.Find(r => r.concept == concept);
            if (record == null || record.attempts == 0) return -1f;
            return (float)record.correct / record.attempts;
        }

        /// <summary>Concepts with at least minAttempts tries and mastery below threshold - what ConvAIManager/DifficultyManager use to flag weak areas.</summary>
        public static List<string> WeakConcepts(float masteryThreshold = 0.6f, int minAttempts = 2)
        {
            var weak = new List<string>();
            foreach (var record in Data.records)
                if (record.attempts >= minAttempts && (float)record.correct / record.attempts < masteryThreshold)
                    weak.Add(record.concept);
            return weak;
        }

        private static void Save()
        {
            PlayerPrefs.SetString(_cacheKey, JsonUtility.ToJson(_cache));
            PlayerPrefs.Save();
        }
    }
}
