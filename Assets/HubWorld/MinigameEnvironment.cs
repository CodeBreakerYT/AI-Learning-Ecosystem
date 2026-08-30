using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Enclosed-room dressing for the minigame scenes, which otherwise ship as
    /// just a Ground plane + light - a bare void with no sense of space. Four
    /// primitive walls + a subject-accent-tinted ceiling strip (no external
    /// assets, so this part can't introduce missing-asset errors) PLUS
    /// persistent ambient VFX from the Hovl Studio Magic Effects pack - a
    /// looping floor centerpiece under the question panel and one aura per
    /// corner pillar, so the room reads as alive even between interactions,
    /// not just during the momentary correct/wrong flash on AnswerTarget.
    /// </summary>
    public class MinigameEnvironment : MonoBehaviour
    {
        public float roomSize = 12f;
        public float wallHeight = 4f;
        public Color wallColor = new(0.11f, 0.13f, 0.18f);
        public Color accentColor = new(0.357f, 0.549f, 1f);

        [Header("Answer feedback VFX (Hovl Studio Magic Effects pack)")]
        public GameObject correctVfxPrefab;
        public GameObject wrongVfxPrefab;
        public GameObject roundCompleteVfxPrefab;
        // Hovl's prefabs are authored at cinematic scale (meant to fill a
        // whole screen), which reads as a giant blob at 1:1 scale in a small
        // VR room - shrink on spawn instead of re-authoring the assets.
        public float feedbackVfxScale = 0.3f;

        [Header("Ambient decoration VFX (Hovl Studio Magic Effects pack)")]
        public GameObject floorCenterpieceVfxPrefab;
        public GameObject cornerAuraVfxPrefab;
        public Vector3 floorCenterpiecePosition = new(0f, 0.05f, 2.2f);
        public float cornerPillarHeight = 1.4f;
        public float ambientVfxScale = 0.5f;

        [Header("Subject-themed static props (lined against the back wall)")]
        public GameObject[] wallPropPrefabs;
        public float wallPropScale = 1f;
        public Vector3 wallPropRotationOffset;

        [Header("Outdoor dressing (grass ring + nature props + roof) - skipped when useRealRoomAssets is on")]
        public GameObject[] outdoorPropPrefabs;
        public Color groundRingColor = new(0.192f, 0.322f, 0.164f);
        public Color roofColor = new(0.322f, 0.192f, 0.164f);
        public float outdoorRingWidth = 8f;
        public int outdoorPropCount = 18;

        [Header("Real-asset room shell (real wall/floor/pillar props instead of primitive cubes)")]
        public bool useRealRoomAssets;
        public GameObject realWallPrefab;
        public float realWallTileWidth = 4.25f;
        public GameObject realFloorPrefab;
        public GameObject realPillarPrefab;

        [Header("Whole pre-built room prefab (overrides everything above - e.g. the same sci-fi 'ship' interior kit Newton's Laws already uses)")]
        public GameObject realRoomPrefab;
        public Vector3 realRoomPivotOffset;

        private void Start()
        {
            if (realRoomPrefab != null)
            {
                BuildRealWholeRoom();
                BuildFloorCenterpiece();
                BuildAccentLight();
                return;
            }

            BuildWalls();
            BuildCornerPillars();
            BuildFloorCenterpiece();
            BuildAccentLight();
            BuildWallProps();
            if (useRealRoomAssets)
            {
                BuildRealFloor();
            }
            else
            {
                BuildAccentCeiling();
                BuildOutdoorRing();
                BuildRoof();
            }
        }

        // Drops in an entire pre-built room prefab from the kit (walls, floor,
        // doorway already assembled as one piece) instead of assembling one
        // tile-by-tile - used for Physics so its minigames sit inside the same
        // sci-fi "ship" interior kit Newton's Laws' own PhysicsLab uses. The
        // prefab's own pivot usually isn't centered on its floor, hence the
        // offset - and the default primitive "Ground" plane is hidden so it
        // doesn't poke through the real floor.
        private void BuildRealWholeRoom()
        {
            var ground = GameObject.Find("Ground");
            if (ground != null) ground.SetActive(false);

            var room = Instantiate(realRoomPrefab, transform);
            room.name = "Real Room";
            room.transform.localPosition = -realRoomPivotOffset;
        }

        // Replaces the default primitive "Ground" plane with a real lab floor
        // tile when the scene supplies one - the Ground GameObject itself is
        // left in place (other scripts may reference it), just hidden.
        private void BuildRealFloor()
        {
            if (realFloorPrefab == null) return;
            var ground = GameObject.Find("Ground");
            if (ground != null) ground.SetActive(false);

            var floor = Instantiate(realFloorPrefab, transform);
            floor.name = "Real Lab Floor";
            floor.transform.localPosition = Vector3.zero;
        }

        // A room that's just 4 flat walls dropped on the default checker
        // ground reads as a box floating in a void the moment you look past
        // the walls. A grass-tinted ground ring just outside the walls, with
        // real nature props (trees/stones/mushrooms) scattered on it, plus a
        // peaked roof silhouette overhead, makes each room read as a real
        // building sitting in an outdoor clearing instead of a bare box.
        private void BuildOutdoorRing()
        {
            var half = roomSize / 2f;
            var outerRadius = half + outdoorRingWidth;

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Outdoor Ground Ring";
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            ring.transform.localScale = new Vector3(outerRadius * 2f, 0.05f, outerRadius * 2f);
            ring.GetComponent<Renderer>().material.color = groundRingColor;
            Destroy(ring.GetComponent<Collider>());

            var usable = outdoorPropPrefabs != null
                ? System.Array.FindAll(outdoorPropPrefabs, p => p != null)
                : System.Array.Empty<GameObject>();
            if (usable.Length == 0) return;

            for (var i = 0; i < outdoorPropCount; i++)
            {
                var angle = i * (360f / outdoorPropCount) + Random.Range(-8f, 8f);
                var dist = Random.Range(half + 1.5f, outerRadius - 1f);
                var rad = angle * Mathf.Deg2Rad;
                var pos = new Vector3(Mathf.Cos(rad) * dist, 0f, Mathf.Sin(rad) * dist);

                var prop = Instantiate(usable[i % usable.Length], transform);
                prop.transform.localPosition = pos;
                prop.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                foreach (var col in prop.GetComponentsInChildren<Collider>())
                    Destroy(col);
            }
        }

        // A flat emissive ceiling strip reads as a UFO-panel ceiling, not a
        // roof. Two large tilted cubes meeting at a ridge above it gives the
        // room a simple gable-roof silhouette from outside/afar - the
        // difference between "grey box" and "a building".
        private void BuildRoof()
        {
            var half = roomSize / 2f;
            var slopeLength = Mathf.Sqrt(half * half + 1.5f * 1.5f);
            var slopeAngle = Mathf.Atan2(1.5f, half) * Mathf.Rad2Deg;

            BuildRoofSlope(new Vector3(half / 2f, wallHeight + 0.75f, 0f), slopeLength, -slopeAngle);
            BuildRoofSlope(new Vector3(-half / 2f, wallHeight + 0.75f, 0f), slopeLength, slopeAngle);
        }

        private void BuildRoofSlope(Vector3 localPos, float length, float tiltZ)
        {
            var slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slope.name = "Roof Slope";
            slope.transform.SetParent(transform, false);
            slope.transform.localPosition = localPos;
            slope.transform.localRotation = Quaternion.Euler(0f, 0f, tiltZ);
            slope.transform.localScale = new Vector3(length, 0.15f, roomSize + 0.6f);
            slope.GetComponent<Renderer>().material.color = roofColor;
            Destroy(slope.GetComponent<Collider>());
        }

        // A single colored point light does more to kill the "flat, dull room"
        // feeling than almost anything else here - the primitive walls/pillars
        // otherwise only ever see flat ambient/directional light.
        private void BuildAccentLight()
        {
            var lightGO = new GameObject("Accent Point Light");
            lightGO.transform.SetParent(transform, false);
            lightGO.transform.localPosition = new Vector3(0f, wallHeight - 0.6f, 0f);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = accentColor;
            light.intensity = 3f;
            light.range = roomSize * 0.8f;
        }

        // Lines up to 6 real subject-prop models (Chemistry glassware, Physics
        // sci-fi crates, Math nature props - whichever this scene was given)
        // along the back wall so the room reads as a themed space, not a bare
        // primitive box. Purely decorative - no colliders, so it can't block
        // the player or interactables.
        private void BuildWallProps()
        {
            if (wallPropPrefabs == null || wallPropPrefabs.Length == 0) return;

            var usable = System.Array.FindAll(wallPropPrefabs, p => p != null);
            if (usable.Length == 0) return;

            var half = roomSize / 2f - 1f;
            var spacing = roomSize / (usable.Length + 1);
            for (var i = 0; i < usable.Length; i++)
            {
                var x = -roomSize / 2f + spacing * (i + 1);
                var prop = Instantiate(usable[i], transform);
                prop.transform.localPosition = new Vector3(x, 0f, half);
                prop.transform.localRotation = Quaternion.Euler(wallPropRotationOffset);
                prop.transform.localScale = Vector3.one * wallPropScale;
                foreach (var col in prop.GetComponentsInChildren<Collider>())
                    Destroy(col);
            }
        }

        private void BuildCornerPillars()
        {
            var half = roomSize / 2f - 0.8f;
            Vector3[] corners =
            {
                new(half, 0f, half), new(-half, 0f, half),
                new(half, 0f, -half), new(-half, 0f, -half)
            };
            foreach (var corner in corners)
                BuildPillar(corner);
        }

        private void BuildPillar(Vector3 localPos)
        {
            GameObject pillar;
            if (useRealRoomAssets && realPillarPrefab != null)
            {
                pillar = Instantiate(realPillarPrefab, transform);
                pillar.name = "Real Corner Pillar";
                pillar.transform.localPosition = localPos;
            }
            else
            {
                pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = "Corner Pillar";
                pillar.transform.SetParent(transform, false);
                pillar.transform.localPosition = localPos + new Vector3(0f, cornerPillarHeight / 2f, 0f);
                pillar.transform.localScale = new Vector3(0.4f, cornerPillarHeight / 2f, 0.4f);
                var mat = pillar.GetComponent<Renderer>().material;
                mat.color = wallColor * 1.3f;
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", accentColor * 0.3f);
            }

            if (cornerAuraVfxPrefab != null)
            {
                var aura = Instantiate(cornerAuraVfxPrefab, pillar.transform);
                aura.transform.localPosition = new Vector3(0f, 1f, 0f);
                aura.transform.localScale = Vector3.one * ambientVfxScale;
            }
        }

        private void BuildFloorCenterpiece()
        {
            if (floorCenterpieceVfxPrefab == null) return;
            var vfx = Instantiate(floorCenterpieceVfxPrefab, transform);
            vfx.transform.localPosition = floorCenterpiecePosition;
            vfx.transform.localScale = Vector3.one * ambientVfxScale;
        }

        // Called by each minigame script at its own "Complete!" moment - a bigger,
        // one-shot payoff distinct from AnswerTarget's per-answer flash, using
        // roundCompleteVfxPrefab (already assigned per scene, previously unused).
        public static void PlayRoundCompleteVfx(Vector3 worldPosition)
        {
            var env = FindFirstObjectByType<MinigameEnvironment>();
            if (env == null || env.roundCompleteVfxPrefab == null) return;
            var vfx = Instantiate(env.roundCompleteVfxPrefab, worldPosition, Quaternion.identity);
            vfx.transform.localScale = Vector3.one * env.feedbackVfxScale;
            Destroy(vfx, 6f);
        }

        private void BuildWalls()
        {
            if (useRealRoomAssets && realWallPrefab != null)
            {
                var half = roomSize / 2f;
                BuildRealWallRun(new Vector3(0f, 0f, half), 0f, roomSize);
                BuildRealWallRun(new Vector3(0f, 0f, -half), 180f, roomSize);
                BuildRealWallRun(new Vector3(half, 0f, 0f), -90f, roomSize);
                BuildRealWallRun(new Vector3(-half, 0f, 0f), 90f, roomSize);
                return;
            }

            var h = roomSize / 2f;
            BuildWall(new Vector3(0f, wallHeight / 2f, h), new Vector3(roomSize, wallHeight, 0.2f));
            BuildWall(new Vector3(0f, wallHeight / 2f, -h), new Vector3(roomSize, wallHeight, 0.2f));
            BuildWall(new Vector3(h, wallHeight / 2f, 0f), new Vector3(0.2f, wallHeight, roomSize));
            BuildWall(new Vector3(-h, wallHeight / 2f, 0f), new Vector3(0.2f, wallHeight, roomSize));
        }

        private void BuildWall(Vector3 localPos, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Room Wall";
            wall.transform.SetParent(transform, false);
            wall.transform.localPosition = localPos;
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().material.color = wallColor;
        }

        // Tiles realWallPrefab along one side of the room, centered on
        // localCenter, facing inward (yaw matches the side: front/back/left/
        // right), so a real lab wall panel replaces a primitive cube slab.
        private void BuildRealWallRun(Vector3 localCenter, float yaw, float runLength)
        {
            var tileCount = Mathf.Max(1, Mathf.CeilToInt(runLength / realWallTileWidth));
            var actualSpacing = runLength / tileCount;
            var rot = Quaternion.Euler(0f, yaw, 0f);

            for (var i = 0; i < tileCount; i++)
            {
                var offset = (i - (tileCount - 1) / 2f) * actualSpacing;
                var localPos = localCenter + rot * new Vector3(offset, 0f, 0f);
                var wall = Instantiate(realWallPrefab, transform);
                wall.name = "Real Wall Tile";
                wall.transform.localPosition = localPos;
                wall.transform.localRotation = rot;
            }
        }

        private void BuildAccentCeiling()
        {
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = "Accent Ceiling Strip";
            strip.transform.SetParent(transform, false);
            strip.transform.localPosition = new Vector3(0f, wallHeight - 0.05f, 0f);
            strip.transform.localScale = new Vector3(roomSize, 0.1f, roomSize);
            var mat = strip.GetComponent<Renderer>().material;
            mat.color = accentColor;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", accentColor * 0.4f);
            Destroy(strip.GetComponent<Collider>());
        }
    }
}
