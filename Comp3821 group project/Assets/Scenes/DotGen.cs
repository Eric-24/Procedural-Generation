using UnityEngine;

public class DotGen : MonoBehaviour
{
    [Header("Dot Settings")]
    public GameObject dotPrefab;
    public int gridWidth = 100;
    public int gridLength = 100;
    public float spacing = 0.5f;
    public float heightSpacing = 50f;

    [Header("Noise Settings")]
    public float noiseScale = 10f;
    public float ampMult = 0.5f;
    public int octaves = 4;
    public float freqMult = 2f;
    public int seed = 0;

    private Transform[,] spawnedDots;

    void Start()
    {
        GenerateDotMap();
    }

    public void GenerateDotMap()
    {
        // Only spawn the GameObjects once (or if grid dimensions change)
        if (spawnedDots == null || spawnedDots.GetLength(0) != gridWidth || spawnedDots.GetLength(1) != gridLength)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            spawnedDots = new Transform[gridWidth, gridLength];

            for (int x = 0; x < gridWidth; x++)
            {
                for (int z = 0; z < gridLength; z++)
                {
                    GameObject dot = Instantiate(dotPrefab, Vector3.zero, Quaternion.identity, this.transform);
                    spawnedDots[x, z] = dot.transform;
                }
            }
        }

        // Compute all heights in a 1D array
        float[] heightMap = GenerateHeightMap();

        // Apply heights to the spawned dots
        for (int z = 0; z < gridLength; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                float y = heightMap[z * gridWidth + x];
                spawnedDots[x, z].position = new Vector3(x * spacing, y, z * spacing);
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
}