using System.Collections;
using UnityEngine;

public class Door : MonoBehaviour, IInteractable
{
    [Header("Door Settings")]
    [SerializeField] private float openAngle = 90f;
    [SerializeField] private float openSpeed = 3f;
    [SerializeField] private bool openAwayFromInteractor = true;
    [SerializeField] private bool invertAwayDirection;
    [SerializeField] private string openPrompt = "Press [E] to open";
    [SerializeField] private string closePrompt = "Press [E] to close";

    [Header("Jeffery Override")]
    [SerializeField] private float jefferyOpenSpeed = 4.5f;
    [SerializeField] private AudioClip jefferyOpenSound;

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
    private Quaternion _targetRotation;
    private bool _isOpen = false;
    private float _currentMoveSpeed;

    public bool IsOpen => _isOpen;
    public bool IsLocked => isLocked;

    private void Start()
    {
        _closedRotation = transform.rotation;
        _targetRotation = _closedRotation;
        _currentMoveSpeed = openSpeed;

        // Auto-add an AudioSource if none is assigned
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    private void Update()
    {
        transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, Time.deltaTime * _currentMoveSpeed);
    }

    public void Interact()
    {
        InteractFrom(null, false);
    }

    public void InteractFrom(Transform opener, bool openedByJeffery)
    {
        if (isLocked)
        {
            PlayClip(lockedSound);
            if (!_isShaking)
                StartCoroutine(ShakeDoor());
            return;
        }

        if (_isOpen)
        {
            _isOpen = false;
            _targetRotation = _closedRotation;
            _currentMoveSpeed = openSpeed;
            PlayClip(closeSound);
            return;
        }

        _isOpen = true;
        _targetRotation = GetOpenRotation(opener);
        _currentMoveSpeed = openedByJeffery ? Mathf.Max(0.01f, jefferyOpenSpeed) : openSpeed;

        PlayClip(openedByJeffery && jefferyOpenSound != null ? jefferyOpenSound : openSound);
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

    private Quaternion GetOpenRotation(Transform opener)
    {
        float signedAngle = openAngle;

        if (openAwayFromInteractor && opener != null)
        {
            Vector3 openerLocalPosition = transform.InverseTransformPoint(opener.position);
            float directionSign = openerLocalPosition.z >= 0f ? -1f : 1f;

            if (invertAwayDirection)
                directionSign *= -1f;

            signedAngle *= directionSign;
        }

        return Quaternion.Euler(_closedRotation.eulerAngles + new Vector3(0f, signedAngle, 0f));
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
