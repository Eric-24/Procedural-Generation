using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

public class SandboxUIController : MonoBehaviour
{
    [Header("References")]
    public DotGen dotGenerator;

    [Header("Amplitude Slider Range")]
    public float ampMin = 0.1f;
    public float ampMax = 1f;

    [Header("Seed")]
    public TMP_InputField seedInputField;

    [Header("Live Regeneration")]
    public float minRegenerationInterval = 0.05f;
    private bool needsRegeneration;
    private float lastRegenerationTime;

    [Header("Menus & Collapsible Dock")]
    public GameObject escapeMenuPanel;
    public GameObject bottomDockPanel;
    public GameObject openDockButton;

    [Header("Color Altitude")]
    public Slider plainsSlider;
    public TMP_InputField plainsValueInput;
    public Slider valleySlider;
    public TMP_InputField valleyValueInput;

    public Slider rockSlider;
    public TMP_InputField rockValueInput;

    public Slider slateSlider;
    public TMP_InputField slateValueInput;

    public Slider snowSlider;
    public TMP_InputField snowValueInput;

    [Header("Development Settings")]
    public Toggle slopeDiagnosticsToggle;
    public Toggle elevationToggle;
    public Toggle waterDischargeToggle;

    [Header("Category Pages (Right Side)")]
    public GameObject advancedParametersPage;
    public GameObject generalSettingsPage;

    [Header("Terrain Settings")]

    public Slider gridWidthSlider;
    public TMP_InputField gridWidthInput;

    public Slider gridLengthSlider;
    public TMP_InputField gridLengthInput;

    [Header("Noise Settings")]

    public Slider macroScaleSlider;
    public TMP_InputField macroScaleInput;

    public Slider macroRangeContrastSlider;
    public TMP_InputField macroRangeContrastInput;

    public Slider largeNoiseScaleSlider;
    public TMP_InputField largeNoiseScaleInput;

    public Slider largeNoiseAmplitudeSlider;
    public TMP_InputField largeNoiseAmplitudeInput;

    public Slider fernBranchingStrengthSlider;
    public TMP_InputField fernBranchingStrengthInput;

    public Slider mountainPowerSlider;
    public TMP_InputField mountainPowerInput;

    public Slider mountainBeltStrengthSlider;
    public TMP_InputField mountainBeltStrengthInput;

    [Header("Water Settings")]

    public Toggle enableWaterToggle;

    public Slider seaLevelSlider;
    public TMP_InputField seaLevelInput;

    public Slider coastAltitudeSlider;
    public TMP_InputField coastAltitudeInput;

    public Slider waterOpacitySlider;
    public TMP_InputField waterOpacityInput;

    public Slider riverFluxThresholdSlider;
    public TMP_InputField riverFluxThresholdInput;

    public Slider sandShoreRadiusSlider;
    public TMP_InputField sandShoreRadiusInput;

    public Slider sandAltitudeSlider;
    public TMP_InputField sandAltitudeInput;

    [Header("Desert Settings")]

    public Slider desertCoverageSlider;
    public TMP_InputField desertCoverageInput;

    public Slider desertNoiseScaleSlider;
    public TMP_InputField desertNoiseScaleInput;

    public Slider desertBaseHeightSlider;
    public TMP_InputField desertBaseHeightInput;

    public Slider duneAmplitudeSlider;
    public TMP_InputField duneAmplitudeInput;

    [Header("Hydraulic Erosion")]

    public Toggle enableHydraulicErosionToggle;

    public Slider erosionIterationsSlider;
    public TMP_InputField erosionIterationsInput;

    public Slider erosionRadiusSlider;
    public TMP_InputField erosionRadiusInput;

    public Slider erosionInertiaSlider;
    public TMP_InputField erosionInertiaInput;

    public Slider sedimentCapacitySlider;
    public TMP_InputField sedimentCapacityInput;

    public Slider erodeSpeedSlider;
    public TMP_InputField erodeSpeedInput;

    public Slider depositSpeedSlider;
    public TMP_InputField depositSpeedInput;

    public Slider dropletLifetimeSlider;
    public TMP_InputField dropletLifetimeInput;

    [Header("Thermal Erosion")]

    public Toggle enableThermalErosionToggle;

    public Slider thermalIterationsSlider;
    public TMP_InputField thermalIterationsInput;

    public Slider talusThresholdSlider;
    public TMP_InputField talusThresholdInput;

    public Slider thermalRelaxationSlider;
    public TMP_InputField thermalRelaxationInput;

    // Add future pages here 

    [Header("General Settings Dropdown")]
    public GameObject generalSettingsMenu;
    public RectTransform dropdownArrow;
    public RectTransform advancedSettingsButton;
    public float advancedSettingsOffset = 100f;

    private Vector2 advancedSettingsOriginalPosition;

    [Header("General Settings Panels")]
    public GameObject seedPanel;
    public GameObject biomePresetsPanel;
    public GameObject colorAltitudePanel;
    public GameObject devSettingsPanel;

    [Header("Advanced Settings Dropdown")]
    public GameObject advancedSettingsMenu;
    public RectTransform advancedDropdownArrow;

    [Header("Advanced Settings Panels")]
    public GameObject terrainPanel;
    public GameObject hydraulicErosionPanel;
    public GameObject thermalErosionPanel;
    public GameObject waterPanel;
    public GameObject desertPanel;

    void Update()
    {
        // Toggle Escape Menu when Escape key is pressed
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ToggleEscapeMenu();
        }

        // Only regenerate if necessary and limited amounts
        if (needsRegeneration && Time.unscaledTime - lastRegenerationTime >= minRegenerationInterval)
        {
            needsRegeneration = false;
            lastRegenerationTime = Time.unscaledTime;
            dotGenerator.GenerateDotMap();
        }
    }

    void Start()
    {
        SetupAltitudeSlider(plainsSlider, plainsValueInput,
            value => dotGenerator.plainsAltitude = value, dotGenerator.plainsAltitude);

        SetupAltitudeSlider(valleySlider, valleyValueInput,
            value => dotGenerator.valleyAltitude = value, dotGenerator.valleyAltitude);

        SetupAltitudeSlider(rockSlider, rockValueInput,
            value => dotGenerator.rockAltitude = value, dotGenerator.rockAltitude);

        SetupAltitudeSlider(slateSlider, slateValueInput,
            value => dotGenerator.slateAltitude = value, dotGenerator.slateAltitude);

        SetupAltitudeSlider(snowSlider, snowValueInput,
            value => dotGenerator.snowAltitude = value, dotGenerator.snowAltitude);

        SetupToggle(slopeDiagnosticsToggle,
            isOn => dotGenerator.useSlopeDiagnosticColors = isOn);

        SetupToggle(elevationToggle,
            isOn => dotGenerator.useElevationDevColors = isOn);

        SetupToggle(waterDischargeToggle,
            isOn => dotGenerator.showWaterDischargeDebug = isOn);

        advancedSettingsOriginalPosition = advancedSettingsButton.anchoredPosition;

        // Terrain Settings
        SetupAltitudeSlider(gridWidthSlider, gridWidthInput,
            value => dotGenerator.gridWidth = Mathf.RoundToInt(value),
            dotGenerator.gridWidth);

        SetupAltitudeSlider(gridLengthSlider, gridLengthInput,
            value => dotGenerator.gridLength = Mathf.RoundToInt(value),
            dotGenerator.gridLength);

        // Noise Settings
        SetupAltitudeSlider(macroScaleSlider, macroScaleInput,
            value => dotGenerator.macroScale = value,
            dotGenerator.macroScale);

        SetupAltitudeSlider(macroRangeContrastSlider, macroRangeContrastInput,
            value => dotGenerator.macroRangeContrast = value,
            dotGenerator.macroRangeContrast);

        SetupAltitudeSlider(largeNoiseScaleSlider, largeNoiseScaleInput,
            value => dotGenerator.largeNoiseScale = value,
            dotGenerator.largeNoiseScale);

        SetupAltitudeSlider(largeNoiseAmplitudeSlider, largeNoiseAmplitudeInput,
            value => dotGenerator.largeNoiseAmplitude = value,
            dotGenerator.largeNoiseAmplitude);

        SetupAltitudeSlider(fernBranchingStrengthSlider, fernBranchingStrengthInput,
            value => dotGenerator.fernBranchingStrength = value,
            dotGenerator.fernBranchingStrength);

        SetupAltitudeSlider(mountainPowerSlider, mountainPowerInput,
            value => dotGenerator.mountainPower = value,
            dotGenerator.mountainPower);

        SetupAltitudeSlider(mountainBeltStrengthSlider, mountainBeltStrengthInput,
            value => dotGenerator.mountainBeltStrength = value,
            dotGenerator.mountainBeltStrength);

        // Water Settings
        SetupToggle(enableWaterToggle,
            isOn => dotGenerator.enableVolumedWater = isOn);

        SetupAltitudeSlider(seaLevelSlider, seaLevelInput,
            value => dotGenerator.oceanSeaLevel = value,
            dotGenerator.oceanSeaLevel);

        SetupAltitudeSlider(coastAltitudeSlider, coastAltitudeInput,
            value => dotGenerator.coastlineAltitude = value,
            dotGenerator.coastlineAltitude);

        SetupAltitudeSlider(waterOpacitySlider, waterOpacityInput,
            value => dotGenerator.waterOpacity = value,
            dotGenerator.waterOpacity);

        SetupAltitudeSlider(riverFluxThresholdSlider, riverFluxThresholdInput,
            value => dotGenerator.riverFluxThreshold = value,
            dotGenerator.riverFluxThreshold);

        SetupAltitudeSlider(sandShoreRadiusSlider, sandShoreRadiusInput,
            value => dotGenerator.sandShoreRadius = value,
            dotGenerator.sandShoreRadius);

        SetupAltitudeSlider(sandAltitudeSlider, sandAltitudeInput,
            value => dotGenerator.sandMaxAltitude = value,
            dotGenerator.sandMaxAltitude);

        // Desert Settings
        SetupAltitudeSlider(desertCoverageSlider, desertCoverageInput,
            value => dotGenerator.desertCoverage = value,
            dotGenerator.desertCoverage);

        SetupAltitudeSlider(desertNoiseScaleSlider, desertNoiseScaleInput,
            value => dotGenerator.desertNoiseScale = value,
            dotGenerator.desertNoiseScale);

        SetupAltitudeSlider(desertBaseHeightSlider, desertBaseHeightInput,
            value => dotGenerator.desertBaseHeight = value,
            dotGenerator.desertBaseHeight);

        SetupAltitudeSlider(duneAmplitudeSlider, duneAmplitudeInput,
            value => dotGenerator.duneAmplitude = value,
            dotGenerator.duneAmplitude);

        // Hydraulic Erosion
        SetupToggle(enableHydraulicErosionToggle,
            isOn => dotGenerator.enableHydraulicErosion = isOn);

        SetupAltitudeSlider(erosionIterationsSlider, erosionIterationsInput,
            value => dotGenerator.erosionIterations = Mathf.RoundToInt(value),
            dotGenerator.erosionIterations);

        SetupAltitudeSlider(erosionRadiusSlider, erosionRadiusInput,
            value => dotGenerator.erosionRadius = Mathf.RoundToInt(value),
            dotGenerator.erosionRadius);

        SetupAltitudeSlider(erosionInertiaSlider, erosionInertiaInput,
            value => dotGenerator.erosionInertia = value,
            dotGenerator.erosionInertia);

        SetupAltitudeSlider(sedimentCapacitySlider, sedimentCapacityInput,
            value => dotGenerator.sedimentCapacityFactor = value,
            dotGenerator.sedimentCapacityFactor);

        SetupAltitudeSlider(erodeSpeedSlider, erodeSpeedInput,
            value => dotGenerator.erodeSpeed = value,
            dotGenerator.erodeSpeed);

        SetupAltitudeSlider(depositSpeedSlider, depositSpeedInput,
            value => dotGenerator.depositSpeed = value,
            dotGenerator.depositSpeed);

        SetupAltitudeSlider(dropletLifetimeSlider, dropletLifetimeInput,
            value => dotGenerator.maxDropletLifetime = Mathf.RoundToInt(value),
            dotGenerator.maxDropletLifetime);

        // Thermal Erosion
        SetupToggle(enableThermalErosionToggle,
            isOn => dotGenerator.enableThermalErosion = isOn);

        SetupAltitudeSlider(thermalIterationsSlider, thermalIterationsInput,
            value => dotGenerator.thermalIterations = Mathf.RoundToInt(value),
            dotGenerator.thermalIterations);

        SetupAltitudeSlider(talusThresholdSlider, talusThresholdInput,
            value => dotGenerator.talusAngleThreshold = value,
            dotGenerator.talusAngleThreshold);

        SetupAltitudeSlider(thermalRelaxationSlider, thermalRelaxationInput,
            value => dotGenerator.thermalRelaxation = value,
            dotGenerator.thermalRelaxation);
    }

    // Request the regeneration of the dot map
    private void RequestRegen()
    {
        needsRegeneration = true;
    }

    public void ToggleEscapeMenu()
    {
        bool isActive = escapeMenuPanel.activeSelf;
        escapeMenuPanel.SetActive(!isActive);
    }

    public void CollapseBottomDock()
    {
        bottomDockPanel.SetActive(false);
        openDockButton.SetActive(true);
    }

    public void ExpandBottomDock()
    {
        bottomDockPanel.SetActive(true);
        openDockButton.SetActive(false);
    }

    public void OpenAdvancedParametersTab()
    {
        ShowCategoryPage(advancedParametersPage);
    }

    public void OpenGeneralSettingsTab()
    {
        ShowCategoryPage(generalSettingsPage);
        ShowGeneralSettingsPanel(seedPanel);
    }

    private void ShowCategoryPage(GameObject selectedPage)
    {
        advancedParametersPage.SetActive(false);
        generalSettingsPage.SetActive(false);

        selectedPage.SetActive(true);
    }

    // General Settings Menu 
    public void ToggleGeneralSettingsMenu()
    {
        bool isOpening = !generalSettingsMenu.activeSelf;

        // Automatically close advanced settings
        advancedSettingsMenu.SetActive(false);

        if (advancedDropdownArrow != null)
        {
            advancedDropdownArrow.localRotation = Quaternion.Euler(0f, 0f, 0f);
        }

        advancedParametersPage.SetActive(false);

        // Open/Close general settings
        generalSettingsMenu.SetActive(isOpening);

        if (dropdownArrow != null)
        {
            dropdownArrow.localRotation = Quaternion.Euler(
                0f, 0f, isOpening ? 180f : 0f
            );
        }

        if (advancedSettingsButton != null)
        {
            advancedSettingsButton.anchoredPosition = isOpening
                ? advancedSettingsOriginalPosition + Vector2.down * advancedSettingsOffset
                : advancedSettingsOriginalPosition;
        }

        if (isOpening)
        {
            OpenGeneralSettingsTab();
        }
    }

    public void OpenSeedPanel()
    {
        ShowCategoryPage(generalSettingsPage);
        ShowGeneralSettingsPanel(seedPanel);
    }

    public void OpenBiomePresetsPanel()
    {
        ShowCategoryPage(generalSettingsPage);
        ShowGeneralSettingsPanel(biomePresetsPanel);
    }

    public void OpenColorAltitudePanel()
    {
        ShowCategoryPage(generalSettingsPage);
        ShowGeneralSettingsPanel(colorAltitudePanel);
    }

    public void OpenDevSettingsPanel()
    {
        ShowCategoryPage(generalSettingsPage);
        ShowGeneralSettingsPanel(devSettingsPanel);
    }

    private void ShowGeneralSettingsPanel(GameObject selectedPanel)
    {
        seedPanel.SetActive(false);
        biomePresetsPanel.SetActive(false);
        colorAltitudePanel.SetActive(false);
        devSettingsPanel.SetActive(false);

        selectedPanel.SetActive(true);
    }

    private void ShowAdvancedSettingsPanel(GameObject selectedPanel)
    {
        terrainPanel.SetActive(false);
        hydraulicErosionPanel.SetActive(false);
        thermalErosionPanel.SetActive(false);
        waterPanel.SetActive(false);
        desertPanel.SetActive(false);

        selectedPanel.SetActive(true);
    }

    public void ToggleAdvancedSettingsMenu()
    {
        bool isOpening = !advancedSettingsMenu.activeSelf;

        // Automatically closing the general sections when advanced is opened
        generalSettingsMenu.SetActive(false);

        if (dropdownArrow != null)
        {
            dropdownArrow.localRotation = Quaternion.Euler(0f, 0f, 0f);
        }

        // Move advanced settings back to the right spot
        if (advancedSettingsButton != null)
        {
            advancedSettingsButton.anchoredPosition = advancedSettingsOriginalPosition;
        }

        generalSettingsPage.SetActive(false);

        // Open/Close advanced settings
        advancedSettingsMenu.SetActive(isOpening);

        if (advancedDropdownArrow != null)
        {
            advancedDropdownArrow.localRotation = Quaternion.Euler(
                0f, 0f, isOpening ? 180f : 0f
            );
        }

        if (isOpening)
        {
            OpenAdvancedParametersTab();
        }
        else
        {
            advancedParametersPage.SetActive(false);
        }
    }

    public void OpenTerrainPanel()
    {
        ShowCategoryPage(advancedParametersPage);
        ShowAdvancedSettingsPanel(terrainPanel);
    }

    public void OpenHydraulicErosionPanel()
    {
        ShowCategoryPage(advancedParametersPage);
        ShowAdvancedSettingsPanel(hydraulicErosionPanel);
    }

    public void OpenThermalErosionPanel()
    {
        ShowCategoryPage(advancedParametersPage);
        ShowAdvancedSettingsPanel(thermalErosionPanel);
    }

    public void OpenWaterPanel()
    {
        ShowCategoryPage(advancedParametersPage);
        ShowAdvancedSettingsPanel(waterPanel);
    }

    public void OpenDesertPanel()
    {
        ShowCategoryPage(advancedParametersPage);
        ShowAdvancedSettingsPanel(desertPanel);
    }

    ///// End General Settings Menu ///////////////////
    public void onAmplitudeChanged(float val)
    {
        dotGenerator.ampMult = val;
        RequestRegen();
    }

    public void onFrequencyChanged(float val)
    {
        dotGenerator.freqMult = val;
        RequestRegen();
    }

    public void onSpacingChanged(float val)
    {
        dotGenerator.spacing = val;
        RequestRegen();
    }

    public void onOctaveChanged(float val)
    {
        dotGenerator.octaves = Mathf.Max(1, Mathf.RoundToInt(val));
        RequestRegen();
    }

    public void onSeedChanged(string text)
    {
        if (int.TryParse(text, out int newSeed))
        {
            dotGenerator.seed = newSeed;
            RequestRegen();
        }
        else
        {
            Debug.LogWarning("SEED HAS TO BE A NUMBER");
        }
    }

    public void generateRandomSeed()
    {
        int newSeed = Random.Range(0, 1000000);
        dotGenerator.seed = newSeed;

        if (seedInputField != null)
        {
            seedInputField.text = newSeed.ToString();
        }

        RequestRegen();
    }

    public void setRandomDefault()
    {
        dotGenerator.seed = 0;
        seedInputField.text = "0";
        RequestRegen();
    }

    // Setting up color altitude sliders: plains, valley, etc.
    private void SetupAltitudeSlider(
    Slider slider,
    TMP_InputField input,
    System.Action<float> updateAltitude,
    float initialValue)
    {
        slider.SetValueWithoutNotify(initialValue);
        input.SetTextWithoutNotify(initialValue.ToString("F2"));

        slider.onValueChanged.AddListener(value =>
        {
            input.SetTextWithoutNotify(value.ToString("F2"));
            updateAltitude(value);
            RequestRegen();
        });

        input.onEndEdit.AddListener(text =>
        {
            if (float.TryParse(text, out float value))
            {
                value = Mathf.Clamp(value, slider.minValue, slider.maxValue);

                slider.SetValueWithoutNotify(value);
                input.SetTextWithoutNotify(value.ToString("F2"));

                updateAltitude(value);
                RequestRegen();
            }
            else
            {
                input.SetTextWithoutNotify(slider.value.ToString("F2"));
            }
        });
    }

    private void SetupToggle(Toggle toggle, System.Action<bool> updateSetting)
    {
        toggle.onValueChanged.AddListener(isOn =>
        {
            updateSetting(isOn);
            RequestRegen();
        });
    }

    // Biome Presets
    public void ApplyMixedPreset()
    {
        dotGenerator.macroRangeContrast = 0.85f;
        dotGenerator.largeNoiseAmplitude = 25f;
        dotGenerator.fernBranchingStrength = 0.8f;
        dotGenerator.mountainPower = 0.6f;
        dotGenerator.oceanSeaLevel = -34f;
        dotGenerator.coastlineAltitude = -30f;
        dotGenerator.desertCoverage = 0.1f;

        RequestRegen();
    }

    public void ApplyDesertPreset()
    {
        dotGenerator.macroRangeContrast = 0.85f;
        dotGenerator.largeNoiseAmplitude = 25f;
        dotGenerator.fernBranchingStrength = 0.8f;
        dotGenerator.mountainPower = 0.6f;
        dotGenerator.oceanSeaLevel = -34f;
        dotGenerator.coastlineAltitude = -30f;
        dotGenerator.desertCoverage = 1f;

        RequestRegen();
    }

    public void ApplyOceanPreset()
    {
        dotGenerator.macroRangeContrast = 0.85f;
        dotGenerator.largeNoiseAmplitude = 25f;
        dotGenerator.fernBranchingStrength = 0.8f;
        dotGenerator.mountainPower = 0.6f;
        dotGenerator.oceanSeaLevel = 20f;
        dotGenerator.coastlineAltitude = -80f;
        dotGenerator.desertCoverage = 0.5f;

        RequestRegen();
    }

    public void ApplyArchipelagoPreset()
    {
        dotGenerator.macroRangeContrast = 0.85f;
        dotGenerator.largeNoiseAmplitude = 25f;
        dotGenerator.fernBranchingStrength = 0.8f;
        dotGenerator.mountainPower = 0.6f;
        dotGenerator.oceanSeaLevel = -11f;
        dotGenerator.coastlineAltitude = -35f;
        dotGenerator.desertCoverage = 0f;

        RequestRegen();
    }

    public void ApplyPlainsPreset()
    {
        dotGenerator.macroRangeContrast = 0.8f;
        dotGenerator.largeNoiseAmplitude = 25f;
        dotGenerator.fernBranchingStrength = 0.6f;
        dotGenerator.mountainPower = 3f;
        dotGenerator.oceanSeaLevel = -40f;
        dotGenerator.coastlineAltitude = -22f;
        dotGenerator.desertCoverage = 0f;

        RequestRegen();
    }

    public void ApplyMountainsPreset()
    {
        dotGenerator.macroRangeContrast = 0.85f;
        dotGenerator.largeNoiseAmplitude = 50f;
        dotGenerator.fernBranchingStrength = 0.8f;
        dotGenerator.mountainPower = 1.25f;
        dotGenerator.oceanSeaLevel = -30f;
        dotGenerator.coastlineAltitude = 0f;
        dotGenerator.desertCoverage = 0f;

        RequestRegen();
    }

    public void ExitApplication()
    {
        Debug.Log("Exiting Sandbox...");
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}