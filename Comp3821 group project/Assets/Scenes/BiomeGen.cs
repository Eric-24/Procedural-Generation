
using System;
using UnityEngine;

public class BiomeGen : MonoBehaviour
{
    public enum BiomeType
    {
        Taiga,
        TemperateForest,
        TropicalRainforest,
        Wetlands,
        Desert
    }

    [Header("Tree Prefabs")]
    public GameObject oakTreePrefab;
    public GameObject firTreePrefab;
    public GameObject poplarTreePrefab;
    public GameObject palmTreePrefab;

    [Tooltip("Optional parent object for all spawned trees.")]
    public Transform treeParent;

    [Header("Tree Spawning")]
    [Min(1f)]
    public float treeSpacing = 12f;

    [Range(0f, 1f)]
    public float treeSpawnChance = 0.8f;

    [Range(0f, 1f)]
    public float minimumSlopeNormalY = 0.65f;

    public bool randomizeTreeRotation = true;

    [Header("Natural Tree Placement")]
    [Range(0f, 1f)]
    public float positionJitter = 0.85f;

    [Range(0f, 1f)]
    public float mountainHeightLimit = 0.75f;

    [Range(0f, 1f)]
    public float mountainSlopeLimit = 0.80f;

    [Header("Temperature and Moisture Noise")]
    [Min(1f)] public float temperatureNoiseScale = 180f;
    [Min(1f)] public float moistureNoiseScale = 130f;

    [Range(0f, 1f)]
    public float temperatureVariation = 0.3f;

    [Range(0f, 1f)]
    public float moistureVariation = 0.4f;

    [Header("Climate Thresholds")]
    [Range(0f, 1f)]
    public float tropicalTemperature = 0.72f;

    [Range(0f, 1f)]
    public float taigaTemperature = 0.30f;

    [Range(0f, 1f)]
    public float desertTemperature = 0.72f;

    [Range(0f, 1f)]
    public float desertMoisture = 0.10f;

    [Range(0f, 1f)]
    public float wetlandMoisture = 0.78f;

    public BiomeType[] biomeMap { get; private set; }

    private const string TREE_PARENT_NAME = "Generated Trees";

    public void GenerateBiomes(
        float[] normalizedHeights,
        float[] worldHeights,
        float[] riverDischarge,
        Vector3[] normals,
        int width,
        int length,
        float spacing,
        float seaLevel,
        int seed)
    {
        int count = width * length;

        if (normalizedHeights == null ||
            worldHeights == null ||
            normalizedHeights.Length != count ||
            worldHeights.Length != count)
        {
            Debug.LogWarning(
                "BiomeGen: Height data is missing or has the wrong size.");
            return;
        }

        if (biomeMap == null || biomeMap.Length != count)
            biomeMap = new BiomeType[count];

        // Calculate the biome for every terrain point.
        for (int z = 0; z < length; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = z * width + x;

                // Set a default before checking whether this point is underwater.
                biomeMap[i] = BiomeType.TemperateForest;

                if (worldHeights[i] <= seaLevel)
                    continue;

                float elevation =
                    Mathf.Clamp01(normalizedHeights[i]);

                float latitude = Mathf.Abs(
                    2f * z / Mathf.Max(1, length - 1) - 1f);

                float tempNoise = Mathf.PerlinNoise(
                    (x + seed * 13f) /
                        Mathf.Max(1f, temperatureNoiseScale),
                    (z - seed * 7f) /
                        Mathf.Max(1f, temperatureNoiseScale));

                float temperature = Mathf.Clamp01(
                    1f - latitude * 0.75f
                    + (tempNoise - 0.5f) * temperatureVariation
                    - elevation * 0.35f);

                float moistureNoise = Mathf.PerlinNoise(
                    (x + seed * 31f + 500f) /
                        Mathf.Max(1f, moistureNoiseScale),
                    (z + seed * 17f - 200f) /
                        Mathf.Max(1f, moistureNoiseScale));

                float moisture = Mathf.Clamp01(
                    moistureNoise
                    + (moistureNoise - 0.5f) * moistureVariation);

                if (riverDischarge != null &&
                    i < riverDischarge.Length)
                {
                    float riverWetness = Mathf.Clamp01(
                        riverDischarge[i] / 2500f);

                    moisture = Mathf.Clamp01(
                        moisture + riverWetness * 0.25f);
                }

                if (temperature <= taigaTemperature &&
                    elevation > 0.45f)
                {
                    biomeMap[i] = BiomeType.Taiga;
                }
                else if (temperature >= tropicalTemperature &&
                         moisture >= 0.70f)
                {
                    biomeMap[i] = BiomeType.TropicalRainforest;
                }
                else if (moisture >= wetlandMoisture &&
                         elevation < 0.20f)
                {
                    biomeMap[i] = BiomeType.Wetlands;
                }
                else if (temperature >= desertTemperature &&
                         moisture <= desertMoisture &&
                         elevation < 0.20f)
                {
                    biomeMap[i] = BiomeType.Desert;
                }
                else
                {
                    biomeMap[i] = BiomeType.TemperateForest;
                }
            }
        }

        SpawnTrees(
            worldHeights,
            normals,
            width,
            length,
            spacing,
            seaLevel,
            seed);
    }

    private void SpawnTrees(
        float[] worldHeights,
        Vector3[] normals,
        int width,
        int length,
        float spacing,
        float seaLevel,
        int seed)
    {
        ClearPreviousTrees();

        if (spacing <= 0f)
        {
            return;
        }

        // Convert desired world-space tree spacing to terrain grid steps.
        int gridStep = Mathf.Max(
            1,
            Mathf.RoundToInt(treeSpacing / spacing));

        System.Random random = new System.Random(seed);

        for (int z = 0; z < length; z += gridStep)
        {
            for (int x = 0; x < width; x += gridStep)
            {
                int offsetX = Mathf.RoundToInt(
                    ((float)random.NextDouble() * 2f - 1f)
                    * gridStep * positionJitter
                );

                int offsetZ = Mathf.RoundToInt(
                    ((float)random.NextDouble() * 2f - 1f)
                    * gridStep * positionJitter
                );

                int px = Mathf.Clamp(x + offsetX, 0, width - 1);
                int pz = Mathf.Clamp(z + offsetZ, 0, length - 1);

                int i = pz * width + px;

                if (worldHeights[i] <= seaLevel)
                    continue;

                // Avoid placing trees on steep slopes when normals are available.
                if (normals != null && i < normals.Length &&
                    normals[i].y < minimumSlopeNormalY)
                {
                    continue;
                }

                if (random.NextDouble() > treeSpawnChance)
                    continue;

                GameObject prefab = GetTreePrefab(biomeMap[i]);

                // Desert biomes, or missing prefab assignments, produce no tree.
                if (prefab == null)
                    continue;

                Vector3 worldPosition = new Vector3(
                    transform.position.x + px * spacing,
                    worldHeights[i],
                    transform.position.z + pz * spacing
                );

                float angle = randomizeTreeRotation
                    ? (float)random.NextDouble() * 360f
                    : 0f;

                Quaternion rotation =
                    Quaternion.Euler(0f, angle, 0f);

                                
                GameObject tree = Instantiate(
                    prefab,
                    worldPosition,
                    rotation
                );

                tree.transform.SetParent(treeParent, true);
                tree.transform.localScale = Vector3.one * 0.1f;
                tree.transform.localScale = Vector3.one;
                tree.SetActive(true);

                // Ensure the prefab's renderers are enabled.
                Renderer[] renderers =
                    tree.GetComponentsInChildren<Renderer>(true);

                foreach (Renderer r in renderers)
                {
                    r.enabled = true;
                }
            }
        }
    }

    private GameObject GetTreePrefab(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Taiga:
                return firTreePrefab;

            case BiomeType.TemperateForest:
                return oakTreePrefab;

            case BiomeType.TropicalRainforest:
                return palmTreePrefab;

            case BiomeType.Wetlands:
                return poplarTreePrefab;

            case BiomeType.Desert:
            default:
                return null;
        }
    }

    private void ClearPreviousTrees()
    {
        if (treeParent == null)
        {
            Transform existing = transform.Find(TREE_PARENT_NAME);

            if (existing != null)
            {
                treeParent = existing;
            }
            else
            {
                GameObject parentObject =
                    new GameObject(TREE_PARENT_NAME);

                treeParent = parentObject.transform;
                treeParent.SetParent(transform, false);
            }
        }

        // Remove the previous generated trees before spawning new ones.
        for (int i = treeParent.childCount - 1; i >= 0; i--)
        {
            GameObject child = treeParent.GetChild(i).gameObject;

            if (Application.isPlaying)
                Destroy(child);
            else
                DestroyImmediate(child);
        }
    }
}
