using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DotGen : MonoBehaviour
{
    [Header("Terrain Mesh Settings")]
    public int gridWidth = 200;
    public int gridLength = 200;
    public float spacing = 0.5f;
    public float heightSpacing = 15f; 

    [Header("Noise Settings")]
    public float noiseScale = 25f; 
    public float ampMult = 0.5f;
    public int octaves = 4;
    public float freqMult = 2f;
    public int seed = 0;

    [Header("Water & Biome Preview")]
    public Gradient terrainGradient;

    private Mesh terrainMesh;
    private Vector3[] vertices;
    private int[] triangles;
    private Vector2[] uvs;
    private Color[] colors;

    private int cachedWidth;
    private int cachedLength;

    void Awake()
    {
        terrainMesh = new Mesh();
        terrainMesh.name = "Procedural Terrain Mesh";
        // Uint32 allows for higher definition meshes
        terrainMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        GetComponent<MeshFilter>().mesh = terrainMesh;

        InitializeDefaultGradient();
    }

    void Start()
    {
        // clean up leftover children
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }

        GenerateDotMap();
    }

    public void GenerateDotMap()
    {
        if (terrainMesh == null) return;

        // only rewrite if dimensions or params change
        bool dimensionsChanged = (vertices == null || cachedWidth != gridWidth || cachedLength != gridLength);
        if (dimensionsChanged)
        {
            cachedWidth = gridWidth;
            cachedLength = gridLength;
            AllocateMeshArrays();
        }

        // generate the height map
        float[] heightMap = GenerateHeightMap();

        // Update vertex positions and biome colors
        for (int z = 0; z < gridLength; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                int index = z * gridWidth + x;
                float y = heightMap[index];

                vertices[index] = new Vector3(x * spacing, y, z * spacing);

                // Normalize height to [0, 1] for biome color
                float normalizedHeight = Mathf.InverseLerp(-heightSpacing, heightSpacing, y);
                colors[index] = terrainGradient.Evaluate(normalizedHeight);
            }
        }

        // Push data to the GPU
        if (dimensionsChanged)
        {
            terrainMesh.Clear();
            terrainMesh.vertices = vertices;
            terrainMesh.triangles = triangles;
            terrainMesh.uv = uvs;
        }
        else
        {
            // only update vertices for slider changes
            terrainMesh.vertices = vertices;
        }

        terrainMesh.colors = colors;
        terrainMesh.RecalculateNormals(); // for directional light shadows
        terrainMesh.RecalculateBounds();
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
                uvs[vertIndex] = new Vector2(x / (float)(gridWidth - 1), z / (float)(gridLength - 1));

                // Build 2 triangles for each quad (skipping the last row/column)
                if (x < gridWidth - 1 && z < gridLength - 1)
                {
                    // Triangle 1 (Bottom-Left, Top-Left, Bottom-Right)
                    triangles[triIndex + 0] = vertIndex;
                    triangles[triIndex + 1] = vertIndex + gridWidth;
                    triangles[triIndex + 2] = vertIndex + 1;

                    // Triangle 2 (Bottom-Right, Top-Left, Top-Right)
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

    // set up a default color for the tile 
    private void InitializeDefaultGradient()
    {
        if (terrainGradient != null && terrainGradient.colorKeys.Length > 2) return;

        terrainGradient = new Gradient();
        terrainGradient.colorKeys = new GradientColorKey[]
        {
            new GradientColorKey(new Color(0.1f, 0.3f, 0.6f), 0.0f), // Deep Water
            new GradientColorKey(new Color(0.85f, 0.8f, 0.55f), 0.35f), // Sand
            new GradientColorKey(new Color(0.25f, 0.55f, 0.2f), 0.5f), // Grass
            new GradientColorKey(new Color(0.45f, 0.4f, 0.35f), 0.75f), // Rock
            new GradientColorKey(Color.white, 0.95f) // Snow
        };
    }
}