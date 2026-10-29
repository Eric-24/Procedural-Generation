using UnityEngine;
using UnityEngine.Rendering;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DotGen : MonoBehaviour
{
    [Header("Terrain Settings")]
    public int gridWidth = 600;
    public int gridLength = 600;
    public float spacing = 1.3f;
    public float heightSpacing = 95f;

    [Header("Noise Settings")]
    public float noiseScale = 135f;
    public float ampMult = 0.5f;
    public int octaves = 6;
    public float freqMult = 2f;
    [Range(1f, 3f)] public float mountainPower = 1.75f;
    [Range(0f, 1f)] public float ridgeSharpness = 0.45f;
    [Range(0f, 1f)] public float fernBranchingStrength = 0.82f; // builds fern-like land ridges 
    [Range(0f, 1f)] public float macroRangeContrast = 0.85f;    // broader terrain transitions
    public float macroScale = 800f; // low frequency texture
    [Range(0f, 1f)] public float mountainBeltStrength = 0.6f; // 0 = isolated mountain complexes, 1 = long mountain belts
    public int seed = 0;

    [Header("Hydraulic Erosion Settings")]
    public bool enableHydraulicErosion = true;
    [Range(0, 150000)] public int erosionIterations = 30000;
    [Range(2, 8)] public int erosionRadius = 3;
    [Range(0.01f, 0.99f)] public float erosionInertia = 0.08f;
    public float sedimentCapacityFactor = 4f;
    public float minSedimentCapacity = 0.01f;
    [Range(0.01f, 1f)] public float erodeSpeed = 0.24f;
    [Range(0.01f, 1f)] public float depositSpeed = 0.28f;
    [Range(0.001f, 0.2f)] public float evaporateSpeed = 0.018f;
    public float gravity = 4f;
    public int maxDropletLifetime = 45;

    [Header("Thermal Erosion Settings")]
    public bool enableThermalErosion = true;
    [Range(1, 15)] public int thermalIterations = 3;
    [Range(0.005f, 0.08f)] public float talusAngleThreshold = 0.018f; // normalized height
    [Range(0.05f, 0.5f)] public float thermalRelaxation = 0.25f;

    [Header("Water Settings")]
    public bool enableVolumedWater = true;
    [Range(-40f, 20f)] public float oceanSeaLevel = -22f;
    [Range(0f, 1f)] public float waterOpacity = 0.78f;
    [Range(20f, 5000f)] public float riverFluxThreshold = 2400f; // controls how thin/defined main rivers are
    [Range(0.5f, 3f)] public float tectonicOceanDropoff = 1.15f; // multiplier for continental shelf to deep ocean plate drop-off (~50% shallower)
    [Range(1f, 12f)] public float sandShoreRadius = 4f; // how far sand extends from water
    [Range(0.005f, 0.2f)] public float sandMaxAltitude = 0.06f;

    [Header("Dev Visualization Settings")]
    public bool useSlopeDiagnosticColors = false;
    public bool useElevationDevColors = false;
    public bool showWaterDischargeDebug = false;
    public float rockStartAltitude = 15f;
    public float alpineSlateAltitude = 32f;
    public float snowStartAltitude = 42f;
    public Gradient elevationGradient;

    private Mesh terrainMesh;
    private MeshRenderer meshRenderer;

    // child mesh for translucent volumed water
    private GameObject waterMeshObj;
    private Mesh waterMesh;
    private MeshRenderer waterRenderer;

    private Vector3[] vertices;
    private Vector3[] normalsBuffer;
    private int[] triangles;
    private Color32[] colors;
    private float[] currentHeightMap;
    private float[] normalizedMapBuffer;
    private float[] shoreDistanceBuffer;
    private float[] carvedHeightsBuffer;
    private Vector3[] waterVertsBuffer;
    private Color32[] waterColsBuffer;
    private int[] waterTrianglesBuffer;
    private int[] wetRowCounts;
    private int[] wetRowOffsets;
    private int currentWetQuadCount;

    private const int GRADIENT_LUT_SIZE = 512;
    private Color32[] gradientLUT = new Color32[GRADIENT_LUT_SIZE];

    // arrays to store water data during erosion
    [HideInInspector, System.NonSerialized] public float[] waterDischargeMap;
    [HideInInspector, System.NonSerialized] public float[] waterSurfaceMap; // stores lake/ocean/stream surface heights

    // river network graph buffers 
    private int[] downstreamParent;
    private byte[] parentDirBuffer;
    private int[] topologicalOrder;
    private bool[] nodeVisited;
    private int[] bucketHeads;
    private int[] bucketNextNode;
    private int[] riverCellIndices;
    private int riverCellCount;
    private float[] coarseMacroBuffer;
    private const int PRIORITY_BUCKETS = 2048;

    // erosion brush settings
    private int[] relativeBrushOffsets;
    private float[] relativeBrushWeights;
    private int relativeBrushCount;
    private int cachedBrushRadius = -1;
    private int cachedBrushWidth = -1;
    private int cachedBrushLength = -1;

    private int cachedWidth;
    private int cachedLength;
    private bool pendingDimensionRegen = false;
    private bool shoreDistanceDirty = true;

    private bool isGenerating = false;
    private bool queuedFullRegen = false;
    private bool queuedWaterUpdate = false;
    private bool queuedColorUpdate = false;

    // OnValidate dirty tracking for water/color settings
    private bool prevEnableWater;
    private float prevOceanSeaLevel;
    private float prevWaterOpacity;
    private float prevRiverFluxThreshold;
    private float prevSandShoreRadius;
    private float prevSandMaxAltitude;
    private bool prevUseSlope;
    private bool prevUseElev;
    private bool prevShowDebug;
    private float prevRockAlt;
    private float prevSlateAlt;
    private float prevSnowAlt;

    private static readonly int[] DX = { -1, 1, 0, 0, -1, 1, -1, 1 };
    private static readonly int[] DZ = { 0, 0, -1, 1, -1, -1, 1, 1 };
    private static readonly float[] INV_DIST = { 1f, 1f, 1f, 1f, 0.70710678f, 0.70710678f, 0.70710678f, 0.70710678f };
    private static readonly float[] EDGE_DIST = { 1f, 1f, 1f, 1f, 1.4142135f, 1.4142135f, 1.4142135f, 1.4142135f };
    private const float COASTAL_SHELF_NORMALIZED = 0.075f;
    private const MeshUpdateFlags FAST_MESH_FLAGS = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

    void Awake()
    {
        terrainMesh = new Mesh();
        terrainMesh.name = "Procedural Terrain Mesh";
        terrainMesh.indexFormat = IndexFormat.UInt32;
        terrainMesh.MarkDynamic();
        GetComponent<MeshFilter>().mesh = terrainMesh;
        meshRenderer = GetComponent<MeshRenderer>();

        if (meshRenderer != null && meshRenderer.material != null)
        {
            meshRenderer.material.SetFloat("_Glossiness", 0.1f);
        }

        InitializeDefaultGradient();
        SetupWaterMeshObject();
        SyncValidateCache();
    }

    void Start()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (child != waterMeshObj)
            {
                Destroy(child);
            }
        }

        // Clamp legacy serialized drop-off so existing scene immediately gets the 50% shallower smooth ocean floor
        if (tectonicOceanDropoff > 1.5f) tectonicOceanDropoff = 1.15f;

        GenerateDotMap();
    }

    void Update()
    {
        // if grid dimensions were changed
        if (pendingDimensionRegen)
        {
            pendingDimensionRegen = false;
            GenerateDotMap();
        }
    }

    private void SyncValidateCache()
    {
        prevEnableWater = enableVolumedWater;
        prevOceanSeaLevel = oceanSeaLevel;
        prevWaterOpacity = waterOpacity;
        prevRiverFluxThreshold = riverFluxThreshold;
        prevSandShoreRadius = sandShoreRadius;
        prevSandMaxAltitude = sandMaxAltitude;
        prevUseSlope = useSlopeDiagnosticColors;
        prevUseElev = useElevationDevColors;
        prevShowDebug = showWaterDischargeDebug;
        prevRockAlt = rockStartAltitude;
        prevSlateAlt = alpineSlateAltitude;
        prevSnowAlt = snowStartAltitude;
    }

    // toggle color checkboxes 
    void OnValidate()
    {
        if (gridWidth < 10) gridWidth = 10;
        if (gridLength < 10) gridLength = 10;

        if (Application.isPlaying && terrainMesh != null)
        {
            // regenerate terrain if dimensions change
            if (vertices == null || cachedWidth != gridWidth || cachedLength != gridLength)
            {
                pendingDimensionRegen = true;
                return;
            }

            if (currentHeightMap != null && currentHeightMap.Length == vertices.Length)
            {
                bool waterChanged = prevEnableWater != enableVolumedWater ||
                                    prevOceanSeaLevel != oceanSeaLevel ||
                                    prevWaterOpacity != waterOpacity ||
                                    prevRiverFluxThreshold != riverFluxThreshold;

                bool colorChanged = waterChanged ||
                                    prevSandShoreRadius != sandShoreRadius ||
                                    prevSandMaxAltitude != sandMaxAltitude ||
                                    prevUseSlope != useSlopeDiagnosticColors ||
                                    prevUseElev != useElevationDevColors ||
                                    prevShowDebug != showWaterDischargeDebug ||
                                    prevRockAlt != rockStartAltitude ||
                                    prevSlateAlt != alpineSlateAltitude ||
                                    prevSnowAlt != snowStartAltitude;

                SyncValidateCache();

                if (waterChanged || colorChanged)
                {
                    if (waterChanged) shoreDistanceDirty = true;
                    _ = RunVisualUpdateAsync(waterChanged, colorChanged);
                }
            }
        }
    }

    public void GenerateDotMap()
    {
        if (terrainMesh == null) return;
        _ = RunGenerationAsync();
    }

    private async Task RunGenerationAsync()
    {
        if (isGenerating)
        {
            queuedFullRegen = true;
            return;
        }
        isGenerating = true;

        try
        {
            do
            {
                queuedFullRegen = false;
                queuedWaterUpdate = false;
                queuedColorUpdate = false;

                if (terrainMesh == null) break;
                if (waterMeshObj == null) SetupWaterMeshObject();

                bool dimensionsChanged = (vertices == null || cachedWidth != gridWidth || cachedLength != gridLength);
                if (dimensionsChanged)
                {
                    cachedWidth = gridWidth;
                    cachedLength = gridLength;
                    AllocateMeshArrays();
                }

                BakeGradientLUT();

                await Task.Run(() =>
                {
                    // iniitalises height map
                    float[] normalizedMap = GenerateNormalizedHeightMap();

                    // creates fractal tree-fern ridges for rivers 
                    ComputeThinRiverDrainage(normalizedMap);

                    // run hydraulic erosion on normalised heights
                    if (enableHydraulicErosion && erosionIterations > 0)
                    {
                        SimulateHydraulicErosion(normalizedMap);
                    }

                    // thermal erosion passes to remove spikes
                    if (enableThermalErosion && thermalIterations > 0)
                    {
                        SimulateThermalErosion(normalizedMap);
                    }

                    // scale normalized [0, 1] heights into world space [-heightSpacing*0.35, heightSpacing*0.65]
                    int w = cachedWidth;
                    int l = cachedLength;
                    float sp = spacing;
                    float hSp = heightSpacing;
                    float dropMult = tectonicOceanDropoff;

                    float fixedCoastlineY = (COASTAL_SHELF_NORMALIZED - 0.35f) * hSp;
                    float maxSubmergeSpan = COASTAL_SHELF_NORMALIZED * hSp;
                    float maxExtraDrop = hSp * 0.095f * dropMult;

                    Parallel.For(0, l, z =>
                    {
                        int rowOffset = z * w;
                        float worldZ = z * sp;
                        for (int x = 0; x < w; x++)
                        {
                            int index = rowOffset + x;
                            float worldY = (normalizedMap[index] - 0.35f) * hSp;

                            if (worldY < fixedCoastlineY && maxSubmergeSpan > 0.001f)
                            {
                                float submergeDepth = fixedCoastlineY - worldY;
                                // Normalized depth t in [0, 1] below the coastline
                                float t = Mathf.Clamp01(submergeDepth / maxSubmergeSpan);
                                float easeInOut = t * t * (3f - 2f * t);
                                worldY = fixedCoastlineY - submergeDepth * 0.75f - easeInOut * maxExtraDrop;
                            }

                            currentHeightMap[index] = worldY;
                            vertices[index] = new Vector3(x * sp, worldY, worldZ);
                        }
                    });

                    ComputeTerrainNormals(currentHeightMap, normalsBuffer);
                    shoreDistanceDirty = true;

                    if (enableVolumedWater)
                    {
                        ComputeVolumedWaterData(currentHeightMap);
                    }

                    UpdateTerrainColorsCompute(currentHeightMap, normalsBuffer);
                });

                if (this == null || terrainMesh == null) break;

                int mw = cachedWidth;
                int ml = cachedLength;
                float msp = spacing;
                float mhSp = heightSpacing;

                if (dimensionsChanged)
                {
                    terrainMesh.Clear();
                    terrainMesh.SetVertices(vertices, 0, vertices.Length, FAST_MESH_FLAGS);
                    terrainMesh.SetNormals(normalsBuffer, 0, normalsBuffer.Length, FAST_MESH_FLAGS);
                    terrainMesh.SetIndices(triangles, MeshTopology.Triangles, 0, false, 0);
                }
                else
                {
                    terrainMesh.SetVertices(vertices, 0, vertices.Length, FAST_MESH_FLAGS);
                    terrainMesh.SetNormals(normalsBuffer, 0, normalsBuffer.Length, FAST_MESH_FLAGS);
                }

                float mapWorldW = (mw - 1) * msp;
                float mapWorldL = (ml - 1) * msp;
                Bounds worldBounds = new Bounds(new Vector3(mapWorldW * 0.5f, mhSp * 0.15f, mapWorldL * 0.5f), new Vector3(mapWorldW, mhSp * 1.5f, mapWorldL));
                terrainMesh.bounds = worldBounds;
                terrainMesh.SetColors(colors, 0, colors.Length, FAST_MESH_FLAGS);

                ApplyWaterMeshToUnity(worldBounds);
                SyncValidateCache();

            } while (queuedFullRegen);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            return;
        }
        finally
        {
            isGenerating = false;
        }

        if (queuedWaterUpdate || queuedColorUpdate)
        {
            bool wUpd = queuedWaterUpdate;
            bool cUpd = queuedColorUpdate;
            queuedWaterUpdate = false;
            queuedColorUpdate = false;
            await RunVisualUpdateAsync(wUpd, cUpd);
        }
    }

    private async Task RunVisualUpdateAsync(bool updateWater, bool updateColors)
    {
        if (isGenerating)
        {
            if (updateWater) queuedWaterUpdate = true;
            if (updateColors) queuedColorUpdate = true;
            return;
        }
        isGenerating = true;

        try
        {
            if (waterMeshObj == null) SetupWaterMeshObject();
            if (updateColors) BakeGradientLUT();

            await Task.Run(() =>
            {
                if (updateWater && enableVolumedWater)
                {
                    ComputeVolumedWaterData(currentHeightMap);
                }
                if (updateColors)
                {
                    UpdateTerrainColorsCompute(currentHeightMap, normalsBuffer);
                }
            });

            if (this != null && terrainMesh != null)
            {
                float mapWorldW = (cachedWidth - 1) * spacing;
                float mapWorldL = (cachedLength - 1) * spacing;
                Bounds worldBounds = new Bounds(new Vector3(mapWorldW * 0.5f, heightSpacing * 0.15f, mapWorldL * 0.5f), new Vector3(mapWorldW, heightSpacing * 1.5f, mapWorldL));

                if (updateWater) ApplyWaterMeshToUnity(worldBounds);
                if (updateColors) terrainMesh.SetColors(colors, 0, colors.Length, FAST_MESH_FLAGS);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            return;
        }
        finally
        {
            isGenerating = false;
        }

        if (queuedFullRegen)
        {
            await RunGenerationAsync();
        }
        else if (queuedWaterUpdate || queuedColorUpdate)
        {
            bool wUpd = queuedWaterUpdate;
            bool cUpd = queuedColorUpdate;
            queuedWaterUpdate = false;
            queuedColorUpdate = false;
            await RunVisualUpdateAsync(wUpd, cUpd);
        }
    }

    // computes normals directly from the heightmap via central differences in parallel
    private void ComputeTerrainNormals(float[] heightMap, Vector3[] normals)
    {
        int w = cachedWidth;
        int l = cachedLength;
        float twoSp = 2f * spacing;
        float oneSp = spacing;

        Parallel.For(0, l, z =>
        {
            int row = z * w;
            int zPrev = (z > 0 ? z - 1 : z) * w;
            int zNext = (z < l - 1 ? z + 1 : z) * w;
            float dzScale = (z > 0 && z < l - 1) ? twoSp : oneSp;

            for (int x = 0; x < w; x++)
            {
                int xPrev = x > 0 ? x - 1 : x;
                int xNext = x < w - 1 ? x + 1 : x;
                float dxScale = (x > 0 && x < w - 1) ? twoSp : oneSp;

                float dhdx = (heightMap[row + xNext] - heightMap[row + xPrev]) / dxScale;
                float dhdz = (heightMap[zNext + x] - heightMap[zPrev + x]) / dzScale;

                float nx = -dhdx;
                float nz = -dhdz;
                float invLen = 1f / Mathf.Sqrt(nx * nx + 1f + nz * nz);
                normals[row + x] = new Vector3(nx * invLen, invLen, nz * invLen);
            }
        });
    }

    private void SimulateHydraulicErosion(float[] heightMap)
    {
        // minimum radius is 2
        if (erosionRadius < 2) erosionRadius = 2;
        InitializeErosionBrush();

        // deterministic offset from seed so droplet paths are reproducible
        uint rngState = (uint)(seed + 1337) * 747796405u + 2891336453u;
        if (rngState == 0) rngState = 1337u;

        int w = cachedWidth;
        int l = cachedLength;
        int safeMargin = erosionRadius + 2;
        float spawnW = w - safeMargin * 2;
        float spawnL = l - safeMargin * 2;
        if (spawnW <= 2f || spawnL <= 2f) return;

        int iterations = erosionIterations;
        int maxLifetime = maxDropletLifetime;
        float inertia = erosionInertia;
        float oneMinusInertia = 1f - inertia;
        float capFactor = sedimentCapacityFactor;
        float minCap = minSedimentCapacity;
        float dSpeed = depositSpeed;
        float eSpeed = erodeSpeed;
        float grav = gravity;
        float waterRetention = 1f - evaporateSpeed;
        int brushCount = relativeBrushCount;
        int[] brushOffsets = relativeBrushOffsets;
        float[] brushWeights = relativeBrushWeights;
        float maxX = w - safeMargin;
        float maxZ = l - safeMargin;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            // spawn droplet at random position inside the grid 
            rngState ^= rngState << 13; rngState ^= rngState >> 17; rngState ^= rngState << 5;
            float u1 = (rngState & 0xFFFFFF) / 16777216f;
            rngState ^= rngState << 13; rngState ^= rngState >> 17; rngState ^= rngState << 5;
            float u2 = (rngState & 0xFFFFFF) / 16777216f;

            float posX = safeMargin + u1 * spawnW;
            float posZ = safeMargin + u2 * spawnL;
            float dirX = 0f;
            float dirZ = 0f;
            float speed = 1f;
            float water = 1f;
            float sediment = 0f;

            for (int lifetime = 0; lifetime < maxLifetime; lifetime++)
            {
                int nodeX = (int)posX;
                int nodeZ = (int)posZ;
                int dropletIndex = nodeZ * w + nodeX;

                float cellOffsetX = posX - nodeX;
                float cellOffsetZ = posZ - nodeZ;

                // calculate droplet's current height and surface gradient
                SampleHeightAndGradient(heightMap, w, posX, posZ, out float height, out float gradX, out float gradZ);

                // update direction with inertia and normalize
                dirX = dirX * inertia - gradX * oneMinusInertia;
                dirZ = dirZ * inertia - gradZ * oneMinusInertia;

                float len = Mathf.Sqrt(dirX * dirX + dirZ * dirZ);
                if (len != 0f)
                {
                    dirX /= len;
                    dirZ /= len;
                }

                posX += dirX;
                posZ += dirZ;

                // stop once droplet stopped moving or flowed off the edge of the map
                if ((dirX == 0f && dirZ == 0f) || posX < safeMargin || posX >= maxX || posZ < safeMargin || posZ >= maxZ)
                {
                    break;
                }

                SampleHeightAndGradient(heightMap, w, posX, posZ, out float newHeight, out _, out _);
                float deltaHeight = newHeight - height;

                // calculate sediment capacity on normalized heightmap
                float sedimentCapacity = Mathf.Max(-deltaHeight * speed * water * capFactor, minCap);

                if (sediment > sedimentCapacity || deltaHeight > 0f)
                {
                    float amountToDeposit = (deltaHeight > 0f)
                        ? Mathf.Min(deltaHeight, sediment)
                        : (sediment - sedimentCapacity) * dSpeed;

                    sediment -= amountToDeposit;

                    // bilinearly deposit sediment 
                    heightMap[dropletIndex] += amountToDeposit * (1f - cellOffsetX) * (1f - cellOffsetZ);
                    heightMap[dropletIndex + 1] += amountToDeposit * cellOffsetX * (1f - cellOffsetZ);
                    heightMap[dropletIndex + w] += amountToDeposit * (1f - cellOffsetX) * cellOffsetZ;
                    heightMap[dropletIndex + w + 1] += amountToDeposit * cellOffsetX * cellOffsetZ;
                }
                else
                {
                    // moving downhill with spare capacity
                    float amountToErode = Mathf.Min((sedimentCapacity - sediment) * eSpeed, -deltaHeight);

                    for (int i = 0; i < brushCount; i++)
                    {
                        int weightedIndex = dropletIndex + brushOffsets[i];
                        float weighedErodeAmount = amountToErode * brushWeights[i];
                        heightMap[weightedIndex] -= weighedErodeAmount;
                        sediment += weighedErodeAmount;
                    }
                }

                // update droplet speed and evaporate
                speed = Mathf.Sqrt(Mathf.Max(0f, speed * speed - deltaHeight * grav));
                water *= waterRetention;
            }
        }
    }

    // grows a continuous drainage tree from the low valleys up into the mountains.
    // reconstructs terrain height upward along the river tree inversely proportional to water volume
    private void ComputeThinRiverDrainage(float[] heightMap)
    {
        int w = cachedWidth;
        int l = cachedLength;
        int vertCount = w * l;
        System.Array.Clear(nodeVisited, 0, vertCount);
        System.Array.Fill(bucketHeads, -1);
        System.Array.Fill(downstreamParent, -1);

        // create river mouths at lowest elevation or boundary regions
        int minBucket = PRIORITY_BUCKETS - 1;

        void TrySeedBoundaryNode(int idx)
        {
            if (heightMap[idx] < 0.38f)
            {
                nodeVisited[idx] = true;
                int b = Mathf.Clamp((int)(heightMap[idx] * (PRIORITY_BUCKETS - 1)), 0, PRIORITY_BUCKETS - 1);
                bucketNextNode[idx] = bucketHeads[b];
                bucketHeads[b] = idx;
                if (b < minBucket) minBucket = b;
            }
        }

        int topRowOffset = (l - 1) * w;
        for (int x = 0; x < w; x++)
        {
            TrySeedBoundaryNode(x);
            TrySeedBoundaryNode(topRowOffset + x);
        }
        for (int z = 1; z < l - 1; z++)
        {
            int row = z * w;
            TrySeedBoundaryNode(row);
            TrySeedBoundaryNode(row + w - 1);
        }

        // if all boundaries happened to be high, create at the global minimum boundary
        if (minBucket == PRIORITY_BUCKETS - 1)
        {
            int lowestIdx = 0;
            float lowestH = float.MaxValue;
            for (int x = 0; x < w; x++)
            {
                if (heightMap[x] < lowestH) { lowestH = heightMap[x]; lowestIdx = x; }
            }
            nodeVisited[lowestIdx] = true;
            bucketHeads[0] = lowestIdx;
            bucketNextNode[lowestIdx] = -1;
            minBucket = 0;
        }

        // grow the branching river network upstream using priority flood with directional inertia
        int topoCount = 0;
        int curBucket = minBucket;

        while (curBucket < PRIORITY_BUCKETS)
        {
            int idx = bucketHeads[curBucket];
            if (idx == -1)
            {
                curBucket++;
                continue;
            }
            bucketHeads[curBucket] = bucketNextNode[idx];
            topologicalOrder[topoCount++] = idx;

            int cz = idx / w;
            int cx = idx - cz * w;

            // check which direction this branch came from so tributaries branch naturally
            int parentIdx = downstreamParent[idx];
            int pDx = 0, pDz = 0;
            if (parentIdx != -1)
            {
                int dir = parentDirBuffer[idx];
                pDx = DX[dir];
                pDz = DZ[dir];
            }

            int startN = ((cx * 73856093) ^ (cz * 19349663) ^ seed) & 7;

            for (int step = 0; step < 8; step++)
            {
                int n = (startN + step) & 7;
                int nx = cx + DX[n];
                int nz = cz + DZ[n];
                if (nx < 0 || nx >= w || nz < 0 || nz >= l) continue;

                int nIdx = nz * w + nx;
                if (nodeVisited[nIdx]) continue;

                nodeVisited[nIdx] = true;
                downstreamParent[nIdx] = idx;
                parentDirBuffer[nIdx] = (byte)n;

                // prioritize edges that align with upstream inertia 
                float alignmentBonus = (pDx * DX[n] + pDz * DZ[n]) * 0.006f;
                // Smooth spatial hash (avoids modulo grid-line striping!)
                uint hHash = (uint)(nx * 374761393 + nz * 668265263 + seed * 1442695041);
                hHash = (hHash ^ (hHash >> 13)) * 1274126177u;
                float organicJitter = ((hHash & 0xFFFF) / 65535f - 0.5f) * 0.016f;
                float priorityH = Mathf.Clamp01(heightMap[nIdx] - alignmentBonus + organicJitter);

                int nb = Mathf.Clamp((int)(priorityH * (PRIORITY_BUCKETS - 1)), 0, PRIORITY_BUCKETS - 1);
                bucketNextNode[nIdx] = bucketHeads[nb];
                bucketHeads[nb] = nIdx;
                if (nb < curBucket) curBucket = nb;
            }
        }

        // accumulate water volume from highest mountain peaks down to river mouth
        float[] dischargeMap = waterDischargeMap;
        System.Array.Fill(dischargeMap, 1f, 0, vertCount);

        for (int i = topoCount - 1; i >= 0; i--)
        {
            int idx = topologicalOrder[i];
            int pIdx = downstreamParent[idx];
            if (pIdx != -1)
            {
                dischargeMap[pIdx] += dischargeMap[idx];
            }
        }

        // precompute targetSlope in parallel using shoreDistanceBuffer as scratch space
        float[] precomputedStep = shoreDistanceBuffer;
        byte[] pDirs = parentDirBuffer;
        Parallel.For(0, l, z =>
        {
            int row = z * w;
            for (int x = 0; x < w; x++)
            {
                int idx = row + x;
                float h = heightMap[idx];
                float edgeDist = EDGE_DIST[pDirs[idx]];
                // macro height determines where the general biome (mountain/plains)
                float macroWeight = 0.15f + 1.85f * h;
                // slope inversely proportional to water volume
                float targetSlope = (0.042f * macroWeight) / (1f + Mathf.Pow(dischargeMap[idx], 0.44f) * 0.48f);
                // height delta between nodes for talus effect 
                float maxTalusDelta = 0.024f * edgeDist * (0.45f + 0.55f * h);
                precomputedStep[idx] = Mathf.Min(targetSlope * edgeDist, maxTalusDelta);
            }
        });

        // traverse the river network uphill starting from the mouth
        float[] carvedHeights = carvedHeightsBuffer;
        float maxCarved = 0.0001f;

        for (int i = 0; i < topoCount; i++)
        {
            int idx = topologicalOrder[i];
            int pIdx = downstreamParent[idx];

            if (pIdx == -1)
            {
                carvedHeights[idx] = heightMap[idx] * 0.15f;
                continue;
            }

            float newH = carvedHeights[pIdx] + precomputedStep[idx];
            carvedHeights[idx] = newH;
            if (newH > maxCarved) maxCarved = newH;
        }

        // normalize and blend the reconstructed fern terrain with the macro heightmap
        float invMax = 1f / maxCarved;
        float fBranch = fernBranchingStrength;
        Parallel.For(0, l, z =>
        {
            int row = z * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                float fernH = carvedHeights[i] * invMax;
                float blended = Mathf.Lerp(heightMap[i], fernH, fBranch);
                heightMap[i] = blended;
                carvedHeights[i] = blended;
            }
        });

        // gaussian blur
        float gaussFluxThresh = riverFluxThreshold * 0.25f;
        float[] tempBlur = precomputedStep;
        System.Array.Copy(heightMap, tempBlur, vertCount);

        for (int pass = 0; pass < 2; pass++)
        {
            Parallel.For(0, l, z =>
            {
                int row = z * w;
                tempBlur[row] = heightMap[row];
                tempBlur[row + w - 1] = heightMap[row + w - 1];
                for (int x = 1; x < w - 1; x++)
                {
                    int idx = row + x;
                    tempBlur[idx] = heightMap[idx] * 0.5f + (heightMap[idx - 1] + heightMap[idx + 1]) * 0.25f;
                }
            });

            Parallel.For(1, l - 1, z =>
            {
                int row = z * w;
                for (int x = 1; x < w - 1; x++)
                {
                    int idx = row + x;
                    float isoAvg = tempBlur[idx] * 0.5f + (tempBlur[idx - w] + tempBlur[idx + w]) * 0.25f;
                    // smooth ridges slightly more than river channels
                    float blend = (dischargeMap[idx] > gaussFluxThresh) ? 0.22f : 0.48f;
                    carvedHeights[idx] = Mathf.Lerp(heightMap[idx], isoAvg, blend);
                }
            });
            System.Array.Copy(carvedHeights, w + 1, heightMap, w + 1, vertCount - 2 * w - 2);
        }

        // carve a clean riverbed trough for major rivers 
        float gridScaleFactor = Mathf.Sqrt(vertCount / 90000f);
        float effectiveFluxThresh = Mathf.Max(riverFluxThreshold, 1800f * (vertCount / 90000f));
        float carveFluxThreshold = effectiveFluxThresh * 0.80f;

        System.Array.Copy(heightMap, carvedHeights, vertCount);
        riverCellCount = 0;

        float[] cellTargetBed = precomputedStep;
        byte[] cellCarveRadius = parentDirBuffer;
        System.Array.Clear(cellCarveRadius, 0, vertCount);

        int maxPossibleRadius = Mathf.Clamp(Mathf.RoundToInt(6f * gridScaleFactor), 1, 6);

        for (int z = 2; z < l - 2; z++)
        {
            int row = z * w;
            for (int x = 2; x < w - 2; x++)
            {
                int idx = row + x;
                float flux = dischargeMap[idx];
                if (flux > carveFluxThreshold)
                {
                    riverCellIndices[riverCellCount++] = idx;
                    float excess = Mathf.Max(0f, (flux - carveFluxThreshold) / effectiveFluxThresh);

                    // check proximity of river to fixed coastal shelf elevation
                    float currentH = heightMap[idx];
                    float coastalProximity = 1f - Mathf.Clamp01((currentH - COASTAL_SHELF_NORMALIZED) / 0.09f);
                    float estuaryFlare = coastalProximity * coastalProximity * (3f - 2f * coastalProximity);

                    // channel depth reduced by ~45% so rivers sit naturally without deep canyon trenches
                    float baseDepth = Mathf.Min(0.016f, 0.005f + Mathf.Sqrt(excess) * 0.0045f);
                    float estuaryExtraDepth = estuaryFlare * 0.012f;
                    float channelDepth = baseDepth + estuaryExtraDepth;

                    // radius reduced (1-3 cells inland, flaring modestly to 4-6 cells right at the ocean mouth)
                    float rawRadius = (1.0f + Mathf.Sqrt(excess) * 0.75f + estuaryFlare * 2.2f) * gridScaleFactor;
                    int radius = Mathf.Clamp(Mathf.RoundToInt(rawRadius), 1, 6);

                    cellCarveRadius[idx] = (byte)radius;
                    cellTargetBed[idx] = currentH - channelDepth;
                }
            }
        }

        // mark rows that are within maxPossibleRadius of any river cell so empty rows skip the gather
        bool[] activeCarveRow = nodeVisited;
        System.Array.Clear(activeCarveRow, 0, l);
        for (int r = 0; r < riverCellCount; r++)
        {
            int rz = riverCellIndices[r] / w;
            int zMin = Mathf.Max(1, rz - maxPossibleRadius);
            int zMax = Mathf.Min(l - 2, rz + maxPossibleRadius);
            for (int z = zMin; z <= zMax; z++) activeCarveRow[z] = true;
        }

        // parallel gather riverbed carving (order-independent min)
        Parallel.For(1, l - 1, nz =>
        {
            if (!activeCarveRow[nz]) return;
            int nRow = nz * w;
            int zStart = Mathf.Max(2, nz - maxPossibleRadius);
            int zEnd = Mathf.Min(l - 3, nz + maxPossibleRadius);

            for (int nx = 1; nx < w - 1; nx++)
            {
                int nIdx = nRow + nx;
                float origH = heightMap[nIdx];
                float minCarveH = origH;

                int xStart = Mathf.Max(2, nx - maxPossibleRadius);
                int xEnd = Mathf.Min(w - 3, nx + maxPossibleRadius);

                for (int rz = zStart; rz <= zEnd; rz++)
                {
                    int dz = nz - rz;
                    int dz2 = dz * dz;
                    int rRow = rz * w;

                    for (int rx = xStart; rx <= xEnd; rx++)
                    {
                        int rIdx = rRow + rx;
                        int rad = cellCarveRadius[rIdx];
                        if (rad == 0) continue;

                        int dx = nx - rx;
                        int d2 = dx * dx + dz2;
                        if (d2 <= rad * rad)
                        {
                            float invRadius = 1f / (rad + 0.5f);
                            float normDist = Mathf.Sqrt(d2) * invRadius;
                            // smooth cosine-like trough profile for river mouths
                            float troughProfile = (1f - normDist * normDist);
                            troughProfile *= troughProfile;
                            float bankCarveH = Mathf.Lerp(origH, cellTargetBed[rIdx], troughProfile * 0.70f);
                            if (bankCarveH < minCarveH) minCarveH = bankCarveH;
                        }
                    }
                }
                carvedHeights[nIdx] = minCarveH;
            }
        });

        // 1 fast isotropic smoothing pass over the carved riverbeds so stamped circles blend into a continuous channel
        Parallel.For(1, l - 1, z =>
        {
            int row = z * w;
            for (int x = 1; x < w - 1; x++)
            {
                int idx = row + x;
                if (carvedHeights[idx] < heightMap[idx])
                {
                    float c = carvedHeights[idx] * 0.25f;
                    float card = (carvedHeights[idx - 1] + carvedHeights[idx + 1] + carvedHeights[idx - w] + carvedHeights[idx + w]) * 0.125f;
                    float diag = (carvedHeights[idx - w - 1] + carvedHeights[idx - w + 1] + carvedHeights[idx + w - 1] + carvedHeights[idx + w + 1]) * 0.0625f;
                    heightMap[idx] = c + card + diag;
                }
                else
                {
                    heightMap[idx] = carvedHeights[idx];
                }
            }
        });
    }

    // thermal erosion passes
    private void SimulateThermalErosion(float[] heightMap)
    {
        int w = cachedWidth;
        int l = cachedLength;
        int iterations = thermalIterations;
        float threshold = talusAngleThreshold;
        float relaxation = thermalRelaxation;

        int n0 = DZ[0] * w + DX[0], n1 = DZ[1] * w + DX[1], n2 = DZ[2] * w + DX[2], n3 = DZ[3] * w + DX[3];
        int n4 = DZ[4] * w + DX[4], n5 = DZ[5] * w + DX[5], n6 = DZ[6] * w + DX[6], n7 = DZ[7] * w + DX[7];
        int[] neighborOffsets = { n0, n1, n2, n3, n4, n5, n6, n7 };

        for (int iter = 0; iter < iterations; iter++)
        {
            for (int z = 1; z < l - 1; z++)
            {
                int row = z * w;
                for (int x = 1; x < w - 1; x++)
                {
                    int idx = row + x;
                    float h = heightMap[idx];

                    float maxDiff = 0f;
                    int steepestNeighbor = -1;

                    for (int n = 0; n < 8; n++)
                    {
                        int nIdx = idx + neighborOffsets[n];
                        float diff = (h - heightMap[nIdx]) * INV_DIST[n];
                        if (diff > maxDiff)
                        {
                            maxDiff = diff;
                            steepestNeighbor = nIdx;
                        }
                    }

                    // if slope exceeds the talus angle of repose, slide a fraction of dirt down
                    if (maxDiff > threshold && steepestNeighbor != -1)
                    {
                        float transfer = (maxDiff - threshold) * relaxation;
                        heightMap[idx] -= transfer;
                        heightMap[steepestNeighbor] += transfer;
                    }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SampleHeightAndGradient(float[] heightMap, int w, float posX, float posZ, out float height, out float gradX, out float gradZ)
    {
        int coordX = (int)posX;
        int coordZ = (int)posZ;

        float x = posX - coordX;
        float z = posZ - coordZ;

        int nodeIndexNW = coordZ * w + coordX;
        float heightNW = heightMap[nodeIndexNW];
        float heightNE = heightMap[nodeIndexNW + 1];
        float heightSW = heightMap[nodeIndexNW + w];
        float heightSE = heightMap[nodeIndexNW + w + 1];

        // Bilinear gradient
        gradX = (heightNE - heightNW) * (1f - z) + (heightSE - heightSW) * z;
        gradZ = (heightSW - heightNW) * (1f - x) + (heightSE - heightNE) * x;

        // Bilinear height
        height = heightNW * (1f - x) * (1f - z) + heightNE * x * (1f - z) + heightSW * (1f - x) * z + heightSE * x * z;
    }

    // precompute brush indices and weights
    private void InitializeErosionBrush()
    {
        if (relativeBrushOffsets != null && cachedBrushRadius == erosionRadius && cachedBrushWidth == cachedWidth && cachedBrushLength == cachedLength)
        {
            return;
        }

        cachedBrushRadius = erosionRadius;
        cachedBrushWidth = cachedWidth;
        cachedBrushLength = cachedLength;

        int maxTaps = (erosionRadius * 2 + 1) * (erosionRadius * 2 + 1);
        relativeBrushOffsets = new int[maxTaps];
        relativeBrushWeights = new float[maxTaps];
        relativeBrushCount = 0;

        float weightSum = 0f;
        for (int z = -erosionRadius; z <= erosionRadius; z++)
        {
            for (int x = -erosionRadius; x <= erosionRadius; x++)
            {
                float sqrDst = x * x + z * z;
                if (sqrDst < erosionRadius * erosionRadius)
                {
                    float weight = 1f - Mathf.Sqrt(sqrDst) / erosionRadius;
                    weightSum += weight;
                    relativeBrushOffsets[relativeBrushCount] = z * cachedWidth + x;
                    relativeBrushWeights[relativeBrushCount] = weight;
                    relativeBrushCount++;
                }
            }
        }

        for (int j = 0; j < relativeBrushCount; j++)
        {
            relativeBrushWeights[j] /= weightSum;
        }
    }

    // translucent mesh for water
    private void SetupWaterMeshObject()
    {
        Transform existing = transform.Find("WaterVolumeMesh");
        if (existing != null)
        {
            waterMeshObj = existing.gameObject;
        }
        else
        {
            waterMeshObj = new GameObject("WaterVolumeMesh");
            waterMeshObj.transform.SetParent(this.transform, false);
            waterMeshObj.transform.localPosition = Vector3.zero;
        }

        MeshFilter wf = waterMeshObj.GetComponent<MeshFilter>();
        if (wf == null) wf = waterMeshObj.AddComponent<MeshFilter>();

        waterRenderer = waterMeshObj.GetComponent<MeshRenderer>();
        if (waterRenderer == null) waterRenderer = waterMeshObj.AddComponent<MeshRenderer>();

        waterMesh = new Mesh();
        waterMesh.name = "Translucent Water Mesh";
        waterMesh.indexFormat = IndexFormat.UInt32;
        waterMesh.MarkDynamic();
        wf.mesh = waterMesh;

        // default mat vertex-colored alpha transparency
        Shader transShader = Shader.Find("Sprites/Default");
        if (transShader != null)
        {
            Material waterMat = new Material(transShader);
            waterRenderer.material = waterMat;
            waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    // prioritizes thin rivers and makes lakes/lagoons rare
    private void ComputeVolumedWaterData(float[] heightMap)
    {
        int w = cachedWidth;
        int l = cachedLength;
        int vertCount = w * l;
        if (waterVertsBuffer == null || waterVertsBuffer.Length != vertCount) return;

        System.Array.Fill(waterSurfaceMap, float.MinValue);

        // prevent high res from making hundreds of streams
        float gridAreaScale = (vertCount / 90000f);
        float gridLinearScale = Mathf.Sqrt(gridAreaScale);
        float effectiveTrunkThreshold = Mathf.Max(riverFluxThreshold, 1800f * gridAreaScale);
        float fixedCoastlineY = (COASTAL_SHELF_NORMALIZED - 0.35f) * heightSpacing;

        // place water along the main river channels 
        if (waterDischargeMap != null && riverCellIndices != null)
        {
            for (int r = 0; r < riverCellCount; r++)
            {
                int idx = riverCellIndices[r];
                float flux = waterDischargeMap[idx];

                if (flux >= effectiveTrunkThreshold)
                {
                    int z = idx / w;
                    int x = idx - z * w;

                    float excess = (flux - effectiveTrunkThreshold) / effectiveTrunkThreshold;
                    float coastalProximity = 1f - Mathf.Clamp01((heightMap[idx] - fixedCoastlineY) / (heightSpacing * 0.08f));
                    float rawBankRadius = (0.75f + Mathf.Sqrt(excess) * 0.65f + coastalProximity * 1.75f) * gridLinearScale;
                    int bankRadius = Mathf.Clamp(Mathf.RoundToInt(rawBankRadius), 1, 5);

                    float surfaceLevel = Mathf.Max(oceanSeaLevel, heightMap[idx] + 0.24f + Mathf.Min(0.55f, Mathf.Sqrt(excess) * 0.16f));

                    for (int dz = -bankRadius; dz <= bankRadius; dz++)
                    {
                        int nz = z + dz;
                        if (nz < 0 || nz >= l) continue;
                        int nRow = nz * w;

                        for (int dx = -bankRadius; dx <= bankRadius; dx++)
                        {
                            int nx = x + dx;
                            if (nx < 0 || nx >= w) continue;

                            if (dx * dx + dz * dz <= bankRadius * bankRadius)
                            {
                                int nIdx = nRow + nx;
                                if (surfaceLevel > waterSurfaceMap[nIdx])
                                {
                                    waterSurfaceMap[nIdx] = surfaceLevel;
                                }
                            }
                        }
                    }
                }
            }
        }

        // set transparency for water
        Color32 riverColor = new Color(0.14f, 0.62f, 0.88f, waterOpacity * 0.92f);
        Color32 shallowWater = new Color(0.15f, 0.62f, 0.85f, waterOpacity * 0.75f);
        Color32 deepWater = new Color(0.03f, 0.18f, 0.46f, Mathf.Min(0.96f, waterOpacity * 1.28f));
        Color32 transparentLand = new Color32(0, 0, 0, 0);

        float sp = spacing;
        float sea = oceanSeaLevel;
        float hSp = heightSpacing;
        Vector3[] wVerts = waterVertsBuffer;
        Color32[] wCols = waterColsBuffer;
        float[] wSurf = waterSurfaceMap;

        Parallel.For(0, l, z =>
        {
            int row = z * w;
            float worldZ = z * sp;
            for (int x = 0; x < w; x++)
            {
                int idx = row + x;
                float groundY = heightMap[idx];
                float riverSurfaceY = wSurf[idx];

                if (sea > groundY + 0.05f)
                {
                    // deep ocean & coastal estuary colors 
                    wVerts[idx] = new Vector3(x * sp, sea, worldZ);
                    float depthFactor = Mathf.Clamp01((sea - groundY) / (hSp * 0.18f));
                    wCols[idx] = Color32.Lerp(shallowWater, deepWater, depthFactor);
                }
                else if (riverSurfaceY > float.MinValue * 0.5f && riverSurfaceY >= groundY - 0.20f)
                {
                    // main river colors
                    float finalY = Mathf.Max(riverSurfaceY, groundY + 0.18f);
                    wVerts[idx] = new Vector3(x * sp, finalY, worldZ);
                    float depthBlend = Mathf.Clamp01((finalY - groundY) / 1.4f);
                    wCols[idx] = Color32.Lerp(riverColor, deepWater, depthBlend * 0.5f);
                }
                else
                {
                    // land colors
                    wVerts[idx] = new Vector3(x * sp, groundY - 0.5f, worldZ);
                    wCols[idx] = transparentLand;
                }
            }
        });

        // build triangle index list for wet cells only to eliminate transparent land overdraw
        int[] rowCounts = wetRowCounts;
        int[] rowOffsets = wetRowOffsets;
        int[] wTris = waterTrianglesBuffer;

        Parallel.For(0, l - 1, z =>
        {
            int row = z * w;
            int count = 0;
            for (int x = 0; x < w - 1; x++)
            {
                int v = row + x;
                if ((wCols[v].a | wCols[v + 1].a | wCols[v + w].a | wCols[v + w + 1].a) != 0)
                {
                    count++;
                }
            }
            rowCounts[z] = count;
        });

        int totalWetQuads = 0;
        for (int z = 0; z < l - 1; z++)
        {
            rowOffsets[z] = totalWetQuads * 6;
            totalWetQuads += rowCounts[z];
        }
        currentWetQuadCount = totalWetQuads;

        Parallel.For(0, l - 1, z =>
        {
            if (rowCounts[z] == 0) return;
            int row = z * w;
            int triIdx = rowOffsets[z];
            for (int x = 0; x < w - 1; x++)
            {
                int v = row + x;
                if ((wCols[v].a | wCols[v + 1].a | wCols[v + w].a | wCols[v + w + 1].a) != 0)
                {
                    if (((x + z) & 1) == 0)
                    {
                        wTris[triIdx + 0] = v;
                        wTris[triIdx + 1] = v + w;
                        wTris[triIdx + 2] = v + 1;

                        wTris[triIdx + 3] = v + 1;
                        wTris[triIdx + 4] = v + w;
                        wTris[triIdx + 5] = v + w + 1;
                    }
                    else
                    {
                        wTris[triIdx + 0] = v;
                        wTris[triIdx + 1] = v + w;
                        wTris[triIdx + 2] = v + w + 1;

                        wTris[triIdx + 3] = v;
                        wTris[triIdx + 4] = v + w + 1;
                        wTris[triIdx + 5] = v + 1;
                    }
                    triIdx += 6;
                }
            }
        });
    }

    private void ApplyWaterMeshToUnity(Bounds worldBounds)
    {
        if (waterMeshObj == null) SetupWaterMeshObject();
        waterMeshObj.SetActive(enableVolumedWater);
        if (!enableVolumedWater) return;

        int vertCount = cachedWidth * cachedLength;
        waterMesh.SetVertices(waterVertsBuffer, 0, vertCount, FAST_MESH_FLAGS);
        waterMesh.SetColors(waterColsBuffer, 0, vertCount, FAST_MESH_FLAGS);
        waterMesh.SetIndices(waterTrianglesBuffer, 0, currentWetQuadCount * 6, MeshTopology.Triangles, 0, false, 0);
        waterMesh.bounds = worldBounds;
    }

    private void ComputeShoreDistance(float[] heightMap)
    {
        int w = cachedWidth;
        int l = cachedLength;
        float[] dist = shoreDistanceBuffer;
        const float diag = 1.4142135f;

        for (int i = 0; i < dist.Length; i++)
        {
            bool isWater = oceanSeaLevel > heightMap[i] + 0.05f;
            if (!isWater && enableVolumedWater)
            {
                float surfaceY = waterSurfaceMap[i];
                isWater = surfaceY > float.MinValue * 0.5f && surfaceY >= heightMap[i] - 0.20f;
            }
            dist[i] = isWater ? 0f : 1000000f;
        }

        for (int z = 0; z < l; z++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                float d = dist[i];
                if (x > 0) d = Mathf.Min(d, dist[i - 1] + 1f);
                if (z > 0)
                {
                    d = Mathf.Min(d, dist[i - w] + 1f);
                    if (x > 0) d = Mathf.Min(d, dist[i - w - 1] + diag);
                    if (x < w - 1) d = Mathf.Min(d, dist[i - w + 1] + diag);
                }
                dist[i] = d;
            }
        }

        for (int z = l - 1; z >= 0; z--)
        {
            for (int x = w - 1; x >= 0; x--)
            {
                int i = z * w + x;
                float d = dist[i];
                if (x < w - 1) d = Mathf.Min(d, dist[i + 1] + 1f);
                if (z < l - 1)
                {
                    d = Mathf.Min(d, dist[i + w] + 1f);
                    if (x < w - 1) d = Mathf.Min(d, dist[i + w + 1] + diag);
                    if (x > 0) d = Mathf.Min(d, dist[i + w - 1] + diag);
                }
                dist[i] = d;
            }
        }
        shoreDistanceDirty = false;
    }

    private void BakeGradientLUT()
    {
        if (elevationGradient == null) InitializeDefaultGradient();
        float invMax = 1f / (GRADIENT_LUT_SIZE - 1);
        for (int i = 0; i < GRADIENT_LUT_SIZE; i++)
        {
            gradientLUT[i] = elevationGradient.Evaluate(i * invMax);
        }
    }

    public void UpdateTerrainColors(float[] heightMap, Vector3[] normals)
    {
        BakeGradientLUT();
        UpdateTerrainColorsCompute(heightMap, normals);
        terrainMesh.SetColors(colors, 0, colors.Length, FAST_MESH_FLAGS);
    }

    private void UpdateTerrainColorsCompute(float[] heightMap, Vector3[] normals)
    {
        // flat elevation
        Color32 deepOceanTrenchColor = new Color(0.28f, 0.30f, 0.35f); // dark oceanic crust 
        Color32 sandBasinColor = new Color(0.76f, 0.70f, 0.50f);   // low riverbed/shore sand
        Color32 valleyGreenColor = new Color(0.26f, 0.48f, 0.22f); // lush valley & foothills green
        Color32 mountainRockColor = new Color(0.52f, 0.50f, 0.46f);// grey mountain rock
        Color32 alpineSnowColor = new Color(0.94f, 0.95f, 0.97f);  // high peak snow
        Color32 neutralFlatColor = new Color(0.75f, 0.75f, 0.75f); // neutral gray

        // slope colors
        Color32 talusSlopeColor = new Color(0.60f, 0.45f, 0.31f);  // terracotta talus
        Color32 steepCliffColor = new Color(0.27f, 0.27f, 0.29f);  // dark slate cliff
        Color32 streamDebugColor = new Color(0.15f, 0.65f, 0.95f); // cyan river 

        int w = cachedWidth;
        int l = cachedLength;
        bool useSlope = useSlopeDiagnosticColors && normals != null;
        bool useElev = useElevationDevColors;
        bool showDebug = showWaterDischargeDebug;
        float fluxThresh = riverFluxThreshold;
        float hSp = heightSpacing;
        float sea = oceanSeaLevel;
        float fixedCoastlineY = (COASTAL_SHELF_NORMALIZED - 0.35f) * hSp;

        float rockY = rockStartAltitude;
        float slateY = Mathf.Max(rockY + 0.1f, alpineSlateAltitude);
        float snowY = Mathf.Max(slateY + 0.1f, snowStartAltitude);
        float valleyStartY = fixedCoastlineY + Mathf.Max(0.5f, (rockY - fixedCoastlineY) * 0.08f);

        float sandMaxWorldDelta = sandMaxAltitude * hSp;
        float sRadius = sandShoreRadius;
        float[] dischargeMap = waterDischargeMap;
        float[] wSurf = waterSurfaceMap;
        float[] shoreDist = shoreDistanceBuffer;
        Color32[] lut = gradientLUT;
        Color32[] cols = colors;

        if (!useSlope && !useElev && shoreDistanceDirty)
        {
            ComputeShoreDistance(heightMap);
        }

        Parallel.For(0, l, z =>
        {
            int row = z * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                float h = heightMap[i];

                if (!useSlope && !useElev)
                {
                    float gradT;
                    if (h <= fixedCoastlineY)
                    {
                        gradT = 0f;
                    }
                    else if (h < valleyStartY)
                    {
                        gradT = Mathf.Lerp(0.0004f, 0.05f, (h - fixedCoastlineY) / (valleyStartY - fixedCoastlineY));
                    }
                    else if (h < rockY)
                    {
                        gradT = Mathf.Lerp(0.05f, 0.55f, (h - valleyStartY) / Mathf.Max(0.01f, rockY - valleyStartY));
                    }
                    else if (h < slateY)
                    {
                        gradT = Mathf.Lerp(0.55f, 0.78f, (h - rockY) / (slateY - rockY));
                    }
                    else if (h < snowY)
                    {
                        gradT = Mathf.Lerp(0.78f, 0.88f, (h - slateY) / (snowY - slateY));
                    }
                    else
                    {
                        gradT = Mathf.Lerp(0.88f, 1f, Mathf.Clamp01((h - snowY) / 15f));
                    }

                    float localWaterRefY = Mathf.Max(sea, fixedCoastlineY);
                    if (wSurf != null && wSurf[i] > float.MinValue * 0.5f)
                    {
                        localWaterRefY = Mathf.Max(localWaterRefY, wSurf[i]);
                    }
                    float heightAboveWater = Mathf.Max(0f, h - localWaterRefY);
                    float altitudeFade = 1f - Mathf.InverseLerp(sandMaxWorldDelta * 0.5f, sandMaxWorldDelta, heightAboveWater);
                    float shoreFade = 1f - Mathf.Clamp01(shoreDist[i] / sRadius);

                    int lutIdx = Mathf.Clamp((int)(gradT * (GRADIENT_LUT_SIZE - 1) + 0.5f), 0, GRADIENT_LUT_SIZE - 1);
                    cols[i] = Color32.Lerp(lut[lutIdx], sandBasinColor, altitudeFade * shoreFade);
                }
                else
                {
                    Color32 baseHeightColor;
                    if (useElev)
                    {
                        if (h < fixedCoastlineY)
                        {
                            float oceanDepthT = Mathf.Clamp01((fixedCoastlineY - h) / (hSp * 0.16f));
                            baseHeightColor = Color32.Lerp(sandBasinColor, deepOceanTrenchColor, oceanDepthT);
                        }
                        else if (h < valleyStartY)
                        {
                            baseHeightColor = Color32.Lerp(sandBasinColor, valleyGreenColor, (h - fixedCoastlineY) / (valleyStartY - fixedCoastlineY));
                        }
                        else if (h < rockY)
                        {
                            baseHeightColor = Color32.Lerp(valleyGreenColor, mountainRockColor, (h - valleyStartY) / Mathf.Max(0.01f, rockY - valleyStartY));
                        }
                        else if (h < snowY)
                        {
                            baseHeightColor = Color32.Lerp(mountainRockColor, alpineSnowColor, (h - rockY) / (snowY - rockY));
                        }
                        else
                        {
                            baseHeightColor = alpineSnowColor;
                        }
                    }
                    else
                    {
                        baseHeightColor = neutralFlatColor;
                    }

                    // overlay Slope Diagnostic colors if enabled
                    if (!useSlope)
                    {
                        cols[i] = baseHeightColor;
                    }
                    else
                    {
                        float ny = Mathf.Clamp(normals[i].y, -1f, 1f);
                        if (ny > 0.939f) // < 20 degrees
                        {
                            cols[i] = baseHeightColor;
                        }
                        else if (ny > 0.809f) // 20 to 36 degrees
                        {
                            float t = (0.939f - ny) / 0.130f;
                            cols[i] = Color32.Lerp(baseHeightColor, talusSlopeColor, t * 0.85f);
                        }
                        else // > 36 degrees
                        {
                            float t = Mathf.Clamp01((0.809f - ny) / 0.235f);
                            cols[i] = Color32.Lerp(talusSlopeColor, steepCliffColor, t);
                        }
                    }
                }

                // debug overlay for hydraulic erosion
                if (showDebug && dischargeMap != null && i < dischargeMap.Length)
                {
                    float discharge = dischargeMap[i];
                    if (discharge > fluxThresh)
                    {
                        float streamBlend = Mathf.Clamp01((discharge - fluxThresh) / (fluxThresh * 2f));
                        cols[i] = Color32.Lerp(cols[i], streamDebugColor, streamBlend * 0.85f);
                    }
                }
            }
        });
    }

    private void AllocateMeshArrays()
    {
        int vertCount = cachedWidth * cachedLength;
        vertices = new Vector3[vertCount];
        normalsBuffer = new Vector3[vertCount];
        colors = new Color32[vertCount];
        currentHeightMap = new float[vertCount];
        normalizedMapBuffer = new float[vertCount];
        shoreDistanceBuffer = new float[vertCount];
        carvedHeightsBuffer = new float[vertCount];
        waterVertsBuffer = new Vector3[vertCount];
        waterColsBuffer = new Color32[vertCount];

        waterDischargeMap = new float[vertCount];
        waterSurfaceMap = new float[vertCount];

        downstreamParent = new int[vertCount];
        parentDirBuffer = new byte[vertCount];
        topologicalOrder = new int[vertCount];
        nodeVisited = new bool[vertCount];
        bucketHeads = new int[PRIORITY_BUCKETS];
        bucketNextNode = new int[vertCount];
        riverCellIndices = new int[vertCount];

        int totalIndices = (cachedWidth - 1) * (cachedLength - 1) * 6;
        triangles = new int[totalIndices];
        waterTrianglesBuffer = new int[totalIndices];
        wetRowCounts = new int[cachedLength - 1];
        wetRowOffsets = new int[cachedLength - 1];

        int w = cachedWidth;
        int l = cachedLength;
        Parallel.For(0, l - 1, z =>
        {
            int row = z * w;
            int triIndex = z * (w - 1) * 6;
            for (int x = 0; x < w - 1; x++)
            {
                int vertIndex = row + x;
                // removes unrealistic line creases across ridges
                if (((x + z) & 1) == 0)
                {
                    triangles[triIndex + 0] = vertIndex;
                    triangles[triIndex + 1] = vertIndex + w;
                    triangles[triIndex + 2] = vertIndex + 1;

                    triangles[triIndex + 3] = vertIndex + 1;
                    triangles[triIndex + 4] = vertIndex + w;
                    triangles[triIndex + 5] = vertIndex + w + 1;
                }
                else
                {
                    triangles[triIndex + 0] = vertIndex;
                    triangles[triIndex + 1] = vertIndex + w;
                    triangles[triIndex + 2] = vertIndex + w + 1;

                    triangles[triIndex + 3] = vertIndex;
                    triangles[triIndex + 4] = vertIndex + w + 1;
                    triangles[triIndex + 5] = vertIndex + 1;
                }
                triIndex += 6;
            }
        });

        if (waterMesh != null)
        {
            waterMesh.Clear();
        }
    }

    // generates a full-range normalized [0, 1] heightmap 
    public float[] GenerateNormalizedHeightMap()
    {
        int w = cachedWidth > 0 ? cachedWidth : gridWidth;
        int l = cachedLength > 0 ? cachedLength : gridLength;
        int vertCount = w * l;
        if (normalizedMapBuffer == null || normalizedMapBuffer.Length != vertCount)
        {
            normalizedMapBuffer = new float[vertCount];
        }
        float[] heightMap = normalizedMapBuffer;

        System.Random prng = new System.Random(seed);
        int oct = octaves;
        Vector2[] octaveOffsets = new Vector2[oct];

        for (int i = 0; i < oct; i++)
        {
            float offsetX = prng.Next(-100000, 100000);
            float offsetZ = prng.Next(-100000, 100000);
            octaveOffsets[i] = new Vector2(offsetX, offsetZ);
        }

        // Low-frequency offsets to make mountain ranges
        Vector2 macroOffset1 = new Vector2(prng.Next(-50000, 50000), prng.Next(-50000, 50000));
        Vector2 macroOffset2 = new Vector2(prng.Next(-50000, 50000), prng.Next(-50000, 50000));

        float scale = noiseScale <= 0f ? 0.0001f : noiseScale;
        float halfWidth = w / 2f;
        float halfLength = l / 2f;
        float aMult = ampMult;
        float fMult = freqMult;
        float rSharp = ridgeSharpness;
        float macroContrast = macroRangeContrast;
        float macroInvScale = 1f / Mathf.Max(0.0001f, macroScale);
        float beltStrength = mountainBeltStrength;

        // precompute low-frequency macro noise on a coarse 4x4 grid and bilinearly interpolate
        const int STEP = 4;
        const float INV_STEP = 1f / STEP;
        int cw = (w - 1) / STEP + 2;
        int cl = (l - 1) / STEP + 2;
        if (coarseMacroBuffer == null || coarseMacroBuffer.Length < cw * cl)
        {
            coarseMacroBuffer = new float[cw * cl];
        }
        float[] coarseMacro = coarseMacroBuffer;

        Parallel.For(0, cl, cz =>
        {
            int z = cz * STEP;
            float macroZ = (z - halfLength) * macroInvScale;
            int cRow = cz * cw;
            for (int cx = 0; cx < cw; cx++)
            {
                int x = cx * STEP;
                float macroX = (x - halfWidth) * macroInvScale;
                // creates mountain plateaus and low basins
                float macro1 = Mathf.PerlinNoise(macroX + macroOffset1.x, macroZ + macroOffset1.y);
                float macro2 = Mathf.PerlinNoise(macroX * 1.857f + macroOffset2.x, macroZ * 1.857f + macroOffset2.y);
                float macroBlend = Mathf.Clamp01(macro1 * 0.7f + macro2 * 0.3f);
                float blobMask = Mathf.Pow(macroBlend, 2.1f);

                // ridging the low-frequency noise turns its mid-level contour into long continuous belts
                float belt = 1f - Mathf.Abs(macroBlend * 2f - 1f);
                float macroMask = Mathf.Lerp(blobMask, belt * belt * belt, beltStrength);
                coarseMacro[cRow + cx] = Mathf.Lerp(1f, 0.08f + 1.92f * macroMask, macroContrast);
            }
        });

        // multi-threaded noise generation 
        Parallel.For(0, l, z =>
        {
            int row = z * w;
            float relZ = (z - halfLength) / scale;
            int cz = z >> 2;
            float fz = (z & 3) * INV_STEP;
            int cRow0 = cz * cw;
            int cRow1 = (cz + 1) * cw;

            for (int x = 0; x < w; x++)
            {
                float amplitude = 1f;
                float frequency = 1f;
                float noiseHeight = 0f;

                float relX = (x - halfWidth) / scale;

                for (int i = 0; i < oct; i++)
                {
                    float sampleX = relX * frequency + octaveOffsets[i].x;
                    float sampleZ = relZ * frequency + octaveOffsets[i].y;

                    float rawPerlin = Mathf.PerlinNoise(sampleX, sampleZ); // [0, 1]

                    // creates sharp mountain crests
                    float ridgeVal = 1f - Mathf.Abs(rawPerlin * 2f - 1f);
                    ridgeVal *= ridgeVal; // Square for sharper ridges

                    // blend with normal perlin noise
                    float combined = Mathf.Lerp(rawPerlin, ridgeVal, rSharp);

                    noiseHeight += combined * amplitude;

                    amplitude *= aMult;
                    frequency *= fMult;
                }

                int cx = x >> 2;
                float fx = (x & 3) * INV_STEP;
                float m00 = coarseMacro[cRow0 + cx];
                float m10 = coarseMacro[cRow0 + cx + 1];
                float m01 = coarseMacro[cRow1 + cx];
                float m11 = coarseMacro[cRow1 + cx + 1];
                float macroMult = Mathf.Lerp(Mathf.Lerp(m00, m10, fx), Mathf.Lerp(m01, m11, fx), fz);

                heightMap[row + x] = noiseHeight * macroMult;
            }
        });

        float[] rowMin = shoreDistanceBuffer;
        float[] rowMax = carvedHeightsBuffer;
        Parallel.For(0, l, z =>
        {
            int row = z * w;
            float rMin = float.MaxValue;
            float rMax = float.MinValue;
            for (int x = 0; x < w; x++)
            {
                float v = heightMap[row + x];
                if (v < rMin) rMin = v;
                if (v > rMax) rMax = v;
            }
            rowMin[z] = rMin;
            rowMax[z] = rMax;
        });

        float minNoise = float.MaxValue;
        float maxNoise = float.MinValue;
        for (int z = 0; z < l; z++)
        {
            if (rowMin[z] < minNoise) minNoise = rowMin[z];
            if (rowMax[z] > maxNoise) maxNoise = rowMax[z];
        }

        // normalize to [0, 1] in parallel
        float range = Mathf.Max(0.0001f, maxNoise - minNoise);
        float invRange = 1f / range;
        float mPower = mountainPower;

        Parallel.For(0, l, z =>
        {
            int row = z * w;
            for (int x = 0; x < w; x++)
            {
                int idx = row + x;
                float norm = (heightMap[idx] - minNoise) * invRange;
                heightMap[idx] = Mathf.Pow(norm, mPower);
            }
        });

        return heightMap;
    }

    private void InitializeDefaultGradient()
    {
        elevationGradient = new Gradient();
        elevationGradient.colorKeys = new GradientColorKey[]
        {
            new GradientColorKey(new Color(0.472f, 0.592f, 0.350f), 0.0004f), // plains green
            new GradientColorKey(new Color(0.28f, 0.52f, 0.25f), 0.05f),// valley green
            new GradientColorKey(new Color(0.52f, 0.50f, 0.48f), 0.55f),// grey mountain rock
            new GradientColorKey(new Color(0.35f, 0.34f, 0.36f), 0.78f),// alpine slate
            new GradientColorKey(Color.white, 0.88f)                    // snow peak
        };
    }
}