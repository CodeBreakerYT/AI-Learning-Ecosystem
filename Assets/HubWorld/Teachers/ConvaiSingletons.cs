using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.UI;
using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Convai's runtime is normally bootstrapped in one shot by the
    /// "Convai Essentials - XR" prefab, which places every global singleton
    /// (ConvaiNPCManager, ConvaiInputManager, ConvaiGRPCAPI, ...) into the
    /// scene together. This project deliberately never includes that prefab
    /// (a pre-existing broken reference upstream - see AITutorSetup.cs), so
    /// every one of those singletons is otherwise silently missing and
    /// crashes the first ConvaiNPC that dereferences it. Call EnsureAll()
    /// once, early (Awake, before any ConvaiNPC's own Awake/OnEnable), from
    /// any scene that places a ConvaiNPC at runtime.
    /// </summary>
    public static class ConvaiSingletons
    {
        // Call from Awake(), before any ConvaiNPC's own Awake/OnEnable runs (those
        // dereference ConvaiNPCManager.Instance unconditionally).
        public static void EnsureCore()
        {
            // Each creation is independent and must not block the other - confirmed
            // live that ConvaiInputManager.OnEnable() throws (its generated Input
            // System Controls wrapper hits the same broken Input System<->OpenXR
            // bridge documented on LegacyXRInputBridge). Only
            // ConvaiNPC.characterID/isCharacterActive checks and TeacherPushToTalk
            // actually read ConvaiInputManager afterward (IsTalkKeyHeld, a plain
            // auto-property), so a broken _controls setup here is harmless to us.
            Create(() => ConvaiNPCManager.Instance == null, () => new GameObject("Convai NPC Manager").AddComponent<ConvaiNPCManager>());
            Create(() => ConvaiInputManager.Instance == null, () => new GameObject("Convai Input Manager").AddComponent<ConvaiInputManager>());
            // MicrophoneManager's private constructor dereferences UISaveLoadSystem.Instance
            // unconditionally (UISaveLoadSystem.cs:20) - normally placed by the excluded
            // "Convai Essentials - XR" prefab. Without it, the very first StopListening/
            // StartListening call throws a NullReferenceException instead of recording.
            Create(() => UISaveLoadSystem.Instance == null, () => new GameObject("UI Save Load System").AddComponent<UISaveLoadSystem>());
        }

        // Call from Start(), not Awake() - confirmed live that ConvaiGRPCAPI silently
        // fails to initialize (Instance stays null, no exception surfaces) when
        // AddComponent'd from inside another object's own Awake(), but works fine
        // from Start() or later. Not needed before then anyway: nothing touches
        // ConvaiGRPCAPI until a real TriggerSpeech/StartListening call happens.
        // ConvaiGRPCAPI has been observed getting destroyed unexpectedly during
        // Editor testing (its own duplicate-instance guard in Awake() firing
        // against a stale Instance reference - exact trigger not pinned down, but
        // recreating on demand is a robust, harmless mitigation regardless of
        // cause). Callers that actually need it (TeacherPushToTalk, QuestGiver.Speak)
        // call this again immediately before each interaction rather than relying
        // on a single Start()-time call.
        public static void EnsureGRPCAPI()
        {
            Create(() => ConvaiGRPCAPI.Instance == null, () => new GameObject("Convai GRPC API").AddComponent<ConvaiGRPCAPI>());
        }

        private static void Create(System.Func<bool> needsCreation, System.Action create)
        {
            if (!needsCreation()) return;
            try
            {
                create();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"ConvaiSingletons: non-fatal exception creating a singleton, continuing: {ex}");
            }
        }
    }
}
