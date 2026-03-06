using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class UIButtonSound : MonoBehaviour
{
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(PlaySound);
    }

    private void PlaySound()
    {
        if (UIAudioPlayer.Instance != null)
            UIAudioPlayer.Instance.PlayClick();
    }
}