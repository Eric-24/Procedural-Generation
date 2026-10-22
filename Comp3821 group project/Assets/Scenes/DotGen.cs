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

    [Header("Dev Visualization Settings")]
    public bool useSlopeDiagnosticColors = true;
    public bool useElevationDevColors = true;
    public Gradient elevationGradient;

    private Mesh terrainMesh;
    private MeshRenderer meshRenderer;

    private Vector3[] vertices;
    private int[] triangles;
    private Vector2[] uvs;
    private Color[] colors;
    private float[] currentHeightMap;

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

    // Allows you to toggle color checkboxes in the Unity Inspector during Play Mode and see results immediately
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

        for (int i = 0; i < vertices.Length; i++)
        {
            float normalizedHeight = Mathf.InverseLerp(-heightSpacing, heightSpacing, heightMap[i]);

            // If both dev toggles are off, use the biome elevationGradient
            if (!useSlopeDiagnosticColors && !useElevationDevColors)
            {
                colors[i] = elevationGradient.Evaluate(normalizedHeight);
                continue;
            }

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
        terrainMesh.colors = colors;
    }

    private void AllocateMeshArrays()
    {
        int vertCount = gridWidth * gridLength;
        vertices = new Vector3[vertCount];
        uvs = new Vector2[vertCount];
        colors = new Color[vertCount];
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