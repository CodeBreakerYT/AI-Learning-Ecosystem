using Convai.Scripts.Runtime.Core;
using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Finds whichever Convai-driven NPC is present in the scene - the
    /// MinigameTeacher-built teacher in every Math/Chemistry/Physics minigame
    /// scene, or a QuestGiver in World.unity - and makes her speak a scripted
    /// narration line. Previously looked up a fixed GameObject name
    /// ("Convai NPC Amelia"), which doesn't exist on MinigameTeacher's built
    /// NPCs, so every call was a silent no-op in every minigame scene
    /// (confirmed live - "the convoai character isnt even speaking or
    /// interacting"). Locating by component type instead of a fixed name
    /// works regardless of which scene/builder placed the NPC.
    ///
    /// Always shows a floating caption via NpcCaption in addition to
    /// triggering real Convai speech - narration text must be readable even
    /// if the voice pipeline itself doesn't produce audio for any reason
    /// (network hiccup, character not fully loaded, etc.), same reasoning as
    /// TeacherPushToTalk's GetClient() guard existing for crash-safety, not
    /// for correctness of what gets said.
    /// </summary>
    public static class ConvaiGuide
    {
        public static void Speak(string message)
        {
            var npc = Object.FindFirstObjectByType<ConvaiNPC>();
            if (npc == null) return;

            var caption = npc.GetComponent<NpcCaption>();
            if (caption == null) caption = npc.gameObject.AddComponent<NpcCaption>();
            caption.Show(message);

            // ConvaiNPC.Start() sets its private gRPC client a frame after the
            // NPC's first SetActive(true) - speaking before that window closes
            // used to NRE deep inside Convai's send path (see
            // TeacherPushToTalk's doc comment for the same guard).
            if (npc.GetClient() == null) return;

            ConvaiSingletons.EnsureGRPCAPI();

            // ConvaiNPC.ProcessResponse() drops the reply unless isCharacterActive
            // is true, and the gRPC cancellation token only rebuilds on a real
            // active-NPC change - forcing null->npc guarantees both, every call.
            if (ConvaiNPCManager.Instance != null)
            {
                ConvaiNPCManager.Instance.SetActiveConvaiNPC(null);
                ConvaiNPCManager.Instance.SetActiveConvaiNPC(npc);
            }

            // onTriggerSent is a UnityEvent field Unity only auto-instantiates
            // through prefab/scene deserialization - MinigameTeacher/QuestGiver
            // build their NPCs via AddComponent<ConvaiNPC>() at runtime, which
            // skips that step entirely, leaving it null. TriggerSpeech()
            // unconditionally calls onTriggerSent.Invoke(...) after sending,
            // so every scripted narration line NREs there and never speaks -
            // confirmed live (System.NullReferenceException at ConvaiNPC.cs:254).
            npc.onTriggerSent ??= new ConvaiNPC.TriggerUnityEvent();

            npc.TriggerSpeech(message);
        }
    }
}
