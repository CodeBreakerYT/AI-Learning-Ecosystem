using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.Features;
using Convai.Scripts.Runtime.Features.LipSync;
using Convai.Scripts.Runtime.Features.LipSync.Models;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Drops a Convai-driven teacher NPC into a minigame scene. Same
    /// component stack and setup order as ClassroomEnvironment.BuildTeacher,
    /// minus the room/desk/demo-prop building that scene already has its own
    /// version of - just the teacher, plus a small local NavMesh patch so
    /// TeacherWander has somewhere to roam even in scenes with no baked
    /// NavMesh of their own.
    ///
    /// [ExecuteAlways], but NOT the same way PhoboNewtonsGuide is - the real
    /// teacher stack (ConvaiNPC/ConvaiActionsHandler/etc.) makes live gRPC
    /// calls from its own Awake()/OnEnable(), which must never fire just
    /// from having the Editor open. So edit mode only ever builds a bare
    /// visual "Teacher (Preview)" - the model at teacherSpawnPosition/
    /// teacherSpawnEuler with no Convai/NavMesh/collider components at all -
    /// so you can see and adjust her placement in the Scene view before
    /// pressing Play. Play mode swaps that preview out for the real
    /// BuildTeacher() stack, unchanged from before.
    /// </summary>
    [ExecuteAlways]
    public class MinigameTeacher : MonoBehaviour
    {
        public GameObject teacherModelPrefab;
        public RuntimeAnimatorController teacherAnimatorController;
        public string teacherCharacterName = "Teacher";
        public string teacherCharacterID;
        public Vector3 teacherSpawnPosition;
        public Vector3 teacherSpawnEuler;
        public Vector3 navMeshBakeSize = new(6f, 3f, 6f);
        // Small on purpose - the earlier default (4m, TeacherWander's own
        // default) let her drift out of the compact minigame area entirely
        // (confirmed live: ended up behind the player, out of view).
        public float wanderRadius = 1.2f;

        // The bake box is centered on teacherSpawnPosition by default, which is
        // fine for a teacher who only wanders near her own spawn. A
        // follow-the-player teacher needs the NavMesh to cover the whole
        // playable area, which can be far larger than (and off-center from)
        // her spawn point - set useCustomNavMeshCenter + navMeshBakeCenter to
        // decouple the two.
        public bool useCustomNavMeshCenter;
        public Vector3 navMeshBakeCenter;

        // When true, replaces the idle TeacherWander with TeacherFollowPlayer -
        // she paths to the player continuously instead of wandering at random.
        public bool followPlayer;

        private void Awake()
        {
            if (Application.isPlaying)
            {
                // Must run before any other NPC's OnEnable (which dereferences
                // ConvaiNPCManager.Instance unconditionally) - Unity runs Awake for
                // every initially-active object before OnEnable for any of them, so
                // Awake is the latest point this is guaranteed to win that race.
                ConvaiSingletons.EnsureCore();
                return;
            }

            if (transform.Find("Teacher (Preview)") == null)
                BuildPreview();
        }

        // Deliberately separate from Awake(): [ExecuteAlways] means Awake()
        // can fire while Application.isPlaying hasn't actually settled yet
        // during the edit-to-play transition (confirmed as a real bug
        // elsewhere in this project - PhoboNewtonsGuide's runtime kickoff
        // silently never ran when that same check lived in Awake() instead
        // of Start()).
        private void Start()
        {
            if (!Application.isPlaying) return;

            // If the preview was dragged in the Scene view, that's the real
            // intended spawn point - fold it back into
            // teacherSpawnPosition/teacherSpawnEuler (local space, same
            // convention BuildTeacher already reads) before the preview is
            // torn down, so moving her in edit mode actually changes where
            // she spawns instead of just being a look-only reference.
            var preview = transform.Find("Teacher (Preview)");
            if (preview != null)
            {
                teacherSpawnPosition = transform.InverseTransformPoint(preview.position);
                teacherSpawnEuler = (Quaternion.Inverse(transform.rotation) * preview.rotation).eulerAngles;
                DestroyImmediate(preview.gameObject);
            }

            ConvaiSingletons.EnsureGRPCAPI();
            EnsureInteractablesData();
            BakeLocalNavMesh();
            GameObject teacher = BuildTeacher();

            if (teacher != null)
            {
                var agent = teacher.GetComponent<NavMeshAgent>();
                if (agent != null && NavMesh.SamplePosition(teacher.transform.position, out var hit, 2f, NavMesh.AllAreas))
                    agent.Warp(hit.position);
            }
        }

        /// <summary>Re-places the edit-time preview at the current teacherSpawnPosition/teacherSpawnEuler fields - use after changing those in the Inspector, or if the preview ever gets out of sync with them.</summary>
        public void RebuildPreview()
        {
            var existing = transform.Find("Teacher (Preview)");
            if (existing != null) DestroyImmediate(existing.gameObject);
            BuildPreview();
        }

        // A bare visual reference, not a functional NPC - no Convai stack,
        // no NavMeshAgent, no collider. Just the model, positioned and
        // rotated exactly where the real teacher will spawn, so
        // teacherSpawnPosition/teacherSpawnEuler can be tuned by eye in the
        // Scene view instead of guessing numbers and pressing Play to check.
        private void BuildPreview()
        {
            if (teacherModelPrefab == null) return;

            var worldPos = transform.TransformPoint(teacherSpawnPosition);
            var preview = Instantiate(teacherModelPrefab, worldPos, Quaternion.Euler(teacherSpawnEuler), transform);
            preview.name = "Teacher (Preview)";

            foreach (var col in preview.GetComponentsInChildren<Collider>()) DestroyImmediate(col);
            foreach (var rb in preview.GetComponentsInChildren<Rigidbody>()) DestroyImmediate(rb);
            foreach (var groupController in preview.GetComponentsInChildren<ConvaiGroupNPCController>()) DestroyImmediate(groupController);
        }

        // ConvaiActionsHandler.Awake() logs an error whenever no ConvaiInteractablesData
        // exists in the scene, even for teachers with no demo props to register - this
        // scene has none, so an empty one just silences the false-alarm log.
        private void EnsureInteractablesData()
        {
            if (FindFirstObjectByType<ConvaiInteractablesData>() != null) return;
            var dataGO = new GameObject("Convai Interactables Data");
            var data = dataGO.AddComponent<ConvaiInteractablesData>();
            data.Characters = System.Array.Empty<ConvaiInteractablesData.Character>();
            data.Objects = System.Array.Empty<ConvaiInteractablesData.Object>();
        }

        private void BakeLocalNavMesh()
        {
            var surface = gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            var bakeCenterLocal = useCustomNavMeshCenter ? navMeshBakeCenter : teacherSpawnPosition;
            var worldCenter = transform.TransformPoint(bakeCenterLocal + new Vector3(0f, navMeshBakeSize.y / 2f, 0f));
            surface.center = transform.InverseTransformPoint(worldCenter);
            surface.size = navMeshBakeSize;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }

        private GameObject BuildTeacher()
        {
            if (teacherModelPrefab == null) return null;

            var worldPos = transform.TransformPoint(teacherSpawnPosition);
            var teacher = Instantiate(teacherModelPrefab, worldPos, Quaternion.Euler(teacherSpawnEuler), transform);
            teacher.SetActive(false);
            teacher.name = teacherCharacterName + " (Teacher)";

            var animator = teacher.GetComponent<Animator>();
            if (animator == null) animator = teacher.AddComponent<Animator>();
            animator.runtimeAnimatorController = teacherAnimatorController;

            var capsule = teacher.GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = teacher.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.radius = 0.3f;
            capsule.height = 1.8f;
            capsule.isTrigger = true;

            if (teacher.GetComponent<AudioSource>() == null) teacher.AddComponent<AudioSource>();

            var groupController = teacher.GetComponent<ConvaiGroupNPCController>();
            if (groupController != null) DestroyImmediate(groupController);

            var agent = teacher.GetComponent<NavMeshAgent>();
            if (agent == null) agent = teacher.AddComponent<NavMeshAgent>();
            agent.radius = 0.3f;
            agent.height = 1.8f;
            agent.speed = 1.4f;
            agent.stoppingDistance = 0.4f;
            agent.updateRotation = false;

            var npc = teacher.GetComponent<ConvaiNPC>();
            if (npc == null) npc = teacher.AddComponent<ConvaiNPC>();
            npc.characterName = teacherCharacterName;
            var subjectOverride = SubjectCharacterIdOverride();
            npc.characterID = string.IsNullOrEmpty(subjectOverride) ? teacherCharacterID : subjectOverride;

            var lipSync = teacher.GetComponent<ConvaiLipSync>();
            if (lipSync == null)
            {
                lipSync = teacher.AddComponent<ConvaiLipSync>();
                lipSync.FacialExpressionData.Head = new SkinMeshRendererData();
                lipSync.FacialExpressionData.Teeth = new SkinMeshRendererData();
                lipSync.FacialExpressionData.Tongue = new SkinMeshRendererData();
            }
            if (teacher.GetComponent<ConvaiHeadTracking>() == null) teacher.AddComponent<ConvaiHeadTracking>();
            if (teacher.GetComponent<ConvaiBlinkingHandler>() == null) teacher.AddComponent<ConvaiBlinkingHandler>();

            var actions = teacher.GetComponent<ConvaiActionsHandler>();
            if (actions == null) actions = teacher.AddComponent<ConvaiActionsHandler>();
            actions.actionMethods = new[]
            {
                new ConvaiActionsHandler.ActionMethod { action = "Move To", actionChoice = ActionChoice.MoveTo },
                new ConvaiActionsHandler.ActionMethod { action = "Point", animationName = "Point", actionChoice = ActionChoice.None },
                new ConvaiActionsHandler.ActionMethod { action = "Dance", animationName = "Dance", actionChoice = ActionChoice.None }
            };

            if (followPlayer)
            {
                teacher.AddComponent<TeacherFollowPlayer>();
            }
            else
            {
                var wander = teacher.AddComponent<TeacherWander>();
                wander.wanderRadius = wanderRadius;
            }
            teacher.AddComponent<TeacherActionOverlay>();
            teacher.AddComponent<TeacherPushToTalk>().npc = npc;

            // Convai's TriggerSpeech (used for scripted welcome/hint lines)
            // just makes the NPC say fixed text - it never touches the LLM.
            // DynamicInfoController is the one channel Convai's own gRPC
            // pipeline actually reads on every real voice turn (see
            // ConvaiGRPCAPI.cs), so this is what lets a player ask "what do
            // I do here?" out loud and get an answer grounded in the actual
            // live problem/score instead of a guess - see
            // AILearningEcosystem.Learning.ConvAIManager.UpdateGameContext,
            // which keeps this text current every time GameManager.ReportAnswer
            // or StartMinigameSession runs.
            if (teacher.GetComponent<Convai.Scripts.Runtime.Features.DynamicInfoController>() == null)
                teacher.AddComponent<Convai.Scripts.Runtime.Features.DynamicInfoController>();
            AILearningEcosystem.Learning.ConvAIManager.Instance?.SetActiveTutor(npc);

            teacher.SetActive(true);
            return teacher;
        }

        // No per-scene "which subject is this" field exists on MinigameTeacher
        // (adding one would mean touching all 11 scenes that use it) - the
        // scene's own asset path already encodes it via this project's
        // existing Assets/PlatformScenes/{Math,Physics,Chemistry}/ folder
        // convention, so reading that back is free and needs no scene edits.
        private string SubjectCharacterIdOverride()
        {
            var path = gameObject.scene.path;
            if (path.Contains("/PlatformScenes/Math/")) return TeacherConvaiConfig.OverrideMathCharacterID;
            if (path.Contains("/PlatformScenes/Physics/")) return TeacherConvaiConfig.OverridePhysicsCharacterID;
            if (path.Contains("/PlatformScenes/Chemistry/")) return TeacherConvaiConfig.OverrideChemistryCharacterID;
            return null;
        }
    }
}
