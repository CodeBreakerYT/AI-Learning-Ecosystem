using System;
using System.Collections.Generic;
using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.Features;
using Convai.Scripts.Runtime.Features.LipSync;
using Convai.Scripts.Runtime.Features.LipSync.Models;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Builds one subject classroom at runtime: the room + desks, a set of
    /// "demo props" the teacher can reference in conversation (registered with
    /// Convai's own ConvaiInteractablesData/ConvaiActionsHandler system - see
    /// Assets/Convai/Scripts/Runtime/Features/Actions/ - so when the AI decides
    /// to say something like "Move to Seesaw", Convai's own action parser walks
    /// the teacher there and plays the matching animation automatically), and
    /// the teacher NPC itself (Convai components + TeacherWander +
    /// TeacherActionOverlay). Same "one script, three scene instances with
    /// different Inspector values" pattern as WorldEnvironment.cs.
    ///
    /// Everything here runs at runtime (Start), not just in the Editor, because
    /// this project deploys to WebGL - NavMesh baking in particular uses
    /// NavMeshSurface.BuildNavMesh() (from com.unity.ai.navigation), which
    /// works in an actual build, unlike the Editor-only NavMeshBuilder API.
    /// </summary>
    public class ClassroomEnvironment : MonoBehaviour
    {
        [Header("Room")]
        public GameObject classroomModelPrefab;
        public GameObject deskChairPrefab;
        public Vector3[] deskChairPositions =
        {
            new(-2f, 0f, 3f), new(0f, 0f, 3f), new(2f, 0f, 3f),
            new(-2f, 0f, 5f), new(0f, 0f, 5f), new(2f, 0f, 5f)
        };

        [Header("Teacher")]
        public GameObject teacherModelPrefab;
        public RuntimeAnimatorController teacherAnimatorController;
        public string teacherCharacterName = "Teacher";
        public string teacherCharacterID;
        public Vector3 teacherSpawnPosition;
        public Vector3 teacherSpawnEuler;

        [Serializable]
        public class DemoProp
        {
            public GameObject prefab;
            public string objectName;
            [TextArea(1, 3)] public string description;
            public Vector3 position;
            public Vector3 eulerAngles;
            public Vector3 scale = Vector3.one;
        }

        [Header("Demo props (the teacher can 'Move to' / 'Pick up' these)")]
        public DemoProp[] demoProps;

        [Header("NavMesh bake area (local space, centered on this object)")]
        public Vector3 navMeshBakeSize = new(14f, 3f, 14f);

        private void Awake()
        {
            // Must run before any other NPC's OnEnable (which dereferences
            // ConvaiNPCManager.Instance unconditionally) - Unity runs Awake for
            // every initially-active object before OnEnable for any of them, so
            // Awake is the latest point this is guaranteed to win that race.
            ConvaiSingletons.EnsureCore();
        }

        private void Start()
        {
            ConvaiSingletons.EnsureGRPCAPI();
            EnsureEventSystem();
            NavTabBar.Build(transform);

            BuildRoom();
            List<(GameObject go, DemoProp data)> props = BuildDemoProps();
            BuildInteractablesData(props);
            BakeNavMesh();
            GameObject teacher = BuildTeacher();

            if (teacher != null)
            {
                var agent = teacher.GetComponent<NavMeshAgent>();
                if (agent != null && NavMesh.SamplePosition(teacher.transform.position, out var hit, 2f, NavMesh.AllAreas))
                    agent.Warp(hit.position);
            }
        }

        /// <summary>
        /// ConvaiNPC.OnEnable() dereferences ConvaiNPCManager.Instance unconditionally -
        /// a plain MonoBehaviour singleton (set in its own Awake()), not lazily
        /// self-creating. No scene in this project actually places one, which is a
        /// latent crash for every ConvaiNPC, not just these teachers (it may have gone
        /// unnoticed elsewhere due to the static Instance field surviving across Play
        /// Mode sessions without a domain reload). Belt-and-suspenders: create one here
        /// if it doesn't already exist, before any ConvaiNPC on this teacher activates.
        /// </summary>
        private void BuildRoom()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Classroom Floor Collider";
            floor.transform.SetParent(transform, false);
            floor.transform.localScale = new Vector3(navMeshBakeSize.x / 10f, 1f, navMeshBakeSize.z / 10f);
            floor.GetComponent<Renderer>().enabled = false;

            if (classroomModelPrefab != null)
                Instantiate(classroomModelPrefab, transform.position, transform.rotation, transform);

            if (deskChairPrefab != null)
                foreach (var pos in deskChairPositions)
                    Instantiate(deskChairPrefab, transform.TransformPoint(pos), transform.rotation, transform);
        }

        private List<(GameObject go, DemoProp data)> BuildDemoProps()
        {
            var list = new List<(GameObject, DemoProp)>();
            if (demoProps == null) return list;

            foreach (var prop in demoProps)
            {
                if (prop.prefab == null) continue;
                var worldPos = transform.TransformPoint(prop.position);
                var instance = Instantiate(prop.prefab, worldPos, Quaternion.Euler(prop.eulerAngles), transform);
                instance.name = prop.objectName;
                instance.transform.localScale = prop.scale;

                if (instance.GetComponent<Collider>() == null && instance.GetComponentInChildren<Collider>() == null)
                {
                    var renderers = instance.GetComponentsInChildren<Renderer>();
                    if (renderers.Length > 0)
                    {
                        var bounds = renderers[0].bounds;
                        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                        var box = instance.AddComponent<BoxCollider>();
                        box.center = instance.transform.InverseTransformPoint(bounds.center);
                        var scale = instance.transform.lossyScale;
                        box.size = new Vector3(bounds.size.x / Mathf.Max(scale.x, 0.001f),
                            bounds.size.y / Mathf.Max(scale.y, 0.001f),
                            bounds.size.z / Mathf.Max(scale.z, 0.001f));
                    }
                }

                list.Add((instance, prop));
            }

            return list;
        }

        private void BuildInteractablesData(List<(GameObject go, DemoProp data)> props)
        {
            var dataGO = new GameObject("Convai Interactables Data");
            dataGO.transform.SetParent(transform, false);
            var data = dataGO.AddComponent<ConvaiInteractablesData>();
            data.Characters = Array.Empty<ConvaiInteractablesData.Character>();

            var objects = new ConvaiInteractablesData.Object[props.Count];
            for (int i = 0; i < props.Count; i++)
            {
                objects[i] = new ConvaiInteractablesData.Object
                {
                    Name = props[i].data.objectName,
                    Description = props[i].data.description,
                    gameObject = props[i].go
                };
            }
            data.Objects = objects;
        }

        private GameObject BuildTeacher()
        {
            if (teacherModelPrefab == null) return null;

            var worldPos = transform.TransformPoint(teacherSpawnPosition);
            var teacher = Instantiate(teacherModelPrefab, worldPos, Quaternion.Euler(teacherSpawnEuler), transform);
            // Build fully deactivated: AddComponent fires Awake/OnEnable synchronously,
            // and several Convai components (ConvaiNPC, ConvaiActionsHandler) read
            // fields like characterID/actionMethods in Awake - fields this method
            // only assigns *after* AddComponent returns. Staying inactive defers all
            // lifecycle methods until SetActive(true) at the end, once everything on
            // this GameObject is fully configured.
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

            // A reused Convai demo prefab (e.g. Mike Carter) can carry a
            // ConvaiGroupNPCController for NPC-to-NPC conversations - a feature this
            // project doesn't use, whose singleton (NPC2NPCConversationManager) isn't
            // set up in any scene here. Left in place it NREs in Start(). DestroyImmediate
            // (not Destroy) because the teacher is still inactive at this point and must
            // not carry the component into SetActive(true) below, which is when its
            // lifecycle methods would actually run.
            var groupController = teacher.GetComponent<ConvaiGroupNPCController>();
            if (groupController != null) DestroyImmediate(groupController);

            var agent = teacher.GetComponent<NavMeshAgent>();
            if (agent == null) agent = teacher.AddComponent<NavMeshAgent>();
            agent.radius = 0.3f;
            agent.height = 1.8f;
            agent.speed = 1.4f;
            agent.stoppingDistance = 0.4f;
            agent.updateRotation = false;

            // Some teacher models are raw FBX (no Convai components yet); others
            // (e.g. a reused Convai demo character prefab like Mike Carter) already
            // ship with the full Convai component stack - get-or-add everywhere so
            // this works for both without duplicate-component errors.
            var npc = teacher.GetComponent<ConvaiNPC>();
            if (npc == null) npc = teacher.AddComponent<ConvaiNPC>();
            npc.characterName = teacherCharacterName;
            npc.characterID = teacherCharacterID;

            var lipSync = teacher.GetComponent<ConvaiLipSync>();
            if (lipSync == null)
            {
                lipSync = teacher.AddComponent<ConvaiLipSync>();
                // A component added purely at runtime (not via a saved/serialized
                // prefab) never goes through Unity's serialize/deserialize round-trip,
                // which is what normally instantiates default values for nested
                // [Serializable] fields like FacialExpressionData.Head/Teeth/Tongue -
                // they'd otherwise stay null and NRE in ConvaiLipSync.Start(). The
                // inner Renderer can stay null; HasUsableBlendShapes() already treats
                // that as "no lipsync for this mesh" and no-ops gracefully.
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
                new ConvaiActionsHandler.ActionMethod { action = "Pick Up", actionChoice = ActionChoice.PickUp },
                new ConvaiActionsHandler.ActionMethod { action = "Drop", actionChoice = ActionChoice.Drop },
                new ConvaiActionsHandler.ActionMethod { action = "Point", animationName = "Point", actionChoice = ActionChoice.None },
                new ConvaiActionsHandler.ActionMethod { action = "Dance", animationName = "Dance", actionChoice = ActionChoice.None }
            };

            teacher.AddComponent<TeacherWander>();
            teacher.AddComponent<TeacherActionOverlay>();
            teacher.AddComponent<TeacherPushToTalk>().npc = npc;

            teacher.SetActive(true);
            return teacher;
        }

        private void BakeNavMesh()
        {
            var surface = gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            surface.center = new Vector3(0f, navMeshBakeSize.y / 2f, 0f);
            surface.size = navMeshBakeSize;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }
    }
}
