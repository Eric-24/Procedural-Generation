using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using System.Threading.Tasks;

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
    [Range(-40f, 20f)] public float oceanSeaLevel = -28f; 
    [Range(0f, 1f)] public float waterOpacity = 0.78f;
    [Range(20f, 5000f)] public float riverFluxThreshold = 2400f; // controls how thin/defined main rivers are
    [Range(0f, 1f)] public float lakeRarity = 0.85f; // higher is more rare

    [Header("Dev Visualization Settings")]
    public bool useSlopeDiagnosticColors = false;
    public bool useElevationDevColors = false;
    public bool showWaterDischargeDebug = false;
    public Gradient elevationGradient;

    [SerializeField]
    private BiomeGen biomeGenerator;
    private Mesh terrainMesh;
    private MeshRenderer meshRenderer;

    // child mesh for translucent volumed water
    private GameObject waterMeshObj;
    private Mesh waterMesh;
    private MeshRenderer waterRenderer;

    private Vector3[] vertices;
    private int[] triangles;
    private Vector2[] uvs;
    private Color[] colors;
    private float[] currentHeightMap;
    private float[] normalizedMapBuffer;
    private Vector3[] waterVertsBuffer;
    private Color[] waterColsBuffer;

    // arrays to store water data during erosion
    [HideInInspector] public float[] waterDischargeMap;
    [HideInInspector] public Vector2[] flowDirectionMap;
    [HideInInspector] public float[] waterSurfaceMap; // stores lake/ocean/stream surface heights

    // river network graph buffers 
    private int[] downstreamParent;
    private int[] topologicalOrder;
    private bool[] nodeVisited;
    private int[] bucketHeads;
    private int[] bucketNextNode;
    private const int PRIORITY_BUCKETS = 2048;

    // erosion brush settings
    private int[][] brushIndexOffsets;
    private float[][] brushWeights;
    private int[] relativeBrushOffsets;
    private float[] relativeBrushWeights;
    private int relativeBrushCount;
    private int cachedBrushRadius = -1;
    private int cachedBrushWidth = -1;
    private int cachedBrushLength = -1;

    private int cachedWidth;
    private int cachedLength;

    private static readonly int[] DX = { -1, 1, 0, 0, -1, 1, -1, 1 };
    private static readonly int[] DZ = { 0, 0, -1, 1, -1, -1, 1, 1 };
    private static readonly float[] DIST = { 1f, 1f, 1f, 1f, 1.4142135f, 1.4142135f, 1.4142135f, 1.4142135f };
    private static readonly float[] INV_DIST = { 1f, 1f, 1f, 1f, 0.70710678f, 0.70710678f, 0.70710678f, 0.70710678f };

    void Awake()
    {
        if (biomeGenerator == null)
        {
            biomeGenerator = GetComponent<BiomeGen>();
        }

        terrainMesh = new Mesh();
        terrainMesh.name = "Procedural Terrain Mesh";
        terrainMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        GetComponent<MeshFilter>().mesh = terrainMesh;
        meshRenderer = GetComponent<MeshRenderer>();

        if (meshRenderer != null && meshRenderer.material != null)
        {
            meshRenderer.material.SetFloat("_Glossiness", 0.1f);
        }

        InitializeDefaultGradient();
        SetupWaterMeshObject();
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
        GenerateDotMap();
    }

    // toggle color checkboxes 
    void OnValidate()
    {
        if (Application.isPlaying && terrainMesh != null && currentHeightMap != null && vertices != null)
        {
            if (currentHeightMap.Length == vertices.Length)
            {
                UpdateTerrainColors(currentHeightMap, terrainMesh.normals);
                UpdateVolumedWaterMesh(currentHeightMap);
            }
        }
    }

    public void GenerateDotMap()
    {
        if (terrainMesh == null) return;

        bool dimensionsChanged = (vertices == null || cachedWidth != gridWidth || cachedLength != gridLength);
        if (dimensionsChanged)
        {
            cachedWidth = gridWidth;
            cachedLength = gridLength;
            AllocateMeshArrays();
        }

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
        int w = gridWidth;
        int l = gridLength;
        float sp = spacing;
        float hSp = heightSpacing;

        Parallel.For(0, l, z =>
        {
            int rowOffset = z * w;
            float worldZ = z * sp;
            for (int x = 0; x < w; x++)
            {
                int index = rowOffset + x;
                float worldY = (normalizedMap[index] - 0.35f) * hSp;
                currentHeightMap[index] = worldY;
                vertices[index] = new Vector3(x * sp, worldY, worldZ);
            }
        });

        if (dimensionsChanged)
        {
            terrainMesh.Clear();
            terrainMesh.vertices = vertices;
            terrainMesh.triangles = triangles;
            terrainMesh.uv = uvs;
        }
        else
        {
            terrainMesh.vertices = vertices; // only vertices to save compute
        }

        terrainMesh.RecalculateNormals();
        terrainMesh.RecalculateBounds();

        // generate biomes after terrain is generated
        if (biomeGenerator != null)
        {
            biomeGenerator.GenerateBiomes(
                normalizedMap,
                currentHeightMap,
                waterDischargeMap,
                terrainMesh.normals,
                gridWidth,
                gridLength,
                spacing,
                oceanSeaLevel,
                seed
            );
        }

        // color terrain by slope and elevation
        UpdateTerrainColors(currentHeightMap, terrainMesh.normals);

        // build translucent water mesh for rivers and lakes
        UpdateVolumedWaterMesh(currentHeightMap);
    }

    private void SimulateHydraulicErosion(float[] heightMap)
    {
        // minimum radius is 2
        if (erosionRadius < 2) erosionRadius = 2;
        InitializeErosionBrush();

        // deterministic offset from seed so droplet paths are reproducible
        uint rngState = (uint)(seed + 1337) * 747796405u + 2891336453u;
        if (rngState == 0) rngState = 1337u;

        int safeMargin = erosionRadius + 2;
        float spawnW = gridWidth - safeMargin * 2;
        float spawnL = gridLength - safeMargin * 2;
        if (spawnW <= 2f || spawnL <= 2f) return;

        for (int iteration = 0; iteration < erosionIterations; iteration++)
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

            for (int lifetime = 0; lifetime < maxDropletLifetime; lifetime++)
            {
                int nodeX = (int)posX;
                int nodeZ = (int)posZ;
                int dropletIndex = nodeZ * gridWidth + nodeX;

                float cellOffsetX = posX - nodeX;
                float cellOffsetZ = posZ - nodeZ;

                // calculate droplet's current height and surface gradient
                SampleHeightAndGradient(heightMap, posX, posZ, out float height, out float gradX, out float gradZ);

                // update direction with inertia and normalize
                dirX = dirX * erosionInertia - gradX * (1f - erosionInertia);
                dirZ = dirZ * erosionInertia - gradZ * (1f - erosionInertia);

                float len = Mathf.Sqrt(dirX * dirX + dirZ * dirZ);
                if (len != 0f)
                {
                    dirX /= len;
                    dirZ /= len;
                }

                posX += dirX;
                posZ += dirZ;

                // stop once droplet stopped moving or flowed off the edge of the map
                if ((dirX == 0f && dirZ == 0f) || posX < safeMargin || posX >= gridWidth - safeMargin || posZ < safeMargin || posZ >= gridLength - safeMargin)
                {
                    break;
                }

                SampleHeightAndGradient(heightMap, posX, posZ, out float newHeight, out _, out _);
                float deltaHeight = newHeight - height;

                // calculate sediment capacity on normalized heightmap
                float sedimentCapacity = Mathf.Max(-deltaHeight * speed * water * sedimentCapacityFactor, minSedimentCapacity);

                if (sediment > sedimentCapacity || deltaHeight > 0f)
                {
                    float amountToDeposit = (deltaHeight > 0f)
                        ? Mathf.Min(deltaHeight, sediment)
                        : (sediment - sedimentCapacity) * depositSpeed;

                    sediment -= amountToDeposit;

                    // bilinearly deposit sediment 
                    heightMap[dropletIndex] += amountToDeposit * (1f - cellOffsetX) * (1f - cellOffsetZ);
                    heightMap[dropletIndex + 1] += amountToDeposit * cellOffsetX * (1f - cellOffsetZ);
                    heightMap[dropletIndex + gridWidth] += amountToDeposit * (1f - cellOffsetX) * cellOffsetZ;
                    heightMap[dropletIndex + gridWidth + 1] += amountToDeposit * cellOffsetX * cellOffsetZ;
                }
                else
                {
                    // moving downhill with spare capacity
                    float amountToErode = Mathf.Min((sedimentCapacity - sediment) * erodeSpeed, -deltaHeight);

                    for (int i = 0; i < relativeBrushCount; i++)
                    {
                        int weightedIndex = dropletIndex + relativeBrushOffsets[i];
                        float weighedErodeAmount = amountToErode * relativeBrushWeights[i];
                        heightMap[weightedIndex] -= weighedErodeAmount;
                        sediment += weighedErodeAmount;
                    }
                }

                // update droplet speed and evaporate
                speed = Mathf.Sqrt(Mathf.Max(0f, speed * speed - deltaHeight * gravity));
                water *= (1f - evaporateSpeed);
            }
        }
    }

    // grows a continuous drainage tree from the low valleys up into the mountains.
    // reconstructs terrain height upward along the river tree inversely proportional to water volume
    private void ComputeThinRiverDrainage(float[] heightMap)
    {
        int vertCount = gridWidth * gridLength;
        System.Array.Clear(nodeVisited, 0, vertCount);
        System.Array.Fill(bucketHeads, -1);
        System.Array.Fill(downstreamParent, -1);

        // create river mouths at lowest elevation or boundary regions
        int minBucket = PRIORITY_BUCKETS - 1;
        for (int z = 0; z < gridLength; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (x == 0 || x == gridWidth - 1 || z == 0 || z == gridLength - 1)
                {
                    int idx = z * gridWidth + x;
                    if (heightMap[idx] < 0.38f)
                    {
                        nodeVisited[idx] = true;
                        int b = Mathf.Clamp((int)(heightMap[idx] * (PRIORITY_BUCKETS - 1)), 0, PRIORITY_BUCKETS - 1);
                        bucketNextNode[idx] = bucketHeads[b];
                        bucketHeads[b] = idx;
                        if (b < minBucket) minBucket = b;
                    }
                }
            }
        }

        // if all boundaries happened to be high, create at the global minimum boundary
        if (minBucket == PRIORITY_BUCKETS - 1)
        {
            int lowestIdx = 0;
            float lowestH = float.MaxValue;
            for (int x = 0; x < gridWidth; x++)
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

            int cx = idx % gridWidth;
            int cz = idx / gridWidth;

            // check which direction this branch came from so tributaries branch naturally
            int parentIdx = downstreamParent[idx];
            int pDx = 0, pDz = 0;
            if (parentIdx != -1)
            {
                pDx = cx - (parentIdx % gridWidth);
                pDz = cz - (parentIdx / gridWidth);
            }

            int startN = ((cx * 73856093) ^ (cz * 19349663) ^ seed) & 7;

            for (int step = 0; step < 8; step++)
            {
                int n = (startN + step) & 7;
                int nx = cx + DX[n];
                int nz = cz + DZ[n];
                if (nx < 0 || nx >= gridWidth || nz < 0 || nz >= gridLength) continue;

                int nIdx = nz * gridWidth + nx;
                if (nodeVisited[nIdx]) continue;

                nodeVisited[nIdx] = true;
                downstreamParent[nIdx] = idx;

                // prioritize edges that align with upstream inertia 
                float alignmentBonus = (pDx * DX[n] + pDz * DZ[n]) * 0.006f;
                float organicJitter = (((nx * 928371 + nz * 523987 + seed) & 1023) / 1023f - 0.5f) * 0.018f;
                float priorityH = Mathf.Clamp01(heightMap[nIdx] - alignmentBonus + organicJitter);

                int nb = Mathf.Clamp((int)(priorityH * (PRIORITY_BUCKETS - 1)), 0, PRIORITY_BUCKETS - 1);
                bucketNextNode[nIdx] = bucketHeads[nb];
                bucketHeads[nb] = nIdx;
                if (nb < curBucket) curBucket = nb;
            }
        }

        // accumulate water volume from highest mountain peaks down to river mouth
        for (int i = 0; i < vertCount; i++) waterDischargeMap[i] = 1f;

        for (int i = topoCount - 1; i >= 0; i--)
        {
            int idx = topologicalOrder[i];
            int pIdx = downstreamParent[idx];
            if (pIdx != -1)
            {
                waterDischargeMap[pIdx] += waterDischargeMap[idx];
            }
        }

        // traverse the river network uphill starting from the mouth
        float[] carvedHeights = new float[vertCount];
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

            int dx = (idx % gridWidth) - (pIdx % gridWidth);
            int dz = (idx / gridWidth) - (pIdx / gridWidth);
            float edgeDist = (dx != 0 && dz != 0) ? 1.4142135f : 1.0f;

            // macro height determines where the general biome (mountain/plains)
            float macroWeight = 0.15f + 1.85f * heightMap[idx];
            float waterVol = waterDischargeMap[idx];

            // slope inversely proportional to water volume
            float targetSlope = (0.042f * macroWeight) / (1f + Mathf.Pow(waterVol, 0.44f) * 0.48f);

            // height delta between nodes for talus effect 
            float maxTalusDelta = 0.024f * edgeDist * (0.45f + 0.55f * heightMap[idx]);
            float stepDelta = Mathf.Min(targetSlope * edgeDist, maxTalusDelta);

            float newH = carvedHeights[pIdx] + stepDelta;
            carvedHeights[idx] = newH;
            if (newH > maxCarved) maxCarved = newH;
        }

        // normalize and blend the reconstructed fern terrain with the macro heightmap
        // also run 2 fast diffusive passes so inter-tree ridge crests meet smoothly without jagged seams
        float invMax = 1f / maxCarved;
        for (int i = 0; i < vertCount; i++)
        {
            float fernH = carvedHeights[i] * invMax;
            heightMap[i] = Mathf.Lerp(heightMap[i], fernH, fernBranchingStrength);
        }

        for (int pass = 0; pass < 2; pass++)
        {
            Parallel.For(1, gridLength - 1, z =>
            {
                int row = z * gridWidth;
                for (int x = 1; x < gridWidth - 1; x++)
                {
                    int idx = row + x;
                    float avg = (heightMap[idx - 1] + heightMap[idx + 1] + heightMap[idx - gridWidth] + heightMap[idx + gridWidth]) * 0.25f;
                    // smooth ridges slightly more than river channels
                    float blend = (waterDischargeMap[idx] > riverFluxThreshold * 0.25f) ? 0.12f : 0.28f;
                    carvedHeights[idx] = Mathf.Lerp(heightMap[idx], avg, blend);
                }
            });
            System.Array.Copy(carvedHeights, 1, heightMap, 1, vertCount - 2);
        }

        // carve a wide riverbed trough for major rivers (where discharge > riverFluxThreshold)
        for (int z = 3; z < gridLength - 3; z++)
        {
            int row = z * gridWidth;
            for (int x = 3; x < gridWidth - 3; x++)
            {
                int idx = row + x;
                float flux = waterDischargeMap[idx];
                if (flux > riverFluxThreshold)
                {
                    float excess = (flux - riverFluxThreshold) / riverFluxThreshold;
                    float channelDepth = Mathf.Min(0.022f, 0.006f + Mathf.Sqrt(excess) * 0.006f);
                    int radius = Mathf.Clamp(1 + (int)(Mathf.Sqrt(excess) * 0.8f), 1, 3);

                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            float d2 = dx * dx + dz * dz;
                            if (d2 <= radius * radius)
                            {
                                int nIdx = (z + dz) * gridWidth + (x + dx);
                                float falloff = 1f - Mathf.Sqrt(d2) / (radius + 0.5f);
                                heightMap[nIdx] -= channelDepth * falloff * 0.35f;
                            }
                        }
                    }
                }
            }
        }
    }

    // thermal erosion passes
    private void SimulateThermalErosion(float[] heightMap)
    {
        for (int iter = 0; iter < thermalIterations; iter++)
        {
            for (int z = 1; z < gridLength - 1; z++)
            {
                int row = z * gridWidth;
                for (int x = 1; x < gridWidth - 1; x++)
                {
                    int idx = row + x;
                    float h = heightMap[idx];

                    float maxDiff = 0f;
                    int steepestNeighbor = -1;

                    for (int n = 0; n < 8; n++)
                    {
                        int nIdx = idx + DZ[n] * gridWidth + DX[n];
                        float diff = (h - heightMap[nIdx]) * INV_DIST[n];
                        if (diff > maxDiff)
                        {
                            maxDiff = diff;
                            steepestNeighbor = nIdx;
                        }
                    }

                    // if slope exceeds the talus angle of repose, slide a fraction of dirt down
                    if (maxDiff > talusAngleThreshold && steepestNeighbor != -1)
                    {
                        float transfer = (maxDiff - talusAngleThreshold) * thermalRelaxation;
                        heightMap[idx] -= transfer;
                        heightMap[steepestNeighbor] += transfer;
                    }
                }
            }
        }
    }

    private void SampleHeightAndGradient(float[] heightMap, float posX, float posZ, out float height, out float gradX, out float gradZ)
    {
        int coordX = (int)posX;
        int coordZ = (int)posZ;

        float x = posX - coordX;
        float z = posZ - coordZ;

        int nodeIndexNW = coordZ * gridWidth + coordX;
        float heightNW = heightMap[nodeIndexNW];
        float heightNE = heightMap[nodeIndexNW + 1];
        float heightSW = heightMap[nodeIndexNW + gridWidth];
        float heightSE = heightMap[nodeIndexNW + gridWidth + 1];

        // Bilinear gradient
        gradX = (heightNE - heightNW) * (1f - z) + (heightSE - heightSW) * z;
        gradZ = (heightSW - heightNW) * (1f - x) + (heightSE - heightNE) * x;

        // Bilinear height
        height = heightNW * (1f - x) * (1f - z) + heightNE * x * (1f - z) + heightSW * (1f - x) * z + heightSE * x * z;
    }

    // precompute brush indices and weights
    private void InitializeErosionBrush()
    {
        if (relativeBrushOffsets != null && cachedBrushRadius == erosionRadius && cachedBrushWidth == gridWidth && cachedBrushLength == gridLength)
        {
            return;
        }

        cachedBrushRadius = erosionRadius;
        cachedBrushWidth = gridWidth;
        cachedBrushLength = gridLength;

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
                    relativeBrushOffsets[relativeBrushCount] = z * gridWidth + x;
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
        waterMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        wf.mesh = waterMesh;

        // default mat vertex-colored alpha transparency
        Shader transShader = Shader.Find("Sprites/Default");
        if (transShader != null)
        {
            Material waterMat = new Material(transShader);
            waterRenderer.material = waterMat;
            waterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    // prioritizes thin rivers and makes lakes/lagoons rare
    private void UpdateVolumedWaterMesh(float[] heightMap)
    {
        if (waterMeshObj == null) SetupWaterMeshObject();

        waterMeshObj.SetActive(enableVolumedWater);
        if (!enableVolumedWater) return;

        int vertCount = gridWidth * gridLength;
        for (int i = 0; i < vertCount; i++)
        {
            waterSurfaceMap[i] = float.MinValue;
        }

        // prevent high res from making hundreds of streams
        float gridAreaScale = (vertCount / 90000f);
        float effectiveTrunkThreshold = Mathf.Max(riverFluxThreshold, 1800f * gridAreaScale);

        // place water along the main river channels 
        for (int z = 3; z < gridLength - 3; z++)
        {
            int row = z * gridWidth;
            for (int x = 3; x < gridWidth - 3; x++)
            {
                int idx = row + x;
                float flux = (waterDischargeMap != null) ? waterDischargeMap[idx] : 0f;

                if (flux >= effectiveTrunkThreshold)
                {
                    float excess = (flux - effectiveTrunkThreshold) / effectiveTrunkThreshold;
                    int bankRadius = Mathf.Clamp(1 + (int)(Mathf.Sqrt(excess) * 0.75f), 1, 3);
                    float surfaceLevel = heightMap[idx] + 0.32f + Mathf.Min(0.8f, Mathf.Sqrt(excess) * 0.25f);

                    for (int dz = -bankRadius; dz <= bankRadius; dz++)
                    {
                        for (int dx = -bankRadius; dx <= bankRadius; dx++)
                        {
                            if (dx * dx + dz * dz <= bankRadius * bankRadius)
                            {
                                int nIdx = (z + dz) * gridWidth + (x + dx);
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
        Color riverColor = new Color(0.14f, 0.62f, 0.88f, waterOpacity * 0.92f);
        Color shallowWater = new Color(0.15f, 0.62f, 0.85f, waterOpacity * 0.75f);
        Color deepWater = new Color(0.04f, 0.25f, 0.55f, Mathf.Min(0.95f, waterOpacity * 1.25f));

        int w = gridWidth;
        int l = gridLength;
        float sp = spacing;
        float sea = oceanSeaLevel;
        float hSp = heightSpacing;

        Parallel.For(0, l, z =>
        {
            int row = z * w;
            float worldZ = z * sp;
            for (int x = 0; x < w; x++)
            {
                int idx = row + x;
                float groundY = heightMap[idx];
                float riverSurfaceY = waterSurfaceMap[idx];

                if (riverSurfaceY > float.MinValue * 0.5f && riverSurfaceY >= groundY - 0.25f)
                {
                    // main river colors
                    float finalY = Mathf.Max(riverSurfaceY, groundY + 0.22f);
                    waterVertsBuffer[idx] = new Vector3(x * sp, finalY, worldZ);
                    float depthBlend = Mathf.Clamp01((finalY - groundY) / 1.5f);
                    waterColsBuffer[idx] = Color.Lerp(riverColor, deepWater, depthBlend * 0.5f);
                }
                else if (sea > groundY + 0.05f)
                {
                    // deep ocean colors
                    waterVertsBuffer[idx] = new Vector3(x * sp, sea, worldZ);
                    float depthFactor = Mathf.Clamp01((sea - groundY) / (hSp * 0.15f));
                    waterColsBuffer[idx] = Color.Lerp(shallowWater, deepWater, depthFactor);
                }
                else
                {
                    // land colors
                    waterVertsBuffer[idx] = new Vector3(x * sp, groundY - 0.5f, worldZ);
                    waterColsBuffer[idx] = new Color(0f, 0f, 0f, 0f);
                }
            }
        });

        waterMesh.vertices = waterVertsBuffer;
        if (waterMesh.triangles == null || waterMesh.triangles.Length != triangles.Length)
        {
            waterMesh.triangles = triangles;
        }
        waterMesh.colors = waterColsBuffer;
        waterMesh.RecalculateNormals();
        waterMesh.RecalculateBounds();
    }

    public void UpdateTerrainColors(float[] heightMap, Vector3[] normals)
    {
        // flat elevation
        Color sandBasinColor = new Color(0.76f, 0.70f, 0.50f);   // low riverbed/shore sand
        Color valleyGreenColor = new Color(0.26f, 0.48f, 0.22f); // lush valley & foothills green
        Color mountainRockColor = new Color(0.52f, 0.50f, 0.46f);// grey mountain rock
        Color alpineSnowColor = new Color(0.94f, 0.95f, 0.97f);  // high peak snow
        Color neutralFlatColor = new Color(0.75f, 0.75f, 0.75f); // neutral gray

        // slope colors
        Color talusSlopeColor = new Color(0.60f, 0.45f, 0.31f);  // terracotta talus
        Color steepCliffColor = new Color(0.27f, 0.27f, 0.29f);  // dark slate cliff
        Color streamDebugColor = new Color(0.15f, 0.65f, 0.95f); // cyan river 

        float minH = float.MaxValue;
        float maxH = float.MinValue;
        for (int i = 0; i < heightMap.Length; i++)
        {
            if (heightMap[i] < minH) minH = heightMap[i];
            if (heightMap[i] > maxH) maxH = heightMap[i];
        }
        if (maxH - minH < 0.01f) maxH = minH + 1f;
        float invRange = 1f / (maxH - minH);

        int len = vertices.Length;
        bool useSlope = useSlopeDiagnosticColors;
        bool useElev = useElevationDevColors;
        bool showDebug = showWaterDischargeDebug;
        float fluxThresh = riverFluxThreshold;

        for (int i = 0; i < len; i++)
        {
            float normalizedHeight = (heightMap[i] - minH) * invRange;

            if (!useSlope && !useElev)
            {
                if (biomeGenerator != null &&
                    biomeGenerator.useBiomeColors &&
                    biomeGenerator.biomeColors != null &&
                    i < biomeGenerator.biomeColors.Length)
                {
                    if (normalizedHeight < 0.35f)
                    {
                        // Keep biome colours for lower terrain.
                        colors[i] = biomeGenerator.biomeColors[i];
                        if (colors[i] == Color.magenta)
                        {
                            // If biome colour not set, refer back to elevation gradient colours
                            colors[i] = elevationGradient.Evaluate(normalizedHeight);
                        }
                    }
                    else
                    {
                        // Preserve the original gradient for mountains and snow
                        colors[i] = elevationGradient.Evaluate(normalizedHeight);
                    }
                }
                else
                {
                    // Original terrain gradient when biome colours are disabled
                    colors[i] = elevationGradient.Evaluate(normalizedHeight);
                }
            }
            else
            {
                // 1. find base elevation color (Sand < 0.08, Green 0.08-0.48, Grey Rock 0.48-0.80, Snow > 0.80)
                Color baseHeightColor;
                if (useElev)
                {
                    if (normalizedHeight < 0.08f)
                    {
                        baseHeightColor = Color.Lerp(sandBasinColor, valleyGreenColor, normalizedHeight / 0.08f);
                    }
                    else if (normalizedHeight < 0.48f)
                    {
                        baseHeightColor = Color.Lerp(valleyGreenColor, mountainRockColor, (normalizedHeight - 0.08f) / 0.40f);
                    }
                    else if (normalizedHeight < 0.80f)
                    {
                        baseHeightColor = Color.Lerp(mountainRockColor, alpineSnowColor, (normalizedHeight - 0.48f) / 0.32f);
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
                    colors[i] = baseHeightColor;
                }
                else
                {
                    float ny = Mathf.Clamp(normals[i].y, -1f, 1f);
                    if (ny > 0.939f) // < 20 degrees
                    {
                        colors[i] = baseHeightColor;
                    }
                    else if (ny > 0.809f) // 20 to 36 degrees
                    {
                        float t = (0.939f - ny) / 0.130f;
                        colors[i] = Color.Lerp(baseHeightColor, talusSlopeColor, t * 0.85f);
                    }
                    else // > 36 degrees
                    {
                        float t = Mathf.Clamp01((0.809f - ny) / 0.235f);
                        colors[i] = Color.Lerp(talusSlopeColor, steepCliffColor, t);
                    }
                }
            }

            // debug overlay for hydraulic erosion
            if (showDebug && waterDischargeMap != null && i < waterDischargeMap.Length)
            {
                float discharge = waterDischargeMap[i];
                if (discharge > fluxThresh)
                {
                    float streamBlend = Mathf.Clamp01((discharge - fluxThresh) / (fluxThresh * 2f));
                    colors[i] = Color.Lerp(colors[i], streamDebugColor, streamBlend * 0.85f);
                }
            }
        }
        terrainMesh.colors = colors;
    }

    private void AllocateMeshArrays()
    {
        int vertCount = gridWidth * gridLength;
        vertices = new Vector3[vertCount];
        uvs = new Vector2[vertCount];
        colors = new Color[vertCount];
        currentHeightMap = new float[vertCount];
        normalizedMapBuffer = new float[vertCount];
        waterVertsBuffer = new Vector3[vertCount];
        waterColsBuffer = new Color[vertCount];

        waterDischargeMap = new float[vertCount];
        flowDirectionMap = new Vector2[vertCount];
        waterSurfaceMap = new float[vertCount];

        downstreamParent = new int[vertCount];
        topologicalOrder = new int[vertCount];
        nodeVisited = new bool[vertCount];
        bucketHeads = new int[PRIORITY_BUCKETS];
        bucketNextNode = new int[vertCount];

        triangles = new int[(gridWidth - 1) * (gridLength - 1) * 6];

        int triIndex = 0;
        for (int z = 0; z < gridLength; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                int vertIndex = z * gridWidth + x;
                // offset UVs by half a texel so vertices align with pixel centers
                uvs[vertIndex] = new Vector2((x + 0.5f) / gridWidth, (z + 0.5f) / gridLength);

                if (x < gridWidth - 1 && z < gridLength - 1)
                {
                    triangles[triIndex + 0] = vertIndex;
                    triangles[triIndex + 1] = vertIndex + gridWidth;
                    triangles[triIndex + 2] = vertIndex + 1;

                    triangles[triIndex + 3] = vertIndex + 1;
                    triangles[triIndex + 4] = vertIndex + gridWidth;
                    triangles[triIndex + 5] = vertIndex + gridWidth + 1;

                    triIndex += 6;
                }
            }
        }

        if (waterMesh != null)
        {
            waterMesh.Clear();
            waterMesh.vertices = waterVertsBuffer;
            waterMesh.triangles = triangles;
        }
    }

    // generates a full-range normalized [0, 1] heightmap 
    public float[] GenerateNormalizedHeightMap()
    {
        int vertCount = gridWidth * gridLength;
        if (normalizedMapBuffer == null || normalizedMapBuffer.Length != vertCount)
        {
            normalizedMapBuffer = new float[vertCount];
        }
        float[] heightMap = normalizedMapBuffer;

        System.Random prng = new System.Random(seed);
        Vector2[] octaveOffsets = new Vector2[octaves];

        for (int i = 0; i < octaves; i++)
        {
            float offsetX = prng.Next(-100000, 100000);
            float offsetZ = prng.Next(-100000, 100000);
            octaveOffsets[i] = new Vector2(offsetX, offsetZ);
        }

        // Low-frequency offsets to make mountain ranges
        Vector2 macroOffset1 = new Vector2(prng.Next(-50000, 50000), prng.Next(-50000, 50000));
        Vector2 macroOffset2 = new Vector2(prng.Next(-50000, 50000), prng.Next(-50000, 50000));

        if (noiseScale <= 0f) noiseScale = 0.0001f;

        int w = gridWidth;
        int l = gridLength;
        float halfWidth = w / 2f;
        float halfLength = l / 2f;
        float scale = noiseScale;
        int oct = octaves;
        float aMult = ampMult;
        float fMult = freqMult;
        float rSharp = ridgeSharpness;
        float macroContrast = macroRangeContrast;

        // multi-threaded noise generation 
        Parallel.For(0, l, z =>
        {
            int row = z * w;
            for (int x = 0; x < w; x++)
            {
                float amplitude = 1f;
                float frequency = 1f;
                float noiseHeight = 0f;

                float relX = (x - halfWidth) / scale;
                float relZ = (z - halfLength) / scale;

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

                // creates mountain plateaus and low basins
                float macro1 = Mathf.PerlinNoise(relX * 0.35f + macroOffset1.x, relZ * 0.35f + macroOffset1.y);
                float macro2 = Mathf.PerlinNoise(relX * 0.65f + macroOffset2.x, relZ * 0.65f + macroOffset2.y);
                float macroMask = Mathf.Pow(Mathf.Clamp01(macro1 * 0.7f + macro2 * 0.3f), 2.1f);

                heightMap[row + x] = noiseHeight * Mathf.Lerp(1f, 0.08f + 1.92f * macroMask, macroContrast);
            }
        });

        float minNoise = float.MaxValue;
        float maxNoise = float.MinValue;
        for (int i = 0; i < vertCount; i++)
        {
            float v = heightMap[i];
            if (v < minNoise) minNoise = v;
            if (v > maxNoise) maxNoise = v;
        }

        // normalize to [0, 1]
        float range = Mathf.Max(0.0001f, maxNoise - minNoise);
        float invRange = 1f / range;
        float mPower = mountainPower;

        for (int i = 0; i < vertCount; i++)
        {
            float norm = (heightMap[i] - minNoise) * invRange;
            heightMap[i] = Mathf.Pow(norm, mPower);
        }

        return heightMap;
    }

    private void InitializeDefaultGradient()
    {
        if (elevationGradient != null && elevationGradient.colorKeys.Length > 2) return;

        elevationGradient = new Gradient();
        elevationGradient.colorKeys = new GradientColorKey[]
        {
            new GradientColorKey(new Color(0.76f, 0.70f, 0.50f), 0.0f), // low sand
            new GradientColorKey(new Color(0.28f, 0.52f, 0.25f), 0.15f),// valley green
            new GradientColorKey(new Color(0.52f, 0.50f, 0.48f), 0.55f),// grey mountain rock
            new GradientColorKey(new Color(0.35f, 0.34f, 0.36f), 0.78f),// alpine slate
            new GradientColorKey(Color.white, 0.88f)                    // snow peak
        };
    }
}