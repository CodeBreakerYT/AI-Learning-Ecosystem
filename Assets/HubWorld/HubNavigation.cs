using UnityEngine;
using UnityEngine.SceneManagement;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Shared "go back to the subject picker" logic, used by both the in-VR
    /// NavTabBar (present in every minigame scene) and NavBridge (the
    /// browser's persistent flat-website nav bar, only reachable outside a
    /// headset). Loads Hub.unity, which is nothing but the subject/category
    /// picker (see HubBootstrap.cs) - there is no separate "World" scene to
    /// distinguish from it.
    /// </summary>
    public static class HubNavigation
    {
        public const string HubSceneName = "Hub";
        public const string StartSceneName = "StartScene";

        public static void GoHome()
        {
            if (SceneManager.GetActiveScene().name != HubSceneName)
            {
                Time.timeScale = 1f; // in case a pause menu left the game paused - see PauseMenu.cs
                SceneManager.LoadScene(HubSceneName);
            }
        }

        public static void GoToStartScene()
        {
            if (SceneManager.GetActiveScene().name != StartSceneName)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(StartSceneName);
            }
        }
    }
}
