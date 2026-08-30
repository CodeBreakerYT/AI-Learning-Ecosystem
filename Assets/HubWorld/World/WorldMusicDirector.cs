using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Background audio for World.unity, from the Complete Mysterious Forest
    /// Game Music Pack: a looped Exploration Cue as ambient music, occasional
    /// one-shot forest SFX for atmosphere, and a Victory stinger played once
    /// if `QuestLog.ConsumeJustCompleted()` says a quest was just solved
    /// (checked here on `Start()`, i.e. the trip back from a minigame).
    /// Tension/Action Battle cues from the same pack aren't wired up yet -
    /// left available for a future puzzle-timer or wrong-answer state, not
    /// needed for this first pass.
    /// </summary>
    public class WorldMusicDirector : MonoBehaviour
    {
        [Header("Ambient (looped, one picked at random)")]
        public AudioClip[] explorationCues;

        [Header("Occasional atmosphere one-shots")]
        public AudioClip[] forestAmbienceSfx;

        [Header("Played once if a quest was just completed")]
        public AudioClip victoryStinger;

        private const float MinSfxInterval = 18f;
        private const float MaxSfxInterval = 40f;

        private AudioSource _musicSource;
        private AudioSource _sfxSource;
        private float _nextSfxTime;

        private void Start()
        {
            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.loop = true;
            _musicSource.volume = 0.2f;
            _musicSource.spatialBlend = 0f;
            if (explorationCues != null && explorationCues.Length > 0)
            {
                _musicSource.clip = explorationCues[Random.Range(0, explorationCues.Length)];
                _musicSource.Play();
            }

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.spatialBlend = 0f;
            _sfxSource.volume = 0.5f;
            ScheduleNextSfx();

            if (QuestLog.ConsumeJustCompleted() && victoryStinger != null)
            {
                _sfxSource.PlayOneShot(victoryStinger, 0.7f);
            }
        }

        private void Update()
        {
            if (forestAmbienceSfx == null || forestAmbienceSfx.Length == 0) return;
            if (Time.time < _nextSfxTime) return;

            _sfxSource.PlayOneShot(forestAmbienceSfx[Random.Range(0, forestAmbienceSfx.Length)], 0.4f);
            ScheduleNextSfx();
        }

        private void ScheduleNextSfx()
        {
            _nextSfxTime = Time.time + Random.Range(MinSfxInterval, MaxSfxInterval);
        }
    }
}
