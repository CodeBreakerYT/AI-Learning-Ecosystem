using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.Features;
using Convai.Scripts.Runtime.Features.LipSync;
using Convai.Scripts.Runtime.Features.LipSync.Models;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// A quest-giver NPC in World.unity: poses a real-life problem tied to one
    /// of the existing minigame scenes, and on "Accept Quest" loads it -
    /// exactly like HubBootstrap.LoadMinigame does, just reached by walking up
    /// to an NPC instead of clicking a picker button. Uses the same
    /// XRSimpleInteractable select interaction as AnswerTarget.cs (point ray,
    /// pull trigger).
    ///
    /// Speech runs on real Convai conversation (same component stack as
    /// ClassroomEnvironment/MinigameTeacher's BuildTeacher, plus
    /// TeacherPushToTalk for VR hold-to-talk) - by explicit request, even
    /// though Convai's NPC runtime is excluded from WebGL builds entirely (no
    /// WebAssembly gRPC). Works for Editor/native/Quest Link testing; will go
    /// silent in an actual WebGL deploy.
    /// </summary>
    public class QuestGiver : MonoBehaviour
    {
        public string npcName = "Quest Giver";
        public string subjectLabel = "Math";
        [TextArea] public string questBlurb = "Help me solve this problem.";
        public string targetScene;
        public Color accentColor = new Color(0.357f, 0.549f, 1f);
        public string characterID;

        private GameObject _panelRoot;
        private TMP_Text _statusText;
        private TMP_Text _blurbText;
        private TMP_Text _actionButtonLabel;
        private ConvaiNPC _npc;

        private void Awake()
        {
            // Must run before any other NPC's OnEnable dereferences these singletons -
            // see ConvaiSingletons for why this is needed and why it has to be this
            // early.
            ConvaiSingletons.EnsureCore();
        }

        private void Start()
        {
            // Not in Awake - confirmed live that ConvaiGRPCAPI silently fails to
            // initialize when AddComponent'd from inside another object's own
            // Awake(), see ConvaiSingletons.EnsureGRPCAPI.
            ConvaiSingletons.EnsureGRPCAPI();
            EnsureInteractable();
            EnsureConvai();
            BuildPanel();
            RefreshPanel();
        }

        private void EnsureConvai()
        {
            // Several Convai components (ConvaiActionsHandler in particular) read
            // fields like actionMethods in Awake() - fields this method only
            // assigns *after* AddComponent returns. Unlike ClassroomEnvironment/
            // MinigameTeacher (which build a brand-new, still-inactive GameObject),
            // this GameObject is already active in the scene, so AddComponent fires
            // Awake immediately. Deactivate for the duration of setup so every
            // AddComponent call here defers Awake/OnEnable until everything below
            // is fully configured, then reactivate at the end.
            gameObject.SetActive(false);

            _npc = GetComponent<ConvaiNPC>();
            if (_npc == null) _npc = gameObject.AddComponent<ConvaiNPC>();
            _npc.characterName = npcName;
            _npc.characterID = characterID;

            var lipSync = GetComponent<ConvaiLipSync>();
            if (lipSync == null)
            {
                lipSync = gameObject.AddComponent<ConvaiLipSync>();
                lipSync.FacialExpressionData.Head = new SkinMeshRendererData();
                lipSync.FacialExpressionData.Teeth = new SkinMeshRendererData();
                lipSync.FacialExpressionData.Tongue = new SkinMeshRendererData();
            }
            if (GetComponent<ConvaiHeadTracking>() == null) gameObject.AddComponent<ConvaiHeadTracking>();
            if (GetComponent<ConvaiBlinkingHandler>() == null) gameObject.AddComponent<ConvaiBlinkingHandler>();

            // Must exist before ConvaiActionsHandler is added below - its Awake()
            // dereferences this unconditionally (confirmed live: NREs otherwise).
            var dataGO = GameObject.Find("Convai Interactables Data");
            if (dataGO == null)
            {
                dataGO = new GameObject("Convai Interactables Data");
                var data = dataGO.AddComponent<ConvaiInteractablesData>();
                data.Characters = System.Array.Empty<ConvaiInteractablesData.Character>();
                data.Objects = System.Array.Empty<ConvaiInteractablesData.Object>();
            }

            var actions = GetComponent<ConvaiActionsHandler>();
            if (actions == null) actions = gameObject.AddComponent<ConvaiActionsHandler>();
            actions.actionMethods = new[]
            {
                new ConvaiActionsHandler.ActionMethod { action = "Move To", actionChoice = ActionChoice.MoveTo },
                new ConvaiActionsHandler.ActionMethod { action = "Point", animationName = "Point", actionChoice = ActionChoice.None },
            };

            var groupController = GetComponent<ConvaiGroupNPCController>();
            if (groupController != null) DestroyImmediate(groupController);

            if (GetComponent<TeacherPushToTalk>() == null)
                gameObject.AddComponent<TeacherPushToTalk>().npc = _npc;

            gameObject.SetActive(true);
        }

        private void EnsureInteractable()
        {
            var interactable = GetComponent<XRSimpleInteractable>();
            if (interactable == null) interactable = gameObject.AddComponent<XRSimpleInteractable>();

            var sphere = gameObject.AddComponent<SphereCollider>();
            sphere.radius = 0.6f;
            sphere.center = new Vector3(0f, 1f, 0f);
            sphere.isTrigger = true;

            interactable.selectEntered.AddListener(_ => OnInteract());
        }

        private void OnInteract()
        {
            RefreshPanel();
            Speak();
            _panelRoot.SetActive(true);
        }

        private void Speak()
        {
            if (_npc == null) return;
            // ConvaiNPC's gRPC client is set inside its own Start(), which Unity
            // defers a frame past this NPC's first SetActive(true) - selecting
            // this NPC before that frame passes used to NRE deep inside Convai's
            // StartRecordAudio/SendTriggerData. GetClient() is Convai's own
            // readiness check - bail out silently rather than crash.
            if (_npc.GetClient() == null) return;

            // ConvaiGRPCAPI has been observed getting destroyed unexpectedly during
            // this project's Editor testing - recreate defensively, see
            // TeacherPushToTalk's doc comment.
            ConvaiSingletons.EnsureGRPCAPI();

            // ConvaiNPC.ProcessResponse() silently drops the server's reply
            // unless isCharacterActive is true, and ConvaiGRPCAPI's cancellation
            // token only gets rebuilt on a real active-NPC change - see
            // TeacherPushToTalk's doc comment for both of these gaps.
            if (ConvaiNPCManager.Instance != null)
            {
                ConvaiNPCManager.Instance.SetActiveConvaiNPC(null);
                ConvaiNPCManager.Instance.SetActiveConvaiNPC(_npc);
            }

            // onTriggerSent is only auto-instantiated through prefab/scene
            // deserialization - this NPC is built via AddComponent<ConvaiNPC>()
            // above, which skips that, leaving it null and crashing inside
            // TriggerSpeech's own onTriggerSent.Invoke(...) call. See
            // ConvaiGuide.Speak's matching fix/comment for the confirmed trace.
            _npc.onTriggerSent ??= new ConvaiNPC.TriggerUnityEvent();

            _npc.TriggerSpeech(QuestLog.IsComplete(targetScene)
                ? "You already helped me with this - thank you! Feel free to try again anytime."
                : questBlurb);
        }

        private void BuildPanel()
        {
            var canvasGO = new GameObject("Quest Panel Canvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            canvasGO.transform.localPosition = new Vector3(0f, 2.1f, 0f);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect = canvasGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(360, 190);
            canvasGO.transform.localScale = Vector3.one * 0.0016f;

            var panel = CreatePanel(canvasGO.transform, Vector2.zero, new Vector2(360, 190), PanelColor);
            CreateText(panel.transform, npcName, 20, accentColor, TextAlignmentOptions.Center,
                new Vector2(0, 70), new Vector2(320, 28));
            _statusText = CreateText(panel.transform, subjectLabel, 14, TextDimColor, TextAlignmentOptions.Center,
                new Vector2(0, 48), new Vector2(320, 22));
            _blurbText = CreateText(panel.transform, questBlurb, 16, TextColor, TextAlignmentOptions.Center,
                new Vector2(0, 4), new Vector2(320, 80));

            var actionButton = CreateButton(panel.transform, "Accept Quest", accentColor, PrimaryTextColor,
                new Vector2(0, -68), new Vector2(240, 46), OnAcceptQuest, 18);
            _actionButtonLabel = actionButton.GetComponentInChildren<TMP_Text>();

            _panelRoot = panel.gameObject;
            _panelRoot.SetActive(false);
        }

        private void RefreshPanel()
        {
            var complete = QuestLog.IsComplete(targetScene);
            if (_statusText != null) _statusText.text = complete ? subjectLabel + " - Solved" : subjectLabel;
            if (_blurbText != null) _blurbText.text = complete ? "You already solved this one - want to try again?" : questBlurb;
            if (_actionButtonLabel != null) _actionButtonLabel.text = complete ? "Replay Quest" : "Accept Quest";
        }

        private void OnAcceptQuest()
        {
            SceneManager.LoadScene(targetScene);
        }
    }
}
