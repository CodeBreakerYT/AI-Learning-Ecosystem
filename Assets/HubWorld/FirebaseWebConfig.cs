using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Firebase Web SDK config for project "ai-learning-ecosystem". Left blank on
    /// purpose - paste in the real values from Firebase Console > Project Settings >
    /// General > Your apps > SDK setup and configuration. Mirrors the web app's
    /// isFirebaseConfigured fallback: HubBootstrap checks IsConfigured before wiring
    /// up auth instead of failing outright.
    /// </summary>
    [CreateAssetMenu(fileName = "FirebaseWebConfig", menuName = "AI Learning Ecosystem/Firebase Web Config")]
    public class FirebaseWebConfig : ScriptableObject
    {
        public string apiKey;
        public string authDomain;
        public string projectId;
        public string storageBucket;
        public string messagingSenderId;
        public string appId;

        public bool IsConfigured => !string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(projectId);
    }
}
