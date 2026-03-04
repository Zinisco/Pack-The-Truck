using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class PauseMenuInput : MonoBehaviour
{
    [SerializeField] private PauseMenu pauseMenu;
    [SerializeField] private PlacementController placement;

    InputSystem_Actions _input;
    bool _subscribed;

    void Awake()
    {
        if (!pauseMenu) pauseMenu = FindFirstObjectByType<PauseMenu>();
        if (!placement) placement = FindFirstObjectByType<PlacementController>();
    }

    void OnEnable()
    {
        _subscribed = false;
        StartCoroutine(EnsureSubscribed());
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    IEnumerator EnsureSubscribed()
    {
        while (InputHub.Instance == null || InputHub.Instance.Actions == null)
            yield return null;

        _input = InputHub.Instance.Actions;

        // IMPORTANT: don't call _input.Enable() here if InputHub already enables it.
        _input.Player.Enable();

        _input.Player.CancelPlacement.performed += OnCancelOrBack; // ESC / B
        _input.Player.Pause.performed += OnPause;                 // Start
        _subscribed = true;
    }

    void Unsubscribe()
    {
        if (!_subscribed) return;

        if (_input != null)
        {
            _input.Player.CancelPlacement.performed -= OnCancelOrBack;
            _input.Player.Pause.performed -= OnPause;
        }

        _subscribed = false;
    }

    void OnCancelOrBack(InputAction.CallbackContext ctx)
    {
        // ESC / B
        if (placement != null && placement.IsPlacing)
            placement.CancelPlacement(); // cancel placememt

        if (pauseMenu) pauseMenu.Toggle();
    }

    void OnPause(InputAction.CallbackContext ctx)
    {
        if (placement != null && placement.IsPlacing)
            placement.CancelPlacement(); // cancel placement

        if (pauseMenu) pauseMenu.Toggle(); //pause anyway
    }
}