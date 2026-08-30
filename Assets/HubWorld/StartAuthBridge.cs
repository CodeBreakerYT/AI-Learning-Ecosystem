using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// StartScene's one job: handle the real HTML/CSS login overlay
    /// (Assets/WebGLTemplates/EcoLearn/index.html - copied verbatim from EcoLearn's
    /// own frontend markup/styles.css, so it renders pixel-identical to the live
    /// site) and, once authenticated, load World. This GameObject must be named
    /// "Start Auth Bridge" - that's the exact string the overlay's JS targets via
    /// unityGame.SendMessage(...). The overlay forwards form submissions here
    /// (ReceiveLoginSubmit/ReceiveRegisterSubmit/ReceiveGoogleSignIn); all the actual
    /// logic (including the admin/admin bypass) lives in C#, the HTML only handles
    /// presentation. World.unity's Learn Hub (HubBootstrap.cs) and every minigame
    /// scene are pure Unity Canvas, loaded only after this screen authenticates -
    /// see the README for why the split is HTML here / Canvas everywhere else (a
    /// VR headset only ever renders the WebGL canvas, never the surrounding
    /// webpage, so HTML is only ever an option before a headset is attached).
    /// </summary>
    public class StartAuthBridge : MonoBehaviour
    {
        public FirebaseWebConfig firebaseConfig;
        public string nextScene = "Hub";

        private const string AdminUser = "admin";
        private const string AdminPass = "admin";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void EcoLearnHideOverlay();
        [DllImport("__Internal")] private static extern void EcoLearnSetStatus(bool isRegister, string message);
#endif

        private FirebaseAuthBridge _auth;
        private bool _pendingIsRegister;

        private void Start()
        {
            var authGO = new GameObject("FirebaseAuthBridge");
            authGO.transform.SetParent(transform, false);
            _auth = authGO.AddComponent<FirebaseAuthBridge>();
            _auth.OnSuccess += OnAuthenticated;
            _auth.OnSessionRestored += OnAuthenticated;
            _auth.OnError += message => ShowStatus(message, _pendingIsRegister);
            _auth.OnCancelled += () => ShowStatus("", _pendingIsRegister);
            _auth.Init(firebaseConfig);

#if UNITY_EDITOR
            Debug.Log("[AI Learning Ecosystem] Editor Play Mode: the HTML login screen only renders in a " +
                "real WebGL build (browsers don't run inside the Editor's Game view). Press Space here to " +
                "simulate an admin login and jump straight to World for testing.");
#endif
        }

#if UNITY_EDITOR
        // Editor-only: lets you reach World without a full WebGL build. Has zero effect
        // on any real build - the whole method is compiled out everywhere except the Editor.
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                Debug.Log("[AI Learning Ecosystem] Editor debug bypass: simulating admin login.");
                OnAuthenticated(new AuthUser { uid = "dummy-admin", email = "admin", provider = "test" });
            }
        }
#endif

        private void OnAuthenticated(AuthUser user)
        {
            NavBridge.EnsureInstance();
            UserSession.SetUser(user);
#if UNITY_WEBGL && !UNITY_EDITOR
            EcoLearnHideOverlay();
#endif
            SceneManager.LoadScene(nextScene);
        }

        private void ShowStatus(string message, bool forRegister = false)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            EcoLearnSetStatus(forRegister, message);
#else
            Debug.Log($"[AI Learning Ecosystem status{(forRegister ? " (register)" : "")}] {message}");
#endif
        }

        // ------------------------------------------------------------------
        // Called by the HTML overlay via unityGame.SendMessage('Start Auth Bridge', ...)
        // See Assets/WebGLTemplates/EcoLearn/index.html
        // ------------------------------------------------------------------

        public void ReceiveLoginSubmit(string json)
        {
            var payload = JsonUtility.FromJson<LoginPayload>(json);

            if (payload.email == AdminUser && payload.password == AdminPass)
            {
                OnAuthenticated(new AuthUser { uid = "dummy-admin", email = "admin", provider = "test" });
                return;
            }

            _pendingIsRegister = false;
            _auth.SignIn(payload.email, payload.password);
        }

        public void ReceiveRegisterSubmit(string json)
        {
            var payload = JsonUtility.FromJson<RegisterPayload>(json);
            _pendingIsRegister = true;
            _auth.Register(payload.name, payload.email, payload.password);
        }

        public void ReceiveGoogleSignIn(string json)
        {
            var payload = JsonUtility.FromJson<GoogleSignInPayload>(json);
            _pendingIsRegister = payload.tab == "register";
            _auth.GoogleSignIn();
        }

        // A real browser can't close its own tab from script (blocked for
        // security reasons - Application.Quit() silently no-ops in an
        // actual WebGL deployment, Unity itself logs a warning about this),
        // so this only does anything in a native/standalone build. Still
        // wired correctly for that case, and stops Play mode in the Editor
        // so the button is at least testable here.
        public void ReceiveQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        [Serializable] private class LoginPayload { public string email; public string password; }
        [Serializable] private class RegisterPayload { public string name; public string email; public string password; }
        [Serializable] private class GoogleSignInPayload { public string tab; }
    }
}
