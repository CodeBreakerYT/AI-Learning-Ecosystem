using Convai.Scripts.Runtime.Core;
using UnityEngine;
using AILearningEcosystem.Hub;

namespace AILearningEcosystem.Learning
{
    /// <summary>
    /// The single place gameplay code talks to the Convai tutor from. Wraps
    /// this project's existing Convai plumbing (ConvaiSingletons, the
    /// SetActiveConvaiNPC(null)-then-real dance TeacherPushToTalk/QuestGiver
    /// already rely on to force a cancellation-token rebuild) rather than
    /// re-deriving it - see ConvaiSingletons.cs and TeacherPushToTalk.cs for
    /// why each of those steps exists.
    ///
    /// IMPORTANT design note (flagging rather than silently assuming): Convai's
    /// TriggerSpeech(string) makes the NPC literally speak the given text - it
    /// is NOT a call to Convai's LLM asking it to generate a reply. Every
    /// method here therefore *composes* the contextual line itself, in C#,
    /// from LearningTaskData (mistake count, concept, hint level, the
    /// player's actual attempted answer) - which is what makes the output
    /// genuinely contextual instead of a canned string, without depending on
    /// Convai account/character configuration this repo can't verify. If you
    /// want truly free-form LLM-generated hints instead of templated-but-data-
    /// driven ones, that needs routing through Convai's real conversational
    /// path (StartListening/StopListening, i.e. voice) with the character's
    /// knowledge base configured in the Convai dashboard - TriggerSpeech alone
    /// can't do it. Composing here keeps hints reliable and correct (a
    /// templated hint can't hallucinate a wrong math fact) while still
    /// satisfying "not a generic chatbot" - phrasing is built from the exact
    /// numbers/concept/mistake the player just produced.
    /// </summary>
    public class ConvAIManager : MonoBehaviour
    {
        public static ConvAIManager Instance { get; private set; }

        [Tooltip("Set by whichever scene bootstrap (MinigameTeacher, ClassroomEnvironment, etc.) builds the active tutor NPC.")]
        public ConvaiNPC ActiveTutor;

        [Tooltip("Short personality line prepended to composed dialogue - swap per subject/scene for a distinct teacher voice.")]
        public string TutorPersonality = "warm, patient, a little playful";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void SetActiveTutor(ConvaiNPC npc) => ActiveTutor = npc;

        // The one channel Convai's own gRPC pipeline actually reads on every
        // real voice turn (ConvaiGRPCAPI.cs sends DynamicInfoController's
        // text alongside each GetResponse call) - this is what makes "what
        // do I do here?" spoken to the teacher answerable from the player's
        // actual live state, not a guess. TriggerSpeech below still can't do
        // this (see class doc comment); this is the other half.
        public void UpdateGameContext(LearningTaskData data)
        {
            if (ActiveTutor == null) return;
            var di = ActiveTutor.GetComponent<Convai.Scripts.Runtime.Features.DynamicInfoController>();
            if (di == null) return;

            var scoreLine = GameManager.Instance != null
                ? $"Score this session: {GameManager.Instance.Score.RoundsCorrect}/{GameManager.Instance.Score.RoundsCompleted} rounds correct so far."
                : "";
            di.SetDynamicInfo(
                $"You are tutoring a student in a VR {data.subject} minigame called {data.minigameId}. " +
                $"Their current task: {data.taskDescription}. Concept being taught: {data.concept}. " +
                $"Mistakes on this task so far: {data.mistakeCount}. {scoreLine} " +
                "If they ask what to do, explain using this exact task and concept - don't guess at a different problem.");
        }

        // ---- Public API the design doc calls for ----

        public void RequestHint(LearningTaskData data, HintManager hints)
        {
            hints.RecordFailure();
            int level = hints.Escalate();

            string line = hints.ShouldRevealAnswer
                ? ComposeReveal(data)
                : ComposeHint(data, level);

            Speak(line);
        }

        public void RequestExplanation(LearningTaskData data)
        {
            Speak(ComposeExplanation(data));
        }

        public void Encourage(LearningTaskData data)
        {
            Speak(ComposeEncouragement(data));
        }

        public void AnnounceDifficultyChange(string subject, string minigameId, int newLevel, bool increased)
        {
            Speak(increased
                ? $"You're getting good at this - let's turn it up a notch."
                : $"Let's dial it back a little and rebuild your footing.");
        }

        public void CelebrateSuccess(LearningTaskData data)
        {
            Speak(ComposeSuccess(data));
        }

        // ---- Composition (template + real gameplay data, per design note above) ----

        private string ComposeHint(LearningTaskData data, int level)
        {
            string strength = HintManager.DescribeLevel(level);
            switch (level)
            {
                case 1:
                    return $"Think about {data.concept} for a second - what's the first step here?";
                case 2:
                    return string.IsNullOrEmpty(data.playerAnswer)
                        ? $"Let's break it down. Look at {data.taskDescription} - what operation connects those numbers?"
                        : $"You tried {data.playerAnswer}. Close in spirit, but check how {data.concept} applies to {data.taskDescription} again.";
                default:
                    return $"Let's walk through it together: {data.taskDescription}. Focus on {data.concept} one step at a time, and tell me what you get after the first step.";
            }
        }

        private string ComposeReveal(LearningTaskData data)
        {
            return $"You've worked hard on this one, so let's finish it together. For {data.taskDescription}, the answer is {data.correctAnswer} - here's why: it comes from applying {data.concept} step by step. Next time you'll spot it faster.";
        }

        private string ComposeExplanation(LearningTaskData data)
        {
            return $"Here's the reasoning behind {data.taskDescription}: it's all about {data.concept}. Once you see the pattern, you can apply it anywhere it shows up.";
        }

        private string ComposeEncouragement(LearningTaskData data)
        {
            if (data.mistakeCount == 0)
                return "Nice pace - keep going, you're doing great.";
            return data.mistakeCount == 1
                ? "That one didn't land, but you're close - try again."
                : "You're still working it out, and that's exactly how learning feels. One more look.";
        }

        private string ComposeSuccess(LearningTaskData data)
        {
            if (data.hintLevel == 0)
                return $"Exactly right, and no hints needed - great grasp of {data.concept}.";
            return $"There it is! {data.correctAnswer} is correct - and now you've seen how {data.concept} works here.";
        }

        private void Speak(string line)
        {
            if (ActiveTutor == null) return;
            // See TeacherPushToTalk's doc comment - GetClient() is Convai's own
            // readiness check for whether ConvaiNPC.Start() has run yet. Without
            // this, a hint fired right as a scene loads can NRE deep inside
            // Convai's own StartRecordAudio/SendTriggerData.
            if (ActiveTutor.GetClient() == null) return;

            ConvaiSingletons.EnsureGRPCAPI();
            if (ConvaiNPCManager.Instance != null)
            {
                ConvaiNPCManager.Instance.SetActiveConvaiNPC(null);
                ConvaiNPCManager.Instance.SetActiveConvaiNPC(ActiveTutor);
            }
            ActiveTutor.TriggerSpeech(line);
        }
    }
}
