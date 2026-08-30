using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Loops a single background-music clip for the scene it's placed in.
    /// One "Music" GameObject with this component per scene, wired to a
    /// subject-appropriate clip from Assets/Scenes2/assets/music/ (World.unity
    /// keeps its own richer WorldMusicDirector instead of this - this is for
    /// every other scene, which just needs one steady looping track).
    /// </summary>
    public class SceneMusic : MonoBehaviour
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.2f;

        private void Awake()
        {
            if (clip == null) return;
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = volume;
            source.playOnAwake = true;
            source.spatialBlend = 0f;
            source.Play();
        }
    }
}
