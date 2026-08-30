#if UNITY_EDITOR
using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Editor-only: keeps every AudioSource muted except the one this
    /// scene's SceneMusic (or WorldMusicDirector) component owns, so Convai
    /// voice/SFX stay silent in the Editor while background music keeps
    /// playing. Runs every frame because Convai NPCs and SFX add new
    /// AudioSources at runtime, well after scene load.
    /// </summary>
    public class EditorAudioMuter : MonoBehaviour
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            new GameObject("Editor Audio Muter (Auto)").AddComponent<EditorAudioMuter>();
            DontDestroyOnLoad(GameObject.Find("Editor Audio Muter (Auto)"));
        }

        private void Update()
        {
            var sources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            foreach (var source in sources)
            {
                bool isMusic = source.GetComponent<SceneMusic>() != null
                    || source.gameObject.name.Contains("Music");
                source.mute = !isMusic;
            }
        }
    }
}
#endif
