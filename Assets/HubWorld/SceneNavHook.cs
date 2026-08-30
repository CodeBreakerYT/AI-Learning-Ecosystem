using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Drop this on any GameObject in a scene that has no other AI Learning
    /// Ecosystem bootstrap script (currently just the ported Lumora
    /// NewtonsLaws.unity) to get the same persistent Subjects/World nav tab
    /// bar every other scene builds for itself in Start().
    /// </summary>
    public class SceneNavHook : MonoBehaviour
    {
        private void Start()
        {
            NavTabBar.Build(transform);
        }
    }
}
