using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AILearningEcosystem.Hub
{
    [Serializable]
    public class AuthUser
    {
        public string uid;
        public string email;
        public string provider;
    }

    /// <summary>
    /// Drives the real Firebase JS SDK (see Assets/Plugins/WebGL/FirebaseAuthPlugin.jslib)
    /// from C#. Firebase's own Unity SDK does not support WebGL, so every call here
    /// crosses into JS and results come back through the SendMessage callbacks below -
    /// the jslib targets this component's GameObject by name, so don't rename it after
    /// Init() runs. Outside of a WebGL build (e.g. Play Mode in the Editor) there is no
    /// JS runtime to call into, so every method reports OnError instead - the
    /// admin/admin bypass in HubBootstrap is what lets you test without Firebase.
    /// </summary>
    public class FirebaseAuthBridge : MonoBehaviour
    {
        public event Action OnReady;
        public event Action<AuthUser> OnSuccess;
        public event Action<string> OnError;
        public event Action OnCancelled;
        public event Action<AuthUser> OnSessionRestored;
        public event Action OnSessionCleared;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void FirebaseInit(string configJson, string callbackObjectName);
        [DllImport("__Internal")] private static extern void FirebaseSignIn(string email, string password);
        [DllImport("__Internal")] private static extern void FirebaseRegister(string name, string email, string password);
        [DllImport("__Internal")] private static extern void FirebaseGoogleSignIn();
        [DllImport("__Internal")] private static extern void FirebaseSignOut();
#endif

        private bool _initialized;

        public void Init(FirebaseWebConfig config)
        {
            if (config == null || !config.IsConfigured)
            {
                OnError?.Invoke("Firebase isn't configured yet. Fill in Assets/HubWorld/FirebaseWebConfig.asset with your project's web config.");
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            var json = JsonUtility.ToJson(new FirebaseConfigPayload
            {
                apiKey = config.apiKey,
                authDomain = config.authDomain,
                projectId = config.projectId,
                storageBucket = config.storageBucket,
                messagingSenderId = config.messagingSenderId,
                appId = config.appId
            });
            FirebaseInit(json, gameObject.name);
            _initialized = true;
#else
            OnError?.Invoke("Firebase sign-in only runs in a WebGL build - use the admin/admin test login while playing in the Editor.");
#endif
        }

        public void SignIn(string email, string password)
        {
            if (!RequireWebGL()) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            FirebaseSignIn(email, password);
#endif
        }

        public void Register(string displayName, string email, string password)
        {
            if (!RequireWebGL()) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            FirebaseRegister(displayName, email, password);
#endif
        }

        public void GoogleSignIn()
        {
            if (!RequireWebGL()) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            FirebaseGoogleSignIn();
#endif
        }

        public void SignOut()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_initialized) FirebaseSignOut();
#endif
        }

        private bool RequireWebGL()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!_initialized)
            {
                OnError?.Invoke("Firebase isn't ready yet - try again in a moment.");
                return false;
            }
            return true;
#else
            OnError?.Invoke("Firebase sign-in only runs in a WebGL build - use the admin/admin test login while playing in the Editor.");
            return false;
#endif
        }

        // --- Called by FirebaseAuthPlugin.jslib via SendMessage(gameObject.name, ...) ---

        public void OnFirebaseReady(string _)
        {
            OnReady?.Invoke();
        }

        public void OnAuthSuccess(string payloadJson)
        {
            OnSuccess?.Invoke(JsonUtility.FromJson<AuthUser>(payloadJson));
        }

        public void OnAuthError(string message)
        {
            OnError?.Invoke(message);
        }

        public void OnAuthCancelled(string _)
        {
            OnCancelled?.Invoke();
        }

        public void OnAuthStateChanged(string payloadJson)
        {
            if (string.IsNullOrEmpty(payloadJson))
                OnSessionCleared?.Invoke();
            else
                OnSessionRestored?.Invoke(JsonUtility.FromJson<AuthUser>(payloadJson));
        }

        [Serializable]
        private class FirebaseConfigPayload
        {
            public string apiKey;
            public string authDomain;
            public string projectId;
            public string storageBucket;
            public string messagingSenderId;
            public string appId;
        }
    }
}
