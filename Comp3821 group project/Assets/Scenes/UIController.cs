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
    
    // Add future pages here 

    [Header("General Settings Dropdown")]
    public GameObject generalSettingsMenu;
    public RectTransform dropdownArrow;

    [Header("General Settings Panels")]
    public GameObject seedPanel;
    public GameObject biomePresetsPanel;
    public GameObject colorAltitudePanel;
    public GameObject devSettingsPanel;

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

    // General Settings Menu ////////////////
    public void ToggleGeneralSettingsMenu()
    {
        bool isOpening = !generalSettingsMenu.activeSelf;

        generalSettingsMenu.SetActive(isOpening);

        if (dropdownArrow != null)
        {
            dropdownArrow.localRotation = Quaternion.Euler(
                0f, 0f, isOpening ? 180f : 0f
            );
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

    public void onSlopeColorToggleChanged(bool isOn)
    {
        dotGenerator.useSlopeDiagnosticColors = isOn;
        RequestRegen();
    }

    public void onElevationColorToggleChanged(bool isOn)
    {
        dotGenerator.useElevationDevColors = isOn;
        RequestRegen();
    }

    private void OnPlainsSliderChanged(float value)
    {
        plainsValueInput.SetTextWithoutNotify(value.ToString("F2"));
        dotGenerator.plainsAltitude = value;
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

    public void ExitApplication()
    {
        Debug.Log("Exiting Sandbox...");
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}