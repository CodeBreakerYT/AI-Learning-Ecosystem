using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR;

public class ScannedData : MonoBehaviour
{
    [Header("Dependencies")]
    public GameObject loadedModel;

    [Header("Block-Out Settings")]
    public Transform blockSpawnPoint;
    public float heightOffset = 1f;
    public Vector3 rotationOffset = Vector3.zero;
    [Tooltip("Size of each grid voxel")]
    public float cubeSize = 0.1f;
    public Material blockMaterial;

    [Header("Monitor UI")]
    public GameObject monitorCanvas;
    public Animator monitorAnimator;
    public TextMeshProUGUI monitorText;
    public AudioSource resultSound;

    private bool isLoaded = false;
    private Coroutine calculationCoroutine;
    private GameObject blockContainer;

    private InputDevice rightHand;
    private bool lastBtnState = false;
    private bool waitForNext = false;

    private List<GameObject> spawnedBlocks = new List<GameObject>();

    private bool[,,] voxelGrid;
    private int resX, resY, resZ;

    void Start()
    {
        if (monitorCanvas != null) monitorCanvas.SetActive(false);
        TryInitDevice();
    }

    void Update()
    {
        if (!rightHand.isValid) TryInitDevice();

        if (blockContainer != null)
        {
            blockContainer.transform.rotation = Quaternion.Euler(rotationOffset);
        }

        // 1. Check if model is loaded and activate monitor
        if (!isLoaded && loadedModel != null)
        {
            isLoaded = true;
            if (monitorCanvas != null)
            {
                monitorCanvas.SetActive(true);
                if (monitorAnimator != null) monitorAnimator.SetTrigger("Open");
            }
            
            // Add colliders to loaded model for Raycasting
            AddMeshColliders(loadedModel);
        }

        // 2. Wait for B button to advance text
        if (isLoaded)
        {
            rightHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bool pressed);
            if (pressed && !lastBtnState)
            {
                if (waitForNext)
                    waitForNext = false;
            }
            lastBtnState = pressed;
        }
    }

    // Called by your custom UI Button
    public void StartCalculations()
    {
        if (!isLoaded && loadedModel != null)
            isLoaded = true;

        if (isLoaded)
        {
            if (calculationCoroutine != null) StopCoroutine(calculationCoroutine);
            calculationCoroutine = StartCoroutine(CalculationSequence());
        }
    }

    void TryInitDevice()
    {
        rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    void AddMeshColliders(GameObject obj)
    {
        foreach (var filter in obj.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.gameObject.GetComponent<Collider>() == null)
            {
                MeshCollider mc = filter.gameObject.AddComponent<MeshCollider>();
            }
            
            // Assigning a specific temporary layer so we can raycast exclusively against it
            filter.gameObject.layer = 30; // Temporary scan layer
        }
    }

    Bounds GetBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(obj.transform.position, Vector3.zero);

        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers)
        {
            b.Encapsulate(r.bounds);
        }
        return b;
    }

    IEnumerator CalculationSequence()
    {
        if (blockContainer != null) Destroy(blockContainer);
        spawnedBlocks.Clear();

        blockContainer = new GameObject("BlockContainer");
        blockContainer.transform.position = (blockSpawnPoint != null ? blockSpawnPoint.position : Vector3.zero) + Vector3.up * heightOffset;
        blockContainer.transform.rotation = Quaternion.Euler(rotationOffset);

        monitorText.text = "";
        yield return TypeText("--- INITIALIZING SCAN ---\n");
        yield return new WaitForSeconds(0.5f);

        Bounds b = GetBounds(loadedModel);
        float w = b.size.x;
        float h = b.size.y;
        float l = b.size.z;

        yield return TypeText($"Model Extents:\n");
        yield return TypeText($"Width:  {w:F2}m\n");
        yield return TypeText($"Height: {h:F2}m\n");
        yield return TypeText($"Length: {l:F2}m\n\n");
        
        yield return WaitNext();

        monitorText.text = "";
        yield return TypeText("--- BLOCK-OUT PROCESS ---\n");
        
        // Safety lock for cubeSize
        if (cubeSize < 0.02f) cubeSize = 0.02f;
        
        yield return TypeText($"Cube Resolution Size: {cubeSize:F2}m\n");
        yield return TypeText($"Spawning blocks non-overlapping...\n");

        // VOXELIZATION
        VoxelizeMesh(b);

        int totalBlocks = spawnedBlocks.Count;
        if (totalBlocks == 0) totalBlocks = 1;

        yield return TypeText($"\nTotal Blocks Spawned: {totalBlocks}\n\n");
        
        yield return WaitNext();

        monitorText.text = "";
        yield return TypeText("--- CALCULATIONS ---\n");
        
        float totalApproxVolume = 0f;
        foreach (GameObject block in spawnedBlocks)
        {
            Vector3 size = block.transform.localScale;
            totalApproxVolume += size.x * size.y * size.z;
        }

        yield return TypeText($"Sum(volume of all cubes) = v1 + v2 + ... + vn\n");
        yield return TypeText($"= {totalApproxVolume:F2} m³\n\n");
        
        yield return WaitNext();

        monitorText.text = "";
        yield return TypeText("--- SURFACE AREA ---\n");

        float approxSurfaceArea = 0f;
        foreach (GameObject block in spawnedBlocks)
        {
            Vector3 size = block.transform.localScale;
            approxSurfaceArea += 2f * ((size.x * size.y) + (size.y * size.z) + (size.z * size.x));
        }

        yield return TypeText($"Sum(surface area of all cubes) = a1 + a2 + ... + an\n");
        yield return TypeText($"= {approxSurfaceArea:F2} m²\n\n");
        
        yield return WaitNext();

        monitorText.text = "";
        yield return TypeText("--- RESULT ---\n");
        yield return TypeText($"Final Volume: {totalApproxVolume:F2} m³\n");
        yield return TypeText($"Final Surface Area: {approxSurfaceArea:F2} m²\n\n");

        if (resultSound != null) resultSound.Play();

        // Restore original layers
        foreach (var filter in loadedModel.GetComponentsInChildren<MeshFilter>())
        {
            filter.gameObject.layer = 0; // Default
        }
    }

    void VoxelizeMesh(Bounds b)
    {
        resX = Mathf.CeilToInt(b.size.x / cubeSize);
        resY = Mathf.CeilToInt(b.size.y / cubeSize);
        resZ = Mathf.CeilToInt(b.size.z / cubeSize);

        if (resX <= 0) resX = 1;
        if (resY <= 0) resY = 1;
        if (resZ <= 0) resZ = 1;

        // Arbitrary cap to prevent crash with massive bounds / tiny cubes
        if (resX > 100) resX = 100;
        if (resY > 100) resY = 100;
        if (resZ > 100) resZ = 100;

        voxelGrid = new bool[resX, resY, resZ];

        Vector3 startPos = b.min;

        int layerMask = 1 << 30; // Scan layer

        for (int x = 0; x < resX; x++)
        {
            for (int z = 0; z < resZ; z++)
            {
                float worldX = startPos.x + (x * cubeSize) + (cubeSize / 2f);
                float worldZ = startPos.z + (z * cubeSize) + (cubeSize / 2f);

                Vector3 rayStartTop = new Vector3(worldX, b.max.y + 1f, worldZ);
                Vector3 rayStartBot = new Vector3(worldX, b.min.y - 1f, worldZ);

                if (Physics.Raycast(rayStartTop, Vector3.down, out RaycastHit topHit, 100f, layerMask) &&
                    Physics.Raycast(rayStartBot, Vector3.up, out RaycastHit botHit, 100f, layerMask))
                {
                    float topY = topHit.point.y;
                    float botY = botHit.point.y;

                    for (int y = 0; y < resY; y++)
                    {
                        float worldY = startPos.y + (y * cubeSize) + (cubeSize / 2f);

                        if (worldY >= botY && worldY <= topY)
                        {
                            voxelGrid[x, y, z] = true;
                            SpawnBlock(x, y, z, worldX, worldY, worldZ, b.center);
                        }
                    }
                }
            }
        }
    }

    void SpawnBlock(int x, int y, int z, float wx, float wy, float wz, Vector3 originalCenter)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        
        Destroy(cube.GetComponent<Collider>());
        
        Vector3 localOffset = new Vector3(wx, wy, wz) - originalCenter;

        cube.transform.SetParent(blockContainer.transform);
        cube.transform.localPosition = localOffset;
        cube.transform.localRotation = Quaternion.identity;

        cube.transform.localScale = Vector3.one * cubeSize;

        if (blockMaterial != null) cube.GetComponent<Renderer>().material = blockMaterial;

        spawnedBlocks.Add(cube);
    }


    // Call this if the user resizes via UI runtime
    public void UpdateCubeSize(float size)
    {
        cubeSize = size;
    }

    IEnumerator TypeText(string text)
    {
        foreach (char c in text)
        {
            monitorText.text += c;
            yield return new WaitForSeconds(0.015f);
        }
    }

    IEnumerator WaitNext()
    {
        waitForNext = true;
        yield return new WaitUntil(() => waitForNext == false);
    }
}
