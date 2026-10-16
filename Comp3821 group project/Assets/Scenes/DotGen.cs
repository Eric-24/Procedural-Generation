using UnityEngine;

public class DotGen : MonoBehaviour {
    [Header("Dot Settings")]
    public GameObject dotPrefab;
    public int gridWidth = 10;
    public int gridLength = 10;
    public float spacing = 50f;
    public float heightSpacing = 2f;
    // Amplitude multipler: scaling vertical intensity
    public float ampMult = 0.5f;
    // Octaves: Layers of noise maps 
    public int octaves = 4;
    // Frequency multiplier: scaling horizontal intensity
    public float freqMult = 2f; 
    // Low ampMult + High freqMult = taller mountains
    // High ampMult + Low freqMult = shorter mountains
    // Make ampMult, freqMult opposite otherwise too much noise

    void Start() {
        GenerateDotMap();
    }

    void GenerateDotMap()
    {
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridLength; z++)
            {
                // float noiseVal = Mathf.PerlinNoise(x * 0.1f, z * 0.1f);
                float y = AddVariations(x, z) * heightSpacing;

                Vector3 pos = new Vector3(x * spacing, y, z * spacing);
                GameObject dot = Instantiate(dotPrefab, pos, Quaternion.identity);
                dot.transform.SetParent(this.transform);
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