using System.Collections.Generic;
using UnityEngine;

public class PackUIController : MonoBehaviour
{
    [Header("Refs")]
    public PackManifest manifest;
    public PlacementController placement;
    public Transform contentRoot;          // ScrollView Content
    public PieceCardUI cardPrefab;

    [Header("Behavior")]
    public bool autoAdvanceToNext = true;

    // progress: def -> packed count
    readonly Dictionary<PieceDefinition, int> _packed = new();
    readonly List<(PieceDefinition def, int required)> _flat = new();
    readonly List<PieceCardUI> _cards = new();

    PieceDefinition _selected;

    void OnEnable()
    {
        if (placement != null)
            placement.OnPlaced += HandlePlaced;
    }

    void OnDisable()
    {
        if (placement != null)
            placement.OnPlaced -= HandlePlaced;
    }

    void Start()
    {
        Rebuild();
        SelectFirstAvailable(spawn: false);
    }

    public void Rebuild()
    {
        _flat.Clear();
        _cards.Clear();

        if (!manifest || !contentRoot || !cardPrefab) return;

        // clear content
        for (int i = contentRoot.childCount - 1; i >= 0; i--)
            Destroy(contentRoot.GetChild(i).gameObject);

        // flatten manifest requirements
        for (int i = 0; i < manifest.required.Count; i++)
        {
            var req = manifest.required[i];
            if (req == null || req.def == null || req.count <= 0) continue;
            _flat.Add((req.def, req.count));

            if (!_packed.ContainsKey(req.def))
                _packed[req.def] = 0;
        }

        // spawn cards
        for (int i = 0; i < _flat.Count; i++)
        {
            var (def, required) = _flat[i];

            var card = Instantiate(cardPrefab, contentRoot);
            _cards.Add(card);

            int packed = _packed.TryGetValue(def, out var p) ? p : 0;
            card.Bind(def, packed, required, OnCardClicked);
        }

        RefreshSelectionVisuals();
    }

    void OnCardClicked(PieceDefinition def)
    {
        if (!def) return;

        // Find required
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

        // refresh cards (simple + safe)
        for (int i = 0; i < _flat.Count; i++)
        {
            var (d, required) = _flat[i];
            int packed = _packed.TryGetValue(d, out var p) ? p : 0;
            _cards[i].Bind(d, packed, required, OnCardClicked);
        }

        if (autoAdvanceToNext)
            SelectFirstAvailable(spawn: true);
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
                    placement.BeginPlaceFromUI(def);   // only when you want to actually place

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
}