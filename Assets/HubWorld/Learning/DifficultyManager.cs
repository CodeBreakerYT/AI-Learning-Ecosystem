using System;
using System.Collections.Generic;
using AILearningEcosystem.Hub;
using UnityEngine;

namespace AILearningEcosystem.Learning
{
    /// <summary>
    /// Per-(subject, minigame) adaptive difficulty, driven by a rolling
    /// accuracy+speed score rather than a flat "3 wrong = level down" rule -
    /// keeps difficulty responsive without whiplashing on a single lucky or
    /// unlucky answer. A minigame calls RecordResult() after every task and
    /// reads CurrentLevel to decide what to generate next (bigger operands,
    /// more atoms, faster targets, etc.) - the mapping from level to actual
    /// content is entirely the minigame's own concern.
    ///
    /// Persisted to PlayerPrefs, scoped by UserSession.CurrentUserId (same
    /// per-user key pattern as PlayerProgressManager) - this instance lives on
    /// GameManager's DontDestroyOnLoad object, which survives scene changes
    /// within one play session but not a relaunch, so without this "adaptive"
    /// difficulty was forgetting every learner the moment the app closed.
    /// </summary>
    public class DifficultyManager
    {
        private const int MinLevel = 1;
        private const int MaxLevel = 5;
        private const float FastAnswerSeconds = 6f;
        private const float SlowAnswerSeconds = 20f;
        private const string PrefsKeyPrefix = "Difficulty_v1_";

        [Serializable]
        private class Track
        {
            public string subject;
            public string minigameId;
            public int level = MinLevel;
            public float rollingScore = 0.5f; // 0..1, EMA of per-task performance
        }

        [Serializable]
        private class TrackBlob
        {
            public List<Track> tracks = new List<Track>();
        }

        private readonly Dictionary<string, Track> _tracks = new Dictionary<string, Track>();
        private bool _loaded;
        private string _loadedKey;

        public int CurrentLevel(string subject, string minigameId) => Get(subject, minigameId).level;

        public void RecordResult(string subject, string minigameId, bool correct, float timeSeconds, int hintsUsed)
        {
            var track = Get(subject, minigameId);

            // A correct, fast, unhinted answer scores near 1; a wrong or heavily
            // hinted one scores near 0 - blended into a slow-moving average so a
            // single outlier can't swing the level by more than one step.
            float speedFactor = Mathf.InverseLerp(SlowAnswerSeconds, FastAnswerSeconds, timeSeconds);
            float hintPenalty = Mathf.Clamp01(hintsUsed * 0.2f);
            float taskScore = correct ? Mathf.Clamp01(0.6f + 0.4f * speedFactor - hintPenalty) : 0f;

            track.rollingScore = Mathf.Lerp(track.rollingScore, taskScore, 0.35f);

            if (track.rollingScore > 0.75f && track.level < MaxLevel)
            {
                track.level++;
                track.rollingScore = 0.5f;
            }
            else if (track.rollingScore < 0.3f && track.level > MinLevel)
            {
                track.level--;
                track.rollingScore = 0.5f;
            }

            Save();
        }

        public void Reset(string subject, string minigameId)
        {
            _tracks[Key(subject, minigameId)] = new Track { subject = subject, minigameId = minigameId };
            Save();
        }

        private Track Get(string subject, string minigameId)
        {
            EnsureLoaded();
            var key = Key(subject, minigameId);
            if (!_tracks.TryGetValue(key, out var track))
            {
                track = new Track { subject = subject, minigameId = minigameId };
                _tracks[key] = track;
            }
            return track;
        }

        private void EnsureLoaded()
        {
            var key = CurrentPrefsKey;
            if (_loaded && _loadedKey == key) return;

            _tracks.Clear();
            var json = PlayerPrefs.GetString(key, "");
            var blob = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<TrackBlob>(json);
            if (blob != null)
                foreach (var track in blob.tracks)
                    _tracks[Key(track.subject, track.minigameId)] = track;

            _loaded = true;
            _loadedKey = key;
        }

        private void Save()
        {
            var blob = new TrackBlob { tracks = new List<Track>(_tracks.Values) };
            PlayerPrefs.SetString(_loadedKey, JsonUtility.ToJson(blob));
            PlayerPrefs.Save();
        }

        private static string CurrentPrefsKey => PrefsKeyPrefix + UserSession.CurrentUserId;
        private static string Key(string subject, string minigameId) => subject + ":" + minigameId;
    }
}
