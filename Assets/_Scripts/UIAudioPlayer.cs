using UnityEngine;

public class UIAudioPlayer : MonoBehaviour
{
    public static UIAudioPlayer Instance { get; private set; }

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip clickSound;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void PlayClick()
    {
        if (audioSource == null || clickSound == null)
            return;

        audioSource.PlayOneShot(clickSound);
    }
}