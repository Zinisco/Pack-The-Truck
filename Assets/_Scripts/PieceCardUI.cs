using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PieceCardUI : MonoBehaviour
{
    [Header("UI")]
    public Button button;
    public Image iconImage;
    public TMP_Text nameText;
    public TMP_Text countText;

    public GameObject selectedOutline;
    public GameObject packedOverlay;

    PieceDefinition _def;
    System.Action<PieceDefinition> _onClick;

    public void Bind(PieceDefinition def, int packed, int required, System.Action<PieceDefinition> onClick)
    {
        _def = def;
        _onClick = onClick;

        if (nameText) nameText.text = def ? def.pieceName : "(Missing)";
        if (iconImage) iconImage.sprite = def ? def.icon : null;

        int remaining = Mathf.Max(0, required - packed);
        if (countText) countText.text = remaining.ToString();

        bool isComplete = remaining <= 0;
        if (packedOverlay) packedOverlay.SetActive(isComplete);

        if (button)
        {
            button.interactable = !isComplete;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => _onClick?.Invoke(_def));
        }
    }

    public void SetSelected(bool selected)
    {
        if (selectedOutline) selectedOutline.SetActive(selected);
    }
}