using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Persists across every scene load (DontDestroyOnLoad) so the browser's
    /// flat-website nav bar (Assets/WebGLTemplates/EcoLearn/index.html,
    /// revealed once authenticated) can always reach Unity via
    /// unityGame.SendMessage('Nav Bridge', 'GoHome', '') no matter which
    /// minigame scene is currently loaded. Created once by StartAuthBridge on
    /// successful login - see HubNavigation.cs for what this loads.
    /// </summary>
    public class NavBridge : MonoBehaviour
    {
        private const string GameObjectName = "Nav Bridge";

        public static void EnsureInstance()
        {
            if (GameObject.Find(GameObjectName) != null) return;
            var go = new GameObject(GameObjectName);
            go.AddComponent<NavBridge>();
            Object.DontDestroyOnLoad(go);
        }

        public void GoHome(string _) => HubNavigation.GoHome();
    }
}
