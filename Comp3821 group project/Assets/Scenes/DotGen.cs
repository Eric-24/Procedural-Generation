using UnityEngine;

public class DotGen : MonoBehaviour
{
    [Header("Dot Settings")]
    public GameObject dotPrefab;
    public int gridWidth = 10;
    public int gridLength = 10;
    public float spacing = 50f;
    public float heightSpacing = 2f;
    public float ampMult = 0.5f;
    public int octaves = 4;
    public float freqMult = 2f;

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

        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridLength; z++)
            {
                float y = AddVariations(x, z) * heightSpacing;
                spawnedDots[x, z].position = new Vector3(x * spacing, y, z * spacing);
            }
        }
    }

    float AddVariations(int x, int z)
    {
        float totalHeight = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxAmp = 0f;

        for (int i = 0; i < octaves; i++)
        {
            float xNoise = x * frequency;
            float zNoise = z * frequency;
            float noiseVal = Mathf.PerlinNoise(xNoise * 0.1f, zNoise * 0.1f) - 1f;
            totalHeight += noiseVal * amplitude;

            maxAmp += amplitude;
            amplitude *= ampMult;
            frequency *= freqMult;
        }

        return totalHeight / maxAmp;
    }
}