using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Real dungeon-kit set dressing for the Math minigame scenes, replacing
    /// MinigameEnvironment's flat-colored primitive box read as "dull". Scene-
    /// placed (like MinigameEnvironment/MinigameTeacher) rather than runtime-
    /// added, since it needs real prefab references wired in the Inspector -
    /// EquationEscapeRoomGame/MathCannonGame/GeometryBuilderGame (all added
    /// via AddComponent at runtime, so they can't hold their own prefab
    /// fields) look this up via FindFirstObjectByType the same way
    /// AnswerTarget already looks up MinigameEnvironment for VFX prefabs.
    ///
    /// A previous version rebuilt this pack's DemoScene room tile-by-tile
    /// from scratch (moduleSize/roomWidthModules/etc, RecenterOnModule to
    /// work around this pack's off-center pivots). That was error-prone
    /// (wrong module size, gapped/overlapping meshes) and the user explicitly
    /// asked for the real thing instead: "why dont u just copy the DemoScene
    /// object". This version does exactly that - clonedRoomPrefab is a
    /// verbatim clone of DungeonModularPack/Scenes/DemoScene.unity's own
    /// "Models" hierarchy (every Tile/Wall/Pillar/Torch/Arch/Handrail/Step,
    /// saved as Assets/HubWorld/Games/Prefabs/DemoDungeonRoom.prefab, torch
    /// point lights included) - instantiated wholesale, not reassembled.
    /// The clone ships with zero colliders (matching the source scene), so
    /// this still fits one BoxCollider per rendered piece on instantiation.
    ///
    /// [ExecuteAlways] so the room is built and visible in the Scene view
    /// without entering Play Mode.
    /// </summary>
    [ExecuteAlways]
    public class DungeonRoomConfig : MonoBehaviour
    {
        [Header("The whole room, cloned verbatim from DungeonModularPack's own DemoScene")]
        public GameObject clonedRoomPrefab;

        [Header("Door + chest (Gridness Elementary Dungeon Pack Lite) - this pack's own")]
        [Header("DemoScene has open archways, not a working door, so one gets added here")]
        public GameObject doorPrefab;
        public GameObject chainPrefab;
        public GameObject chestBottomPrefab;
        public GameObject chestTopPrefab;

        [Header("Clutter (mixed packs) - scattered around the entry chamber, no colliders")]
        public GameObject[] clutterPrefabs;

        [Header("Weight props (real rock meshes, by denomination - NOT primitive cubes)")]
        public GameObject weightStone1Prefab;
        public GameObject weightStone2Prefab;
        public GameObject weightStone5Prefab;
        public GameObject weightStone10Prefab;

        /// <summary>Real rock prefab for a weight-stone denomination (1/2/5/10) - falls back to the next-smaller size if a slot isn't wired, never to a primitive.</summary>
        public GameObject GetWeightStonePrefab(int denomination)
        {
            if (denomination >= 10 && weightStone10Prefab != null) return weightStone10Prefab;
            if (denomination >= 5 && weightStone5Prefab != null) return weightStone5Prefab;
            if (denomination >= 2 && weightStone2Prefab != null) return weightStone2Prefab;
            if (weightStone1Prefab != null) return weightStone1Prefab;
            return weightStone10Prefab ?? weightStone5Prefab ?? weightStone2Prefab;
        }

        [Tooltip("Local position (relative to this GameObject) where the gate door is placed - the entry chamber's open side, roughly where the cloned DemoScene's own Arch pieces sit.")]
        public Vector3 doorLocalPosition = new Vector3(0f, 1.2f, -6f);

        [Tooltip("Once the player's local Z drops below this (further into the room, past the gate), the level counts as cleared.")]
        public float exitThresholdLocalZ = -8f;

        /// <summary>World position where a door/chest-style minigame should put its exit mechanism.</summary>
        public Vector3 DoorAnchor => transform.TransformPoint(doorLocalPosition);

        private void Awake()
        {
            // Already built and saved into the scene (edit-mode authoring,
            // or a Play session where Awake already ran once) - don't
            // duplicate the geometry, just restore the runtime references
            // (DoorObject) that Unity never serializes on their own.
            //
            // MinigameEnvironment.Start() rebuilds its own primitive
            // room/ring fresh every Play session regardless (it's runtime-
            // only, nothing about it persists in the scene), so the hide
            // step below has to run every time too - it used to live only
            // inside Rebuild(), which only runs once ever (the geometry
            // gets saved into the scene after the first build), leaving
            // every subsequent Play session with the primitive grass ring
            // left fully visible underneath/around the real dungeon floor
            // (confirmed live - green ground bleeding through the stone
            // tiles, glitchy mesh overlap).
            if (transform.childCount > 0)
            {
                RediscoverReferences();
                if (Application.isPlaying)
                    StartCoroutine(HidePrimitiveEnvironmentNextFrame());
                else
                    HidePrimitiveEnvironment();
                return;
            }
            Rebuild();
        }

        /// <summary>Tears down and rebuilds the whole room from the current field values - safe to call from the Inspector's "Rebuild Dungeon" button after tuning doorLocalPosition/exitThresholdLocalZ.</summary>
        public void Rebuild()
        {
            ClearBuilt();

            BuildClonedRoom();
            BuildDoorway();
            ScatterClutter();

            // MinigameEnvironment builds its primitive walls/ceiling in its
            // OWN Start(), which only ever runs in Play Mode and always
            // after every object's Awake() - hiding them here would run too
            // early and find nothing yet, leaving the old grey box fully
            // visible on top of these new dungeon meshes. A coroutine that
            // yields one frame is guaranteed to resume after every Start()
            // call this frame has completed - but coroutines don't run
            // outside Play Mode at all, so in the editor-preview path just
            // hide it immediately (nothing else is about to rebuild it).
            if (Application.isPlaying)
                StartCoroutine(HidePrimitiveEnvironmentNextFrame());
            else
                HidePrimitiveEnvironment();
        }

        private void ClearBuilt()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            DoorObject = null;
            ChainObjects = null;
        }

        // DoorObject/ChainObjects are runtime-only C# properties (never
        // serialized), so a domain reload or a fresh Editor session that
        // finds the geometry already built (childCount > 0, so Rebuild()
        // gets skipped) would otherwise leave every minigame script that
        // reads them working off stale nulls. Restore them by finding the
        // already-placed children instead of re-instantiating anything.
        private void RediscoverReferences()
        {
            if (doorPrefab != null)
            {
                var doorName = doorPrefab.name + "(Clone)";
                foreach (Transform child in transform)
                {
                    if (child.name != doorName) continue;
                    DoorObject = child.gameObject;
                    break;
                }
            }

            if (chainPrefab != null && DoorObject != null)
            {
                var chainName = chainPrefab.name + "(Clone)";
                var chains = new System.Collections.Generic.List<GameObject>();
                foreach (Transform child in DoorObject.transform)
                    if (child.name == chainName) chains.Add(child.gameObject);
                ChainObjects = chains.ToArray();
            }
        }

        private System.Collections.IEnumerator HidePrimitiveEnvironmentNextFrame()
        {
            yield return null;
            HidePrimitiveEnvironment();
        }

        private void HidePrimitiveEnvironment()
        {
            var env = FindFirstObjectByType<MinigameEnvironment>();
            if (env == null) return;

            // A dungeon room has no business showing MinigameEnvironment's
            // outdoor grass ring, scattered trees/mushrooms, or ambient VFX -
            // none of that is dungeon-themed, and a first pass that only
            // hid the 4 named structural pieces (walls/ceiling/pillars/ring)
            // left every scattered decoration clone (Tree001_V1(Clone),
            // Mushroom003(Clone), ...) fully visible, since those don't match
            // any fixed name. Hide every renderer under it unconditionally.
            foreach (var renderer in env.GetComponentsInChildren<Renderer>())
                renderer.enabled = false;

            // Primitive wall/pillar cubes carry Unity's auto-added BoxCollider,
            // sized for MinigameEnvironment's own single-room footprint -
            // dead weight now that this room has its own proper colliders.
            // Left enabled, these silently boxed the player into the wrong
            // footprint (confirmed live - "trapped in a single room").
            foreach (var col in env.GetComponentsInChildren<Collider>())
                col.enabled = false;
        }

        // Instantiates the verbatim DemoScene clone and fits one BoxCollider
        // per rendered piece - the source scene (and therefore this clone)
        // ships with zero colliders on any of it.
        private void BuildClonedRoom()
        {
            if (clonedRoomPrefab == null) return;
            var room = Instantiate(clonedRoomPrefab, transform);
            room.name = "Demo Dungeon Room";
            room.transform.localPosition = Vector3.zero;
            room.transform.localRotation = Quaternion.identity;

            foreach (Transform piece in room.transform)
            {
                if (piece.GetComponent<Renderer>() == null) continue;
                if (piece.GetComponent<Collider>() != null) continue;
                FitBoxCollider(piece.gameObject);
            }
        }

        /// <summary>True once the given world position has stepped past the gate, further into the room - the win condition, checked by distance in each minigame's Update loop.</summary>
        public bool IsPositionInExitRoom(Vector3 worldPosition)
        {
            var local = transform.InverseTransformPoint(worldPosition);
            return local.z < exitThresholdLocalZ;
        }

        private void BuildDoorway()
        {
            if (doorPrefab != null)
            {
                var door = Instantiate(doorPrefab, transform);
                door.transform.localPosition = doorLocalPosition;
                door.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                DoorObject = door;
            }

            if (chainPrefab != null && DoorObject != null)
            {
                var chainA = Instantiate(chainPrefab, DoorObject.transform);
                chainA.transform.localPosition = new Vector3(-0.3f, 1f, 0f);
                var chainB = Instantiate(chainPrefab, DoorObject.transform);
                chainB.transform.localPosition = new Vector3(0.3f, 1f, 0f);
                StripColliders(chainA);
                StripColliders(chainB);
                ChainObjects = new[] { chainA, chainB };
            }
        }

        /// <summary>Set once BuildDoorway runs - the real door GameObject the minigame can react to on solve.</summary>
        public GameObject DoorObject { get; private set; }

        /// <summary>The two chain props draped on the door - hide/drop these when the puzzle is solved.</summary>
        public GameObject[] ChainObjects { get; private set; }

        // The minigame that lives in this room always builds its interactive
        // pieces (scale, shelf, weight stones) down the room's central Z
        // axis, near local X=0 - keeping clutter to a strip near the side
        // walls guarantees it never overlaps whatever gameplay mechanism
        // ends up in that center lane, without DungeonRoomConfig needing to
        // know that mechanism's exact layout (built later, by a different
        // script). Scattered across the entry chamber only (local Z 0 to
        // -8), not the whole cloned room.
        public float centerLaneHalfWidth = 1.6f;

        private void ScatterClutter()
        {
            if (clutterPrefabs == null || clutterPrefabs.Length == 0) return;
            const float minX = -5.4f;
            const float maxX = 5.4f;

            for (var i = 0; i < 6; i++)
            {
                var prefab = clutterPrefabs[Random.Range(0, clutterPrefabs.Length)];
                if (prefab == null) continue;

                // Pick a side (left or right of the center lane) rather than
                // sampling the full width and rejecting - guarantees exactly
                // 6 placed items instead of an unbounded retry loop.
                bool leftSide = Random.value < 0.5f;
                var x = leftSide
                    ? Random.Range(minX, -centerLaneHalfWidth)
                    : Random.Range(centerLaneHalfWidth, maxX);
                var z = Random.Range(-7.4f, -0.6f);
                var item = Instantiate(prefab, transform);
                item.transform.localPosition = new Vector3(x, 0f, z);
                item.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                StripColliders(item);
            }
        }

        private static void StripColliders(GameObject go)
        {
            foreach (var col in go.GetComponentsInChildren<Collider>())
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
        }

        // Fits a BoxCollider to a GameObject's actual rendered bounds rather
        // than guessing module dimensions - robust regardless of the source
        // pack's real-world mesh size. Dividing WORLD-space AABB size by
        // lossyScale (an earlier version of this method) is only valid at
        // 0/180-degree rotations - many of this room's pieces are rotated
        // +/-90, which swaps which world axis corresponds to which local
        // axis, so that shortcut produces a collider several meters too
        // deep, mispositioned and blocking passage entirely. Transforming
        // each world-bounds corner into local space and taking the local
        // min/max is the general, rotation-correct fit.
        private static void FitBoxCollider(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var t = go.transform;
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            foreach (var r in renderers)
            {
                var b = r.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        b.center.x + ((i & 1) == 0 ? -b.extents.x : b.extents.x),
                        b.center.y + ((i & 2) == 0 ? -b.extents.y : b.extents.y),
                        b.center.z + ((i & 4) == 0 ? -b.extents.z : b.extents.z));
                    var local = t.InverseTransformPoint(corner);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
            }

            var col = go.AddComponent<BoxCollider>();
            col.center = (min + max) * 0.5f;
            col.size = max - min;
        }

        /// <summary>Call from the minigame's success handler - drops the door chains and swings the door.</summary>
        public void PlayDoorOpen()
        {
            foreach (var chain in ChainObjects ?? System.Array.Empty<GameObject>())
            {
                if (chain == null) continue;
                var rb = chain.AddComponent<Rigidbody>();
                rb.AddTorque(Random.insideUnitSphere * 2f, ForceMode.Impulse);
            }
            if (DoorObject != null)
                DoorObject.transform.localRotation *= Quaternion.Euler(0f, -70f, 0f);
        }
    }
}
