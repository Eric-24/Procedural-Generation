using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class SandboxUIController : MonoBehaviour
{
    [Header("References")]
    public DotGen dotGenerator;

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

    // --- Slider Callbacks for DotGen ---
    public void OnAmplitudeChanged(float val)
    {
        dotGenerator.ampMult = val;
        dotGenerator.GenerateDotMap();
    }

    public void OnFrequencyChanged(float val)
    {
        dotGenerator.freqMult = val;
        dotGenerator.GenerateDotMap();
    }

    public void OnSpacingChanged(float val)
    {
        dotGenerator.spacing = val;
        dotGenerator.GenerateDotMap();
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