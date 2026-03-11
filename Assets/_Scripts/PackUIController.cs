using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PackUIController : MonoBehaviour
{
    [Header("Refs")]
    public PackManifest manifest;
    public PlacementController placement;
    public Transform contentRoot;
    public PieceCardUI cardPrefab;

    [Header("Behavior")]
    public bool autoAdvanceToNext = true;

    [Header("Gamepad")]
    public bool alwaysShowOnGamepad = false;   // if true, panel stays visible when gamepad scheme active
    public bool allowToggleOnGamepad = true;   // if false and not alwaysShowOnGamepad, it’ll just stay hidden
    public bool autoFocusFirstCardOnShow = true;
    public float focusDelay = 0.01f; // one frame-ish, helps UI settle after animation

    [Header("UI Toggle + Animation")]
    public RectTransform panel;                 // drag your Pack UI panel root here
    public CanvasGroup canvasGroup;             // drag CanvasGroup here (or auto-find)
    public float animDuration = 0.18f;
    public float hiddenYOffset = 420f;          // how far up to slide when hidden
    public bool startHidden = false;

    [Header("Edge Hover Auto-Show")]
    public bool edgeHoverToShow = true;

    // y from bottom of screen in pixels
    public float showWhenMouseWithinBottomPixels = 18f;
    public float hideWhenMouseAboveBottomPixels = 80f; // bigger than show -> hysteresis

    [Tooltip("When the panel is visible, keep it open while the mouse is over the panel.")]
    public bool keepOpenWhileHoveringPanel = true;

    [Tooltip("Small delay before hiding after leaving the zone (prevents jitter).")]
    public float hideDelay = 0.08f;

    float _hideAtTime = -1f;

    [Tooltip("If true, you can't hide the UI while currently placing a piece.")]
    public bool lockWhilePlacing = true;

    bool _isVisible = true;
    Coroutine _animCo;
    GameObject _prevSelectedBeforePlace;
    bool _navWasEnabled = true;
    bool _lastPlacingState = false;

    // progress: def -> packed count
    readonly Dictionary<PieceDefinition, int> _packed = new();
    readonly List<(PieceDefinition def, int required)> _flat = new();
    readonly List<PieceCardUI> _cards = new();

    PieceDefinition _selected;

    void Awake()
    {
        if (!canvasGroup && panel) canvasGroup = panel.GetComponent<CanvasGroup>();
        if (!canvasGroup && panel) canvasGroup = panel.gameObject.AddComponent<CanvasGroup>();

        if (startHidden)
            SetVisibleInstant(false);
        else
            SetVisibleInstant(true);
    }

    void Start()
    {
        Rebuild();
        SelectFirstAvailable(spawn: false);
    }

    void Update()
    {
        // If placing and you lock UI, force it visible
        if (lockWhilePlacing && placement != null && placement.IsPlacing)
        {
            if (!_isVisible) SetVisible(true);
            _hideAtTime = -1f;
        }

        UpdatePlacementUILock();

        var scheme = (InputHub.Instance != null) ? InputHub.Instance.ActiveScheme : ControlSchemeMode.KeyboardMouse;

        // -------------------------
        // GAMEPAD MODE
        // -------------------------
        if (scheme == ControlSchemeMode.Gamepad)
        {
            // if you want it always open on controller
            if (alwaysShowOnGamepad)
            {
                if (!_isVisible) SetVisible(true);
                return;
            }

            // otherwise, optional toggle button
            if (allowToggleOnGamepad)
            {
                bool pressed = false;

                if (Gamepad.current != null)
                    pressed = Gamepad.current.selectButton.wasPressedThisFrame; // View/Back

                if (pressed)
                    Toggle();
            }

            return; // don’t run mouse edge-hover logic in gamepad scheme
        }

        // -------------------------
        // KEYBOARD & MOUSE MODE
        // -------------------------
        if (!edgeHoverToShow) return;
        if (Mouse.current == null) return;

        float mouseY = Mouse.current.position.ReadValue().y; // 0 bottom -> Screen.height top
        float fromBottom = mouseY;

        bool wantsShow = fromBottom <= showWhenMouseWithinBottomPixels;
        bool hoveringPanel = keepOpenWhileHoveringPanel && _isVisible && IsPointerOverPanel();
        bool wantsHide = (fromBottom >= hideWhenMouseAboveBottomPixels) && !hoveringPanel;

        if (wantsShow)
        {
            _hideAtTime = -1f;
            if (!_isVisible) SetVisible(true);
            return;
        }

        if (wantsHide)
        {
            if (_hideAtTime < 0f) _hideAtTime = Time.unscaledTime + hideDelay;
            if (_isVisible && Time.unscaledTime >= _hideAtTime)
                SetVisible(false);
            return;
        }

        _hideAtTime = -1f;
    }

    public void Toggle()
    {
        SetVisible(!_isVisible);
    }

    public void SetVisible(bool show)
    {
        if (_animCo != null) StopCoroutine(_animCo);
        _animCo = StartCoroutine(AnimateVisible(show));
    }

    bool IsPointerOverPanel()
    {
        if (!panel) return false;

        Vector2 mousePos = Mouse.current.position.ReadValue();

        // Works for Screen Space Overlay and Camera canvases
        Canvas parentCanvas = panel.GetComponentInParent<Canvas>();
        Camera uiCam = null;
        if (parentCanvas && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCam = parentCanvas.worldCamera;

        return RectTransformUtility.RectangleContainsScreenPoint(panel, mousePos, uiCam);
    }

    void SetVisibleInstant(bool show)
    {
        _isVisible = show;

        if (!panel) return;

        Vector2 p = panel.anchoredPosition;
        p.y = show ? 0f : hiddenYOffset;
        panel.anchoredPosition = p;

        if (canvasGroup)
        {
            canvasGroup.interactable = show;
            canvasGroup.blocksRaycasts = show;
        }
    }

    IEnumerator AnimateVisible(bool show)
    {
        _isVisible = show;

        if (!panel)
            yield break;

        float t = 0f;

        float startY = panel.anchoredPosition.y;
        float endY = show ? 0f : hiddenYOffset;

        float endA = show ? 1f : 0f;

        // enable interaction early when showing
        if (canvasGroup && show)
        {
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, animDuration);
            float ease = 1f - Mathf.Pow(1f - t, 3f); // easeOutCubic

            float y = Mathf.Lerp(startY, endY, ease);
            panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, y);

            yield return null;
        }

        panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, endY);

        if (canvasGroup)
        {

            // disable interaction when hidden
            if (!show)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }

        _animCo = null;

        if (show)
        {
            // Kick focus after opening
            StartCoroutine(FocusAfterDelay());
        }
    }

    bool IsGamepadScheme()
    {
        return InputHub.Instance != null && InputHub.Instance.ActiveScheme == ControlSchemeMode.Gamepad;
    }

    void TryFocusFirstAvailableCard()
    {
        if (!autoFocusFirstCardOnShow) return;
        if (!IsGamepadScheme()) return;
        if (placement != null && placement.IsPlacing) return;

        if (EventSystem.current == null) return;

        // If something in this panel is already selected, don't fight it.
        // (Optional, but usually feels better)
        var current = EventSystem.current.currentSelectedGameObject;
        if (current != null && current.transform.IsChildOf(panel)) return;

        // Find first interactable button in your spawned cards
        for (int i = 0; i < _cards.Count; i++)
        {
            var c = _cards[i];
            if (c == null || c.button == null) continue;
            if (!c.button.IsActive() || !c.button.interactable) continue;

            EventSystem.current.SetSelectedGameObject(c.button.gameObject);
            return;
        }

        // Nothing selectable (all complete) -> clear selection
        EventSystem.current.SetSelectedGameObject(null);
    }

    IEnumerator FocusAfterDelay()
    {
        // Wait a tiny bit so layout/animation/scrollview can settle
        float end = Time.unscaledTime + Mathf.Max(0f, focusDelay);
        while (Time.unscaledTime < end) yield return null;

        // Also wait one frame minimum
        yield return null;

        TryFocusFirstAvailableCard();
    }

    void SetUINavigationLocked(bool locked)
    {
        if (EventSystem.current == null) return;

        if (locked)
        {
            // Save current selection so we can restore it later
            _prevSelectedBeforePlace = EventSystem.current.currentSelectedGameObject;

            // Stop D-pad / stick navigation from changing UI selection
            _navWasEnabled = EventSystem.current.sendNavigationEvents;
            EventSystem.current.sendNavigationEvents = false;

            // Clear selection so nothing highlights / moves
            EventSystem.current.SetSelectedGameObject(null);

            // Make the panel non-interactable (prevents UI from consuming inputs)
            if (canvasGroup)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false; // prevent UI from eating inputs while placing
            }
        }
        else
        {
            // Re-enable navigation
            EventSystem.current.sendNavigationEvents = _navWasEnabled;

            // Restore interactability if visible
            if (canvasGroup)
            {
                canvasGroup.interactable = _isVisible;
                canvasGroup.blocksRaycasts = _isVisible;
            }

            // Restore focus (or choose first available again)
            if (_isVisible)
                StartCoroutine(FocusAfterDelay());
        }
    }

    void UpdatePlacementUILock()
    {
        if (placement == null) return;

        bool placingNow = placement.IsPlacing;

        if (placingNow == _lastPlacingState) return;
        _lastPlacingState = placingNow;

        // Only lock UI nav on gamepad (optional, but usually desired)
        if (IsGamepadScheme())
            SetUINavigationLocked(placingNow);
    }

    void OnEnable()
    {
        if (placement != null)
        {
            placement.OnPlaced += HandlePlaced;
            placement.OnReturned += HandleReturned;
        }
    }

    void OnDisable()
    {
        if (placement != null)
        {
            placement.OnPlaced -= HandlePlaced;
            placement.OnReturned -= HandleReturned;
        }
    }

    public void Rebuild()
    {
        _flat.Clear();
        _cards.Clear();

        if (!manifest || !contentRoot || !cardPrefab) return;

        for (int i = contentRoot.childCount - 1; i >= 0; i--)
            Destroy(contentRoot.GetChild(i).gameObject);

        for (int i = 0; i < manifest.required.Count; i++)
        {
            var req = manifest.required[i];
            if (req == null || req.def == null || req.count <= 0) continue;
            _flat.Add((req.def, req.count));

            if (!_packed.ContainsKey(req.def))
                _packed[req.def] = 0;
        }

        for (int i = 0; i < _flat.Count; i++)
        {
            var (def, required) = _flat[i];

            var card = Instantiate(cardPrefab, contentRoot);
            _cards.Add(card);

            int packed = _packed.TryGetValue(def, out var p) ? p : 0;
            card.Bind(def, packed, required, OnCardClicked);
        }

        RefreshSelectionVisuals();
        if (_isVisible) StartCoroutine(FocusAfterDelay());
    }

    void OnCardClicked(PieceDefinition def)
    {
        if (!def) return;

        int required = 0;
        for (int i = 0; i < _flat.Count; i++)
            if (_flat[i].def == def) { required = _flat[i].required; break; }

        int packed = _packed.TryGetValue(def, out var p) ? p : 0;
        int remaining = Mathf.Max(0, required - packed);
        if (remaining <= 0) return;

        _selected = def;
        placement.BeginPlaceFromUI(def);
        RefreshSelectionVisuals();
    }

    void HandlePlaced(PieceDefinition def, int placedId)
    {
        if (!def) return;

        if (!_packed.ContainsKey(def))
            _packed[def] = 0;

        _packed[def]++;

        for (int i = 0; i < _flat.Count; i++)
        {
            var (d, required) = _flat[i];
            int packed = _packed.TryGetValue(d, out var p) ? p : 0;
            _cards[i].Bind(d, packed, required, OnCardClicked);
        }

        RefreshSelectionVisuals();

        // Kick auto-advance after placement (works even if panel was already open)
        StopCoroutine(nameof(AutoAdvanceAfterPlaced));
        StartCoroutine(AutoAdvanceAfterPlaced());
    }

    void HandleReturned(PieceDefinition def, int placedId)
    {
        if (!def) return;

        if (_packed.ContainsKey(def))
            _packed[def] = Mathf.Max(0, _packed[def] - 1);
        else
            _packed[def] = 0;

        for (int i = 0; i < _flat.Count; i++)
        {
            var (d, required) = _flat[i];
            int packed = _packed.TryGetValue(d, out var p) ? p : 0;
            _cards[i].Bind(d, packed, required, OnCardClicked);
        }

        // if current selection was null or completed before, pick something valid again
        if (_selected == null)
            SelectFirstAvailable(spawn: false);
        else
            RefreshSelectionVisuals();
    }

    void SelectFirstAvailable(bool spawn)
    {
        for (int i = 0; i < _flat.Count; i++)
        {
            var (def, required) = _flat[i];
            int packed = _packed.TryGetValue(def, out var p) ? p : 0;

            if (packed < required)
            {
                _selected = def;

                if (spawn)
                    placement.BeginPlaceFromUI(def);

                RefreshSelectionVisuals();
                return;
            }
        }

        _selected = null;
        RefreshSelectionVisuals();
    }

    void RefreshSelectionVisuals()
    {
        for (int i = 0; i < _flat.Count; i++)
        {
            var def = _flat[i].def;
            _cards[i].SetSelected(def != null && def == _selected);
        }
    }

    IEnumerator AutoAdvanceAfterPlaced()
    {
        // Let PlacementController finish ExitPlacementMode and let layout rebuild settle
        yield return null;

        // Keep panel visible on controller, so you don't need to toggle
        if (!_isVisible)
            SetVisible(true);

        if (!autoAdvanceToNext) yield break;

        SelectFirstAvailable(spawn: true);

        // If we just spawned the next placement, keep UI navigation locked on gamepad
        // (prevents moving UI selection while placing)
        if (placement != null && placement.IsPlacing && IsGamepadScheme())
            SetUINavigationLocked(true);
    }
}