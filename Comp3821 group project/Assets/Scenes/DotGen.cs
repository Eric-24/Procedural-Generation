using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DotGen : MonoBehaviour
{
    [Header("Terrain Mesh Settings")]
    public int gridWidth = 200;
    public int gridLength = 200;
    public float spacing = 2.4f;
    public float heightSpacing = 15f;

    [Header("Noise Settings")]
    public float noiseScale = 25f;
    public float ampMult = 0.5f;
    public int octaves = 4;
    public float freqMult = 2f;
    public int seed = 0;

    [Header("Hydraulic Erosion Settings")]
    public bool enableHydraulicErosion = true;
    [Range(0, 100000)] public int erosionIterations = 25000;
    [Range(1, 8)] public int erosionRadius = 3;
    [Range(0.01f, 0.99f)] public float erosionInertia = 0.05f;
    public float sedimentCapacityFactor = 4f;
    public float minSedimentCapacity = 0.01f;
    [Range(0.01f, 1f)] public float erodeSpeed = 0.3f;
    [Range(0.01f, 1f)] public float depositSpeed = 0.3f;
    [Range(0.001f, 0.2f)] public float evaporateSpeed = 0.02f;
    public float gravity = 4f;
    public int maxDropletLifetime = 30;

    [Header("Dev Visualization Settings")]
    public bool useSlopeDiagnosticColors = true;
    public bool useElevationDevColors = true;
    public bool showWaterDischargeDebug = true;
    public float streamDischargeThreshold = 15f;
    public Gradient elevationGradient;

    private Mesh terrainMesh;
    private MeshRenderer meshRenderer;

    private Vector3[] vertices;
    private int[] triangles;
    private Vector2[] uvs;
    private Color[] colors;
    private float[] currentHeightMap;

    // arrays to store water data during erosion
    [HideInInspector] public float[] waterDischargeMap;
    [HideInInspector] public Vector2[] flowDirectionMap;

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
    }

    void Start()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
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

        // generate the height map
        currentHeightMap = GenerateHeightMap();

        // run hydraulic erosion pass and record water discharge/flow
        if (enableHydraulicErosion && erosionIterations > 0)
        {
            SimulateHydraulicErosion(currentHeightMap);
        }
        else
        {
            System.Array.Clear(waterDischargeMap, 0, waterDischargeMap.Length);
            System.Array.Clear(flowDirectionMap, 0, flowDirectionMap.Length);
        }

        // update vertex Positions
        for (int z = 0; z < gridLength; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                int index = z * gridWidth + x;
                vertices[index] = new Vector3(x * spacing, currentHeightMap[index], z * spacing);
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

        // color terrain by slope and elevation
        UpdateTerrainColors(currentHeightMap, terrainMesh.normals);
    }

    private void SimulateHydraulicErosion(float[] heightMap)
    {
        InitializeErosionBrush();

        System.Array.Clear(waterDischargeMap, 0, waterDischargeMap.Length);
        System.Array.Clear(flowDirectionMap, 0, flowDirectionMap.Length);

        // Deterministic offset from seed so droplet paths are reproducible
        System.Random prng = new System.Random(seed + 1337);

        for (int iteration = 0; iteration < erosionIterations; iteration++)
        {
            // Spawn droplet at random position inside the grid
            float posX = (float)(prng.NextDouble() * (gridWidth - 2) + 0.5);
            float posZ = (float)(prng.NextDouble() * (gridLength - 2) + 0.5);
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

                // Record water discharge and flow vector 
                if (lifetime > 2)
                {
                    waterDischargeMap[dropletIndex] += water;
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

                // Calculate sediment capacity 
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

    public void UpdateTerrainColors(float[] heightMap, Vector3[] normals)
    {
        // flat colors
        Color lowBasinColor = new Color(0.22f, 0.42f, 0.45f);   // teal
        Color midPlainsColor = new Color(0.38f, 0.55f, 0.32f);  // green
        Color highPlateauColor = new Color(0.76f, 0.72f, 0.62f);// ochre
        Color neutralFlatColor = new Color(0.75f, 0.75f, 0.75f);// neutral grey when elevation dev color is off

        // slope colors
        Color talusSlopeColor = new Color(0.72f, 0.45f, 0.25f); // terracotta
        Color steepCliffColor = new Color(0.22f, 0.23f, 0.26f); // dark Slate 
        Color streamDebugColor = new Color(0.15f, 0.65f, 0.95f);// cyan river drainage preview

        for (int i = 0; i < vertices.Length; i++)
        {
            float normalizedHeight = Mathf.InverseLerp(-heightSpacing, heightSpacing, heightMap[i]);

            // If both dev toggles are off, use the biome elevationGradient
            if (!useSlopeDiagnosticColors && !useElevationDevColors)
            {
                colors[i] = elevationGradient.Evaluate(normalizedHeight);
            }
            else
            {
                // 1. Determine base flat color (either Dev Elevation or Neutral Grey)
                Color baseHeightColor;
                if (useElevationDevColors)
                {
                    baseHeightColor = normalizedHeight < 0.5f
                        ? Color.Lerp(lowBasinColor, midPlainsColor, normalizedHeight * 2f)
                        : Color.Lerp(midPlainsColor, highPlateauColor, (normalizedHeight - 0.5f) * 2f);
                }
                else
                {
                    baseHeightColor = neutralFlatColor;
                }

                // 2. Overlay Slope Diagnostic colors if enabled
                if (!useSlopeDiagnosticColors)
                {
                    colors[i] = baseHeightColor;
                }
                else
                {
                    // Calculate slope angle in degrees. 0 is flat
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

            // 3. Optional debug overlay for hydraulic erosion river channels
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
        waterDischargeMap = new float[vertCount];
        flowDirectionMap = new Vector2[vertCount];
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

    public float[] GenerateHeightMap()
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

                    float perlinValue = Mathf.PerlinNoise(sampleX, sampleZ) * 2f - 1f;
                    noiseHeight += perlinValue * amplitude;

                    amplitude *= ampMult;
                    frequency *= freqMult;
                }

                heightMap[z * gridWidth + x] = (noiseHeight / maxAmp) * heightSpacing;
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
            new GradientColorKey(new Color(0.1f, 0.3f, 0.6f), 0.0f), // water
            new GradientColorKey(new Color(0.85f, 0.8f, 0.55f), 0.35f), // sand
            new GradientColorKey(new Color(0.25f, 0.55f, 0.2f), 0.5f), // grass
            new GradientColorKey(new Color(0.45f, 0.4f, 0.35f), 0.75f), // rock
            new GradientColorKey(Color.white, 0.95f) // snow
        };
    }
}