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

    [Header("Category Pages (Right Side)")]
    public GameObject advancedParametersPage;
    // Add future pages here 

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
        advancedParametersPage.SetActive(true);
        // When you add more category pages later, set them to false here
    }

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

    public void ExitApplication()
    {
        Debug.Log("Exiting Sandbox...");
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}