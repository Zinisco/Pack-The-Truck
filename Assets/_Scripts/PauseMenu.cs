using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public class PauseMenu : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject pauseRoot;              // the panel/root you enable/disable
    [SerializeField] private GameObject firstSelectedWhenOpen;  // optional: first button to highlight
    [SerializeField] private GameObject firstSelectedWhenClose; // optional: gameplay-selected object (can be null)

    [SerializeField] private GameObject packUI;

    [Header("Behavior")]
    [SerializeField] private bool pauseTimeScale = true;
    [SerializeField] private bool pauseAudioListener = false;   // optional

    public bool IsPaused { get; private set; }

    float _prevTimeScale = 1f;

    void Awake()
    {
        if (!pauseRoot) pauseRoot = gameObject;
        pauseRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (IsPaused) Resume();
    }

    public void Toggle()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    public void Pause()
    {
        if (IsPaused) return;
        IsPaused = true;

        pauseRoot.SetActive(true);
        packUI.SetActive(false); // hide pack UI when pausing

        if (pauseTimeScale)
        {
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        if (pauseAudioListener)
            AudioListener.pause = true;

        // UI focus (gamepad friendly)
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            if (firstSelectedWhenOpen != null)
                EventSystem.current.SetSelectedGameObject(firstSelectedWhenOpen);
        }
    }

    public void Resume()
    {
        if (!IsPaused) return;
        IsPaused = false;

        if (pauseTimeScale)
            Time.timeScale = _prevTimeScale <= 0f ? 1f : _prevTimeScale;

        if (pauseAudioListener)
            AudioListener.pause = false;

        pauseRoot.SetActive(false);
        packUI.SetActive(true);

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            if (firstSelectedWhenClose != null)
                EventSystem.current.SetSelectedGameObject(firstSelectedWhenClose);
        }
    }

    // --- Button methods ---
    public void Restart()
    {
        Resume(); // restore timescale before reload
        var scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.buildIndex);
    }

    public void MainMenu()
    {
        Resume();
        // Change this to your menu scene name or build index
        SceneManager.LoadScene("MainMenu");
    }

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // If you have a settings panel, you can open it here.
    public void OpenSettingsPanel(GameObject settingsPanel)
    {
        if (!settingsPanel) return;
        settingsPanel.SetActive(true);
        pauseRoot.SetActive(false);
    }

    public void CloseSettingsPanel(GameObject settingsPanel)
    {
        if (!settingsPanel) return;
        settingsPanel.SetActive(false);
        pauseRoot.SetActive(true);

        // Re-select the first pause button again
        if (EventSystem.current != null && firstSelectedWhenOpen != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(firstSelectedWhenOpen);
        }
    }
}