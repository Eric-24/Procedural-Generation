using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DotGen : MonoBehaviour
{
    [Header("Terrain Mesh Settings")]
    public int gridWidth = 200;
    public int gridLength = 200;
    public float spacing = 2.4f;
    public float heightSpacing = 45f; 

    [Header("Noise & Mountain Settings")]
    public float noiseScale = 55f; 
    public float ampMult = 0.5f;
    public int octaves = 5;
    public float freqMult = 2f;
    [Range(1f, 3f)] public float mountainPower = 1.6f; // pushes valleys flat and peaks tall
    [Range(0f, 1f)] public float ridgeSharpness = 0.35f; // blends crisp mountain ridges
    public int seed = 0;

    [Header("Hydraulic Erosion Settings")]
    public bool enableHydraulicErosion = true;
    [Range(0, 100000)] public int erosionIterations = 30000;
    [Range(2, 8)] public int erosionRadius = 3;
    [Range(0.01f, 0.99f)] public float erosionInertia = 0.1f;
    public float sedimentCapacityFactor = 4f;
    public float minSedimentCapacity = 0.01f;
    [Range(0.01f, 1f)] public float erodeSpeed = 0.25f;
    [Range(0.01f, 1f)] public float depositSpeed = 0.25f;
    [Range(0.001f, 0.2f)] public float evaporateSpeed = 0.02f;
    public float gravity = 4f;
    public int maxDropletLifetime = 35;

    [Header("Thermal Erosion Cleanup (Anti-Spike)")]
    public bool enableThermalErosion = true;
    [Range(1, 15)] public int thermalIterations = 4;
    [Range(0.005f, 0.08f)] public float talusAngleThreshold = 0.022f; // Normalized height
    [Range(0.05f, 0.5f)] public float thermalRelaxation = 0.25f;

    [Header("Translucent Volumed Water (Oceans, Lakes & Streams)")]
    public bool enableVolumedWater = true;
    [Range(-20f, 20f)] public float oceanSeaLevel = -8f;
    [Range(0f, 1f)] public float waterOpacity = 0.58f;
    public float streamDischargeThreshold = 25f;

    [Header("Dev Visualization Settings")]
    public bool useSlopeDiagnosticColors = true;
    public bool useElevationDevColors = true;
    public bool showWaterDischargeDebug = false; // Off by default 
    public Gradient elevationGradient;

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

    // arrays to store water data during erosion
    [HideInInspector] public float[] waterDischargeMap;
    [HideInInspector] public Vector2[] flowDirectionMap;
    [HideInInspector] public float[] waterSurfaceMap; // stores lake/ocean/stream surface heights

    // erosion brush settings
    private int[][] brushIndexOffsets;
    private float[][] brushWeights;
    private int cachedBrushRadius = -1;
    private int cachedBrushWidth = -1;
    private int cachedBrushLength = -1;

    private int cachedWidth;
    private int cachedLength;

    void Awake()
    {
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
        // Destroy old dot children, but keep our WaterVolumeMesh child!
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (child != waterMeshObj)
            {
                Destroy(child);
            }
        }

        // Upgrade legacy low Inspector values automatically on start if needed
        if (heightSpacing <= 15f) heightSpacing = 45f;
        if (noiseScale <= 25f) noiseScale = 55f;
        if (erosionRadius < 2) erosionRadius = 3;

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

        // 1. Generate normalized heightmap in [0, 1] with defined mountains & valleys
        float[] normalizedMap = GenerateNormalizedHeightMap();

        // 2. Run Hydraulic Erosion on normalized [0, 1] heights so parameters scale properly!
        if (enableHydraulicErosion && erosionIterations > 0)
        {
            SimulateHydraulicErosion(normalizedMap);
        }
        else
        {
            System.Array.Clear(waterDischargeMap, 0, waterDischargeMap.Length);
            System.Array.Clear(flowDirectionMap, 0, flowDirectionMap.Length);
        }

        // 3. Run cheap Thermal Erosion pass to remove any 1-vertex needle spikes
        if (enableThermalErosion && thermalIterations > 0)
        {
            SimulateThermalErosion(normalizedMap);
        }

        // 4. Scale normalized [0, 1] heights into world space [-heightSpacing*0.35, heightSpacing*0.65]
        for (int z = 0; z < gridLength; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                int index = z * gridWidth + x;
                float worldY = (normalizedMap[index] - 0.35f) * heightSpacing;
                currentHeightMap[index] = worldY;
                vertices[index] = new Vector3(x * spacing, worldY, z * spacing);
            }
        }

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

        // 5. Color terrain by slope and elevation (ground remains visible underwater!)
        UpdateTerrainColors(currentHeightMap, terrainMesh.normals);

        // 6. Build translucent Volumed Water mesh for Oceans, Lakes/Lagoons, and Streams
        UpdateVolumedWaterMesh(currentHeightMap);
    }

    private void SimulateHydraulicErosion(float[] heightMap)
    {
        // Enforce minimum radius of 2 so erosion never digs 1-vertex needle holes
        if (erosionRadius < 2) erosionRadius = 2;
        InitializeErosionBrush();

        System.Array.Clear(waterDischargeMap, 0, waterDischargeMap.Length);
        System.Array.Clear(flowDirectionMap, 0, flowDirectionMap.Length);

        // Deterministic offset from seed so droplet paths are reproducible
        System.Random prng = new System.Random(seed + 1337);

        for (int iteration = 0; iteration < erosionIterations; iteration++)
        {
            // Spawn droplet at random position inside the grid
            float posX = (float)(prng.NextDouble() * (gridWidth - 3) + 1.0);
            float posZ = (float)(prng.NextDouble() * (gridLength - 3) + 1.0);
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

                // Sub-cell offsets inside the quad [0, 1)
                float cellOffsetX = posX - nodeX;
                float cellOffsetZ = posZ - nodeZ;

                // 1. Calculate droplet's current height and surface gradient
                SampleHeightAndGradient(heightMap, posX, posZ, out float height, out float gradX, out float gradZ);

                // 2. Update direction with inertia and normalize
                dirX = dirX * erosionInertia - gradX * (1f - erosionInertia);
                dirZ = dirZ * erosionInertia - gradZ * (1f - erosionInertia);

                float len = Mathf.Sqrt(dirX * dirX + dirZ * dirZ);
                if (len != 0f)
                {
                    dirX /= len;
                    dirZ /= len;
                }

                // Only record water discharge once droplet has gathered momentum in a channel (lifetime > 8)
                if (lifetime > 8)
                {
                    waterDischargeMap[dropletIndex] += water * speed;
                    flowDirectionMap[dropletIndex] += new Vector2(dirX, dirZ) * water;
                }

                posX += dirX;
                posZ += dirZ;

                // Stop once droplet stopped moving or flowed off the edge of the map
                if ((dirX == 0f && dirZ == 0f) || posX < 1f || posX >= gridWidth - 2 || posZ < 1f || posZ >= gridLength - 2)
                {
                    break;
                }

                SampleHeightAndGradient(heightMap, posX, posZ, out float newHeight, out _, out _);
                float deltaHeight = newHeight - height;

                // Calculate sediment capacity on normalized [0, 1] heightmap
                float sedimentCapacity = Mathf.Max(-deltaHeight * speed * water * sedimentCapacityFactor, minSedimentCapacity);

                if (sediment > sedimentCapacity || deltaHeight > 0f)
                {
                    float amountToDeposit = (deltaHeight > 0f)
                        ? Mathf.Min(deltaHeight, sediment)
                        : (sediment - sedimentCapacity) * depositSpeed;

                    sediment -= amountToDeposit;

                    // Bilinearly deposit sediment 
                    heightMap[dropletIndex] += amountToDeposit * (1f - cellOffsetX) * (1f - cellOffsetZ);
                    heightMap[dropletIndex + 1] += amountToDeposit * cellOffsetX * (1f - cellOffsetZ);
                    heightMap[dropletIndex + gridWidth] += amountToDeposit * (1f - cellOffsetX) * cellOffsetZ;
                    heightMap[dropletIndex + gridWidth + 1] += amountToDeposit * cellOffsetX * cellOffsetZ;
                }
                else
                {
                    // Moving downhill with spare capacity
                    float amountToErode = Mathf.Min((sedimentCapacity - sediment) * erodeSpeed, -deltaHeight);

                    int[] brushIndices = brushIndexOffsets[dropletIndex];
                    float[] weights = brushWeights[dropletIndex];

                    for (int i = 0; i < brushIndices.Length; i++)
                    {
                        int weightedIndex = brushIndices[i];
                        float weighedErodeAmount = amountToErode * weights[i];
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

    // thermal erosion passes
    private void SimulateThermalErosion(float[] heightMap)
    {
        int[] dx = { -1, 1, 0, 0, -1, 1, -1, 1 };
        int[] dz = { 0, 0, -1, 1, -1, -1, 1, 1 };
        float[] distMult = { 1f, 1f, 1f, 1f, 1.414f, 1.414f, 1.414f, 1.414f };

        for (int iter = 0; iter < thermalIterations; iter++)
        {
            for (int z = 1; z < gridLength - 1; z++)
            {
                for (int x = 1; x < gridWidth - 1; x++)
                {
                    int idx = z * gridWidth + x;
                    float h = heightMap[idx];

                    float maxDiff = 0f;
                    int steepestNeighbor = -1;

                    for (int n = 0; n < 8; n++)
                    {
                        int nIdx = (z + dz[n]) * gridWidth + (x + dx[n]);
                        float diff = (h - heightMap[nIdx]) / distMult[n];
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
        if (brushIndexOffsets != null && cachedBrushRadius == erosionRadius && cachedBrushWidth == gridWidth && cachedBrushLength == gridLength)
        {
            return;
        }

        cachedBrushRadius = erosionRadius;
        cachedBrushWidth = gridWidth;
        cachedBrushLength = gridLength;

        int vertCount = gridWidth * gridLength;
        brushIndexOffsets = new int[vertCount][];
        brushWeights = new float[vertCount][];

        int[] xOffsets = new int[erosionRadius * erosionRadius * 4];
        int[] zOffsets = new int[erosionRadius * erosionRadius * 4];
        float[] weights = new float[erosionRadius * erosionRadius * 4];

        for (int i = 0; i < vertCount; i++)
        {
            int centreX = i % gridWidth;
            int centreZ = i / gridWidth;

            float weightSum = 0f;
            int addIndex = 0;

            for (int z = -erosionRadius; z <= erosionRadius; z++)
            {
                for (int x = -erosionRadius; x <= erosionRadius; x++)
                {
                    float sqrDst = x * x + z * z;
                    if (sqrDst < erosionRadius * erosionRadius)
                    {
                        int coordX = centreX + x;
                        int coordZ = centreZ + z;

                        if (coordX >= 0 && coordX < gridWidth && coordZ >= 0 && coordZ < gridLength)
                        {
                            float weight = 1f - Mathf.Sqrt(sqrDst) / erosionRadius;
                            weightSum += weight;
                            weights[addIndex] = weight;
                            xOffsets[addIndex] = coordX;
                            zOffsets[addIndex] = coordZ;
                            addIndex++;
                        }
                    }
                }
            }

            brushIndexOffsets[i] = new int[addIndex];
            brushWeights[i] = new float[addIndex];

            for (int j = 0; j < addIndex; j++)
            {
                brushIndexOffsets[i][j] = zOffsets[j] * gridWidth + xOffsets[j];
                brushWeights[i][j] = weights[j] / weightSum;
            }
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

    // 3D surface water mesh
    private void UpdateVolumedWaterMesh(float[] heightMap)
    {
        if (waterMeshObj == null) SetupWaterMeshObject();

        waterMeshObj.SetActive(enableVolumedWater);
        if (!enableVolumedWater) return;

        int vertCount = gridWidth * gridLength;
        Vector3[] waterVerts = new Vector3[vertCount];
        Color[] waterCols = new Color[vertCount];

        // depression filling pass to find lakes and lagoons
        for (int z = 0; z < gridLength; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                int idx = z * gridWidth + x;
                bool isBorder = (x == 0 || x == gridWidth - 1 || z == 0 || z == gridLength - 1);
                waterSurfaceMap[idx] = isBorder ? Mathf.Max(heightMap[idx], oceanSeaLevel) : 10000f;
            }
        }

        // iterative basin flooding for basin filling
        for (int pass = 0; pass < 4; pass++)
        {
            for (int z = 1; z < gridLength - 1; z++)
            {
                for (int x = 1; x < gridWidth - 1; x++)
                {
                    int idx = z * gridWidth + x;
                    float ground = Mathf.Max(heightMap[idx], oceanSeaLevel);
                    float minNeighbor = Mathf.Min(
                        Mathf.Min(waterSurfaceMap[idx - 1], waterSurfaceMap[idx + 1]),
                        Mathf.Min(waterSurfaceMap[idx - gridWidth], waterSurfaceMap[idx + gridWidth])
                    );
                    waterSurfaceMap[idx] = Mathf.Max(ground, Mathf.Min(waterSurfaceMap[idx], minNeighbor));
                }
            }
            for (int z = gridLength - 2; z >= 1; z--)
            {
                for (int x = gridWidth - 2; x >= 1; x--)
                {
                    int idx = z * gridWidth + x;
                    float ground = Mathf.Max(heightMap[idx], oceanSeaLevel);
                    float minNeighbor = Mathf.Min(
                        Mathf.Min(waterSurfaceMap[idx - 1], waterSurfaceMap[idx + 1]),
                        Mathf.Min(waterSurfaceMap[idx - gridWidth], waterSurfaceMap[idx + gridWidth])
                    );
                    waterSurfaceMap[idx] = Mathf.Max(ground, Mathf.Min(waterSurfaceMap[idx], minNeighbor));
                }
            }
        }

        // set transparency for water
        Color shallowWater = new Color(0.18f, 0.68f, 0.88f, waterOpacity * 0.65f);
        Color deepWater = new Color(0.05f, 0.28f, 0.58f, Mathf.Min(0.92f, waterOpacity * 1.25f));

        for (int z = 0; z < gridLength; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                int idx = z * gridWidth + x;
                float groundY = heightMap[idx];
                float surfaceY = waterSurfaceMap[idx];

                float depth = surfaceY - groundY;
                float streamFlux = (waterDischargeMap != null) ? waterDischargeMap[idx] : 0f;

                if (depth > 0.05f)
                {
                    // ocean, lakes, lagoon
                    waterVerts[idx] = new Vector3(x * spacing, surfaceY, z * spacing);
                    float depthFactor = Mathf.Clamp01(depth / (heightSpacing * 0.25f));
                    waterCols[idx] = Color.Lerp(shallowWater, deepWater, depthFactor);
                }
                else if (streamFlux > streamDischargeThreshold)
                {
                    // streams, rivers, flowing channels
                    waterVerts[idx] = new Vector3(x * spacing, groundY + 0.15f, z * spacing);
                    float fluxAlpha = Mathf.Clamp01((streamFlux - streamDischargeThreshold) / (streamDischargeThreshold * 2f));
                    waterCols[idx] = new Color(shallowWater.r, shallowWater.g, shallowWater.b, waterOpacity * 0.7f * fluxAlpha);
                }
                else
                {
                    // dry land
                    waterVerts[idx] = new Vector3(x * spacing, groundY - 0.2f, z * spacing);
                    waterCols[idx] = new Color(0f, 0f, 0f, 0f);
                }
            }
        }

        waterMesh.Clear();
        waterMesh.vertices = waterVerts;
        waterMesh.triangles = triangles;
        waterMesh.colors = waterCols;
        waterMesh.RecalculateNormals();
        waterMesh.RecalculateBounds();
    }

    public void UpdateTerrainColors(float[] heightMap, Vector3[] normals)
    {
        // flat colors
        Color lowBasinColor = new Color(0.76f, 0.70f, 0.50f);   // underwater 
        Color midPlainsColor = new Color(0.32f, 0.55f, 0.28f);  // green
        Color highPlateauColor = new Color(0.82f, 0.80f, 0.75f);// alpine stone
        Color neutralFlatColor = new Color(0.75f, 0.75f, 0.75f);// gray

        // slope colors
        Color talusSlopeColor = new Color(0.68f, 0.44f, 0.26f); // terracotta talus
        Color steepCliffColor = new Color(0.24f, 0.25f, 0.28f); // dark slate cliff
        Color streamDebugColor = new Color(0.15f, 0.65f, 0.95f);// cyan river 

        float minH = -0.35f * heightSpacing;
        float maxH = 0.65f * heightSpacing;

        for (int i = 0; i < vertices.Length; i++)
        {
            float normalizedHeight = Mathf.InverseLerp(minH, maxH, heightMap[i]);

            if (!useSlopeDiagnosticColors && !useElevationDevColors)
            {
                colors[i] = elevationGradient.Evaluate(normalizedHeight);
            }
            else
            {
                // determine base flat color
                Color baseHeightColor;
                if (useElevationDevColors)
                {
                    baseHeightColor = normalizedHeight < 0.4f
                        ? Color.Lerp(lowBasinColor, midPlainsColor, normalizedHeight / 0.4f)
                        : Color.Lerp(midPlainsColor, highPlateauColor, (normalizedHeight - 0.4f) / 0.6f);
                }
                else
                {
                    baseHeightColor = neutralFlatColor;
                }

                // overlay Slope Diagnostic colors if enabled
                if (!useSlopeDiagnosticColors)
                {
                    colors[i] = baseHeightColor;
                }
                else
                {
                    // calculate slope angle in degrees. 0 is flat
                    float slopeDegrees = Mathf.Acos(Mathf.Clamp(normals[i].y, -1f, 1f)) * Mathf.Rad2Deg;

                    if (slopeDegrees < 18f)
                    {
                        colors[i] = baseHeightColor;
                    }
                    else if (slopeDegrees < 35f)
                    {
                        // blend flat color with slope color
                        float t = Mathf.InverseLerp(18f, 35f, slopeDegrees);
                        colors[i] = Color.Lerp(baseHeightColor, talusSlopeColor, t);
                    }
                    else
                    {
                        // blend slope color with steep slope color
                        float t = Mathf.InverseLerp(35f, 55f, slopeDegrees);
                        colors[i] = Color.Lerp(talusSlopeColor, steepCliffColor, t);
                    }
                }
            }

            // debug overlay for hydraulic erosion
            if (showWaterDischargeDebug && waterDischargeMap != null && i < waterDischargeMap.Length)
            {
                float discharge = waterDischargeMap[i];
                if (discharge > streamDischargeThreshold)
                {
                    float streamBlend = Mathf.Clamp01((discharge - streamDischargeThreshold) / (streamDischargeThreshold * 2f));
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
        waterDischargeMap = new float[vertCount];
        flowDirectionMap = new Vector2[vertCount];
        waterSurfaceMap = new float[vertCount];
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
    }

    // Generates a normalized [0, 1] heightmap with sharp ridges and exponential mountain peaks
    public float[] GenerateNormalizedHeightMap()
    {
        float[] heightMap = new float[gridWidth * gridLength];
        System.Random prng = new System.Random(seed);
        Vector2[] octaveOffsets = new Vector2[octaves];

        float maxAmp = 0f;
        float amp = 1f;

        for (int i = 0; i < octaves; i++)
        {
            float offsetX = prng.Next(-100000, 100000);
            float offsetZ = prng.Next(-100000, 100000);
            octaveOffsets[i] = new Vector2(offsetX, offsetZ);

            maxAmp += amp;
            amp *= ampMult;
        }

        if (noiseScale <= 0f) noiseScale = 0.0001f;

        float halfWidth = gridWidth / 2f;
        float halfLength = gridLength / 2f;

        for (int z = 0; z < gridLength; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                float amplitude = 1f;
                float frequency = 1f;
                float noiseHeight = 0f;

                for (int i = 0; i < octaves; i++)
                {
                    float sampleX = (x - halfWidth) / noiseScale * frequency + octaveOffsets[i].x;
                    float sampleZ = (z - halfLength) / noiseScale * frequency + octaveOffsets[i].y;

                    float rawPerlin = Mathf.PerlinNoise(sampleX, sampleZ); // [0, 1]

                    // Ridged noise (1 - |2*p - 1|) creates sharp mountain crests
                    float ridgeVal = 1f - Mathf.Abs(rawPerlin * 2f - 1f);
                    ridgeVal *= ridgeVal; // Square for sharper ridges

                    // Blend standard billowy Perlin with sharp Ridged noise
                    float combined = Mathf.Lerp(rawPerlin, ridgeVal, ridgeSharpness);

                    noiseHeight += combined * amplitude;

                    amplitude *= ampMult;
                    frequency *= freqMult;
                }

                // Normalize to [0, 1] and apply redistribution power curve (flattens valleys, steepens peaks!)
                float norm = Mathf.Clamp01(noiseHeight / maxAmp);
                heightMap[z * gridWidth + x] = Mathf.Pow(norm, mountainPower);
            }
        }

        return heightMap;
    }

    private void InitializeDefaultGradient()
    {
        if (elevationGradient != null && elevationGradient.colorKeys.Length > 2) return;

        elevationGradient = new Gradient();
        elevationGradient.colorKeys = new GradientColorKey[]
        {
            new GradientColorKey(new Color(0.76f, 0.70f, 0.50f), 0.0f), // underwater sand
            new GradientColorKey(new Color(0.85f, 0.8f, 0.55f), 0.30f), // beach sand
            new GradientColorKey(new Color(0.25f, 0.55f, 0.2f), 0.5f),  // grass
            new GradientColorKey(new Color(0.45f, 0.4f, 0.35f), 0.75f), // rock
            new GradientColorKey(Color.white, 0.92f)                    // snow
        };
    }
}