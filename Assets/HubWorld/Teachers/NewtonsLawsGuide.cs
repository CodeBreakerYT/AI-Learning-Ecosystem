using Convai.Scripts.Runtime.Core;
using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Bridges NewtonSimulate's on-screen typewriter text (one instance per
    /// law panel) to the following teacher's Convai voice, so she actually
    /// narrates each law as its demo plays instead of just standing nearby.
    /// Finds the teacher lazily since MinigameTeacher builds her at runtime,
    /// after this component's own Start().
    /// </summary>
    public class NewtonsLawsGuide : MonoBehaviour
    {
        private ConvaiNPC _teacherNpc;

        private void Start()
        {
            foreach (var sim in FindObjectsByType<NewtonSimulate>(FindObjectsSortMode.None))
                sim.OnStageText += OnStageText;
        }

        private void OnStageText(string text)
        {
            if (_teacherNpc == null)
            {
                var teacherGO = GameObject.Find("Mrs. Iyer (Teacher)");
                if (teacherGO != null) _teacherNpc = teacherGO.GetComponent<ConvaiNPC>();
            }
            if (_teacherNpc == null) return;
            // See TeacherPushToTalk's doc comment - GetClient() is Convai's own
            // readiness check for whether ConvaiNPC.Start() has run yet.
            if (_teacherNpc.GetClient() == null) return;

            ConvaiSingletons.EnsureGRPCAPI();
            if (ConvaiNPCManager.Instance != null)
            {
                ConvaiNPCManager.Instance.SetActiveConvaiNPC(null);
                ConvaiNPCManager.Instance.SetActiveConvaiNPC(_teacherNpc);
            }
            _teacherNpc.TriggerSpeech(text);
        }
    }
}
