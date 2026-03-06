using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public class PauseMenu : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject pauseRoot;
    [SerializeField] private GameObject firstSelectedWhenOpen;
    [SerializeField] private GameObject firstSelectedWhenClose;

    [SerializeField] private GameObject packUI;

    [Header("Behavior")]
    [SerializeField] private bool pauseTimeScale = true;
    [SerializeField] private bool pauseAudioListener = false;

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

        if (packUI) packUI.SetActive(false);

        if (pauseTimeScale)
        {
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        if (pauseAudioListener)
            AudioListener.pause = true;

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

        if (packUI) packUI.SetActive(true);

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            if (firstSelectedWhenClose != null)
                EventSystem.current.SetSelectedGameObject(firstSelectedWhenClose);
        }
    }

    public void Restart()
    {
        Resume();
        var scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.buildIndex);
    }

    public void MainMenu()
    {
        Resume();
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

    void OnApplicationQuit()
    {
        GameSettings.SaveAll();
    }

    public void OpenSettingsPanel(GameObject settingsPanel)
    {
        if (!settingsPanel) return;

        settingsPanel.SetActive(true);
        pauseRoot.SetActive(false);
    }

    public void CloseSettingsPanel(GameObject settingsPanel)
    {
        if (!settingsPanel) return;

        // Save settings only when closing the settings menu
        GameSettings.SaveAll();

        settingsPanel.SetActive(false);
        pauseRoot.SetActive(true);

        if (EventSystem.current != null && firstSelectedWhenOpen != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(firstSelectedWhenOpen);
        }
    }
}