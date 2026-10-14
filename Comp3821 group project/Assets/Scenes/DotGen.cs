using UnityEngine;

public class DotGen : MonoBehaviour {
    [Header("Dot Settings")]
    public GameObject dotPrefab;
    public int gridWidth = 10;
    public int gridLength = 10;
    public float spacing = 50f;
    public float heightSpacing = 2f;

    void Start() {
        GenerateDotMap();
    }

    void GenerateDotMap()
    {
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridLength; z++)
            {
                float noiseVal = Mathf.PerlinNoise(x * 0.1f, z * 0.1f);
                float y = noiseVal * heightSpacing;

                Vector3 pos = new Vector3(x * spacing, y, z * spacing);
                GameObject dot = Instantiate(dotPrefab, pos, Quaternion.identity);
                dot.transform.SetParent(this.transform);
            }
        }
    }
}