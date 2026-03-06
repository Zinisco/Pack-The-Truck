using UnityEngine;
using UnityEngine.UI;

public class SettingsMenu : MonoBehaviour
{
    [System.Serializable]
    public class Tab
    {
        public string id;
        public Button button;
        public GameObject panelRoot; // top-level panel (children stay managed inside)
    }

    [Header("Tabs")]
    public Tab[] tabs;

    [Header("Startup")]
    public string defaultTabId = "General";

    Tab _active;

    void Awake()
    {
        // Wire buttons
        foreach (var t in tabs)
        {
            if (!t.button) continue;
            var tab = t; // local copy for closure
            t.button.onClick.AddListener(() => Open(tab.id));
        }
    }

    void OnEnable()
    {
        Open(defaultTabId);
    }

    public void Open(string tabId)
    {
        Tab target = null;

        foreach (var t in tabs)
        {
            bool isTarget = t.id == tabId;
            if (t.panelRoot) t.panelRoot.SetActive(isTarget);
            if (isTarget) target = t;
        }

        _active = target;

        // Optional: keep UI navigation nice for controller/keyboard
        if (_active != null && _active.button != null)
            _active.button.Select();
    }
}