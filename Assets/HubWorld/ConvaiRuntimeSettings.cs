using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// One place to change which Convai character teachers speak as, and which
    /// Convai API key the whole session uses - without editing per-scene
    /// MinigameTeacher fields or the checked-in (deliberately empty)
    /// Assets/Resources/ConvaiAPIKey.asset by hand. Lives on a GameObject in
    /// StartScene (runs once, before any other scene loads). Leave a field
    /// blank to keep that scene's/asset's own existing value.
    ///
    /// NOTE: whatever you type into these fields gets saved into StartScene's
    /// own scene file the moment you save the scene in the Editor, same as any
    /// other Inspector field - don't commit a real API key this way if this
    /// repo is ever made public; leave it blank locally instead and paste it
    /// in per-machine.
    /// </summary>
    public class ConvaiRuntimeSettings : MonoBehaviour
    {
        [Header("Convai character ID per subject's teacher (blank = keep that scene's own MinigameTeacher.teacherCharacterID)")]
        [SerializeField] private string mathTeacherCharacterID;
        [SerializeField] private string physicsTeacherCharacterID;
        [SerializeField] private string chemistryTeacherCharacterID;

        [Header("Convai API key (blank = keep Assets/Resources/ConvaiAPIKey.asset's own key)")]
        [SerializeField] private string apiKey;

        private void Awake()
        {
            if (!string.IsNullOrEmpty(mathTeacherCharacterID))
                TeacherConvaiConfig.OverrideMathCharacterID = mathTeacherCharacterID;
            if (!string.IsNullOrEmpty(physicsTeacherCharacterID))
                TeacherConvaiConfig.OverridePhysicsCharacterID = physicsTeacherCharacterID;
            if (!string.IsNullOrEmpty(chemistryTeacherCharacterID))
                TeacherConvaiConfig.OverrideChemistryCharacterID = chemistryTeacherCharacterID;

            if (!string.IsNullOrEmpty(apiKey))
            {
                var keyAsset = Resources.Load<ConvaiAPIKeySetup>("ConvaiAPIKey");
                if (keyAsset != null) keyAsset.APIKey = apiKey;
            }
        }
    }

    /// <summary>
    /// Session-wide per-subject teacher character ID overrides, set by
    /// ConvaiRuntimeSettings in StartScene. Plain static fields survive scene
    /// loads on their own - no DontDestroyOnLoad GameObject needed for value
    /// types. MinigameTeacher.BuildTeacher picks the right one for its own
    /// scene by checking which PlatformScenes/{Math,Physics,Chemistry}/
    /// subfolder it was loaded from.
    /// </summary>
    public static class TeacherConvaiConfig
    {
        public static string OverrideMathCharacterID;
        public static string OverridePhysicsCharacterID;
        public static string OverrideChemistryCharacterID;
    }
}
