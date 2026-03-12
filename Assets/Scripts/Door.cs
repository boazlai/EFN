using System.Collections;
using UnityEngine;

public class Door : MonoBehaviour, IInteractable
{
    [Header("Door Settings")]
    [SerializeField] private float openAngle = 90f;
    [SerializeField] private float openSpeed = 3f;
    [SerializeField] private string openPrompt = "Press [E] to open";
    [SerializeField] private string closePrompt = "Press [E] to close";

    [Header("Lock Settings")]
    [SerializeField] private bool isLocked;
    [SerializeField] private string lockedPrompt = "Door is locked";

    [Header("Shake Settings")]
    [SerializeField] private float shakeAngle = 3f;
    [SerializeField] private float shakeSpeed = 20f;
    [SerializeField] private int shakeCount = 4;

    private bool _isShaking = false;

    [Header("Sound")]
    [SerializeField] private AudioClip openSound;
    [SerializeField] private AudioClip closeSound;
    [SerializeField] private AudioClip lockedSound;
    [SerializeField] private AudioSource audioSource;

    private Quaternion _closedRotation;
    private Quaternion _openRotation;
    private bool _isOpen = false;

    private void Start()
    {
        _closedRotation = transform.rotation;
        _openRotation = Quaternion.Euler(transform.eulerAngles + new Vector3(0, openAngle, 0));

        // Auto-add an AudioSource if none is assigned
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    private void Update()
    {
        Quaternion target = _isOpen ? _openRotation : _closedRotation;
        transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * openSpeed);
    }

    public void Interact()
    {
        if (isLocked)
        {
            PlayClip(lockedSound);
            if (!_isShaking)
                StartCoroutine(ShakeDoor());
            return;
        }

        _isOpen = !_isOpen;

        PlayClip(_isOpen ? openSound : closeSound);
    }

    public string GetPrompt()
    {
        if (isLocked)
            return lockedPrompt;

        return _isOpen ? closePrompt : openPrompt;
    }

    public void SetLocked(bool locked)
    {
        isLocked = locked;
    }

    private void PlayClip(AudioClip clip)
    {
        if (clip == null || audioSource == null)
            return;

        audioSource.PlayOneShot(clip);
    }

    private IEnumerator ShakeDoor()
    {
        _isShaking = true;
        Quaternion origin = transform.rotation;

        for (int i = 0; i < shakeCount; i++)
        {
            // Rattle right
            Quaternion right = origin * Quaternion.Euler(0, shakeAngle, 0);
            while (Quaternion.Angle(transform.rotation, right) > 0.1f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, right, shakeSpeed * Time.deltaTime);
                yield return null;
            }

            // Rattle left
            Quaternion left = origin * Quaternion.Euler(0, -shakeAngle, 0);
            while (Quaternion.Angle(transform.rotation, left) > 0.1f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, left, shakeSpeed * Time.deltaTime);
                yield return null;
            }
        }

        // Return to original rotation
        while (Quaternion.Angle(transform.rotation, origin) > 0.05f)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, origin, shakeSpeed * Time.deltaTime);
            yield return null;
        }

        transform.rotation = origin;
        _isShaking = false;
    }
}
