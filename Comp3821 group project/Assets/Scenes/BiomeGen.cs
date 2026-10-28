using UnityEngine;

public class BiomeGen : MonoBehaviour {
    public enum BiomeType
    {
        Taiga,
        TemperateForest,
        TropicalRainforest,
        Wetlands,
        Desert
    }

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
    public float desertMoisture = 0.10f; // moisture below which arid biomes gen

    [Range(0f, 1f)]
    public float wetlandMoisture = 0.78f; // moisture above which wetlands gen


    [Header("Biome Colours")]
    public bool useBiomeColors = true;

    public Color taigaColor = new Color(0.20f, 0.34f, 0.20f);

    public Color temperateForestColor = new Color(0.18f, 0.48f, 0.22f);

    public Color tropicalColor = new Color(0.04f, 0.32f, 0.12f);

    public Color wetlandsColor = new Color(0.28f, 0.43f, 0.27f);

    public Color desertColor = new Color(0.78f, 0.64f, 0.37f);

    // Arrays for mapping biomes, biome colours (actually 2D, given a width)
    // Assigning biomes
    public BiomeType[] biomeMap
    {
        get; private set;
    }

    public Color[] biomeColors 
    { 
        get; private set; 
    }

    // Returns biome colour, given a biome type
    private Color GetColor(BiomeType biomeType)
    {
        switch (biomeType)
        {
            case BiomeType.Taiga:
                return taigaColor;

            case BiomeType.TemperateForest:
                return temperateForestColor;

            case BiomeType.TropicalRainforest:
                return tropicalColor;

            case BiomeType.Wetlands:
                return wetlandsColor;

            case BiomeType.Desert:
                return desertColor;

            default:
                return Color.magenta;
        }
    }

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

        if (normalizedHeights == null || worldHeights == null || normalizedHeights.Length != count || worldHeights.Length != count)
        {
            return;
        }

        // Making sure biomeMap and biomeColors array sizes are correct
        if (biomeMap == null || biomeMap.Length != count)
        {
            biomeMap = new BiomeType[count];
            biomeColors = new Color[count];
        }

        for (int z = 0; z < length; z++)
        {
            for (int x = 0; x < width; x++)
            {
                // Converting 2D position to 1D
                int i = z * width + x;

                float elevation = Mathf.Clamp01(normalizedHeights[i]);

                // Cooler toward the north and south edges.
                float latitude = Mathf.Abs(2f * z / Mathf.Max(1, length - 1) - 1f);

                // Creating smooth variation in temp
                float tempNoise = Mathf.PerlinNoise(
                    (x + seed * 13f) / Mathf.Max(1f, temperatureNoiseScale),
                    (z - seed * 7f) / Mathf.Max(1f, temperatureNoiseScale));

                // Calculating temp: latitude, temp noise/variation, elevation
                float temperature = Mathf.Clamp01(
                    1f - latitude * 0.75f
                    + (tempNoise - 0.5f) * temperatureVariation
                    - elevation * 0.35f);

                // Creating smooth variation in moisture
                float moistureNoise = Mathf.PerlinNoise(
                    (x + seed * 31f + 500f) / Mathf.Max(1f, moistureNoiseScale),
                    (z + seed * 17f - 200f) / Mathf.Max(1f, moistureNoiseScale));

                // Calculating moisture
                float moisture = Mathf.Clamp01(moistureNoise + (moistureNoise - 0.5f) * moistureVariation);

                // River discharge increases local moisture
                if (riverDischarge != null && i < riverDischarge.Length)
                {
                    float riverWetness = Mathf.Clamp01(riverDischarge[i] / 2500f);
                    moisture = Mathf.Clamp01(moisture + riverWetness * 0.25f);
                }

                if (worldHeights[i] <= seaLevel)
                {
                    // Don't set biome colour
                    biomeColors[i] = Color.magenta;
                    continue;
                }
                else if (temperature <= taigaTemperature && elevation > 0.45f)
                {
                    // Taiga: low temperature, high elevation
                    biomeMap[i] = BiomeType.Taiga;
                }
                else if (temperature >= tropicalTemperature && moisture >= 0.70f)
                {
                    // Tropical rainforest: high temperature, high moisture
                    biomeMap[i] = BiomeType.TropicalRainforest;
                }
                else if (moisture >= wetlandMoisture && elevation < 0.20f)
                {
                    // Wetlands: high moisture, low elevation
                    biomeMap[i] = BiomeType.Wetlands;
                }
                else if (temperature >= desertTemperature && moisture <= desertMoisture && elevation < 0.20f)
                {
                    // Desert: high temperature, low moisture, low elevation
                    biomeMap[i] = BiomeType.Desert;
                }
                else
                {
                    // Temperate forest: default biome for all other land
                    biomeMap[i] = BiomeType.TemperateForest;
                }


                biomeColors[i] = GetColor(biomeMap[i]);
            }
        }
    }
}
