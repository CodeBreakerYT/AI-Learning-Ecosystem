using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Who's actually playing right now - the one piece of real identity
    /// StartAuthBridge gets from Firebase (AuthUser.uid) was previously
    /// thrown away right after login. PlayerProgressManager/DifficultyManager
    /// read UserSession.CurrentUserId to scope their PlayerPrefs data per
    /// learner instead of sharing one global blob. Falls back to a stable
    /// per-install GUID (not a fresh one every launch) so Editor testing and
    /// the admin/admin bypass still get consistent, personalized tracking
    /// instead of colliding into one shared "guest" bucket.
    ///
    /// DontDestroyOnLoad + GameObject.Find-by-name singleton, same pattern as
    /// NavBridge.cs.
    /// </summary>
    public class UserSession : MonoBehaviour
    {
        private const string GameObjectName = "User Session";
        private const string GuestIdPrefsKey = "LocalGuestId";

        private static string _currentUserId;

        public static string CurrentUserId
        {
            get
            {
                if (!string.IsNullOrEmpty(_currentUserId)) return _currentUserId;

                var guestId = PlayerPrefs.GetString(GuestIdPrefsKey, "");
                if (string.IsNullOrEmpty(guestId))
                {
                    guestId = System.Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(GuestIdPrefsKey, guestId);
                    PlayerPrefs.Save();
                }
                return guestId;
            }
        }

        public static void SetUser(AuthUser user)
        {
            EnsureInstance();
            _currentUserId = user != null && !string.IsNullOrEmpty(user.uid) ? user.uid : null;
        }

        private static void EnsureInstance()
        {
            if (GameObject.Find(GameObjectName) != null) return;
            var go = new GameObject(GameObjectName);
            go.AddComponent<UserSession>();
            DontDestroyOnLoad(go);
        }
    }
}
