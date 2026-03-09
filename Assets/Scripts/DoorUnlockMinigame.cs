using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Attach this to the same DoorHinge GameObject that has the Door component.
/// Make sure Door's "Is Locked" checkbox is ticked.
/// DoorInteraction will automatically skip this door and let this script handle it.
/// </summary>
public class DoorUnlockMinigame : MonoBehaviour
{
    // ── Door ─────────────────────────────────────────────────────────────────
    [Header("Door Reference")]
    [SerializeField] private Door door;
    [SerializeField] private float interactRange = 3f;

    // ── Game Settings ─────────────────────────────────────────────────────────
    [Header("Minigame Settings")]
    [SerializeField] private int   totalScrews        = 4;
    [SerializeField] private float unscrewDuration    = 10f;  // seconds to hold E per screw
    [SerializeField] private float alignTolerance     = 15f;  // degrees of leniency
    [SerializeField] private float baseSpinSpeed      = 90f;  // deg/sec on first challenge
    [SerializeField] private float spinSpeedIncrement = 25f;  // added per challenge (gets harder)
    [SerializeField] private int   totalWires         = 3;

    // ── Audio ─────────────────────────────────────────────────────────────────
    [Header("Audio")]
    [SerializeField] private AudioClip unscrewLoopClip;
    [SerializeField] private AudioClip dropClip;
    [SerializeField] private AudioClip alignSuccessClip;
    [SerializeField] private AudioClip wireCutClip;
    [SerializeField] private AudioClip electricSparkClip;
    private AudioSource _audio;

    // ── UI References ─────────────────────────────────────────────────────────
    [Header("UI – Root")]
    [SerializeField] private GameObject minigamePanel;   // the whole overlay

    [Header("UI – Screws")]
    [SerializeField] private Image[] screwIcons;         // 4 Image components
    [SerializeField] private Image   progressFill;       // fill Image of the progress bar

    [Header("UI – Align Circle")]
    [SerializeField] private GameObject       alignPanel;
    [SerializeField] private RectTransform    needlePivot;      // empty pivot at center, spins
    [SerializeField] private RectTransform    targetBoxPivot;   // empty pivot at center, random angle
    [SerializeField] private TextMeshProUGUI  alignHintText;

    [Header("UI – Wires")]
    [SerializeField] private Image[] wireIcons;          // 3 Image components

    [Header("UI – Status")]
    [SerializeField] private TextMeshProUGUI statusText;

    // ── State ─────────────────────────────────────────────────────────────────
    private enum Phase { Idle, Unscrewing, AlignScrew, WireReady, AlignWire, Done }
    private Phase _phase = Phase.Idle;

    private int   _screwsDone    = 0;
    private int   _wiresDone     = 0;
    private float _unscrewT      = 0f;
    private int   _challengeNum  = 0;   // cumulative challenge count (drives speed)
    private float _targetAngle   = 0f;
    private bool  _inRange       = false;

    private static readonly Color ColDone    = new Color(1f, 1f, 1f, 0.3f);
    private static readonly Color ColPending = Color.white;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _audio = gameObject.AddComponent<AudioSource>();
        if (door == null)
            door = GetComponentInParent<Door>();
    }

    private void Start()
    {
        minigamePanel.SetActive(false);
        alignPanel.SetActive(false);
        RefreshScrewIcons();
        RefreshWireIcons();
    }

    private void Update()
    {
        _inRange = IsPlayerLookingAtDoor();

        switch (_phase)
        {
            case Phase.Idle:
                if (_inRange && Input.GetKeyDown(KeyCode.E))
                    StartUnscrewing();
                break;

            case Phase.Unscrewing:
                TickUnscrewing();
                break;

            case Phase.AlignScrew:
            case Phase.AlignWire:
                TickAlign();
                break;

            case Phase.WireReady:
                if (_inRange && Input.GetKeyDown(KeyCode.E))
                    BeginAlignWire();
                break;
        }
    }

    // ── Phase Handlers ────────────────────────────────────────────────────────

    private void StartUnscrewing()
    {
        _phase = Phase.Unscrewing;
        minigamePanel.SetActive(true);
        alignPanel.SetActive(false);
        SetStatus("Hold [E] to unscrew (" + (_screwsDone + 1) + "/" + totalScrews + ")...");
    }

    private void TickUnscrewing()
    {
        if (!_inRange || !Input.GetKey(KeyCode.E))
        {
            StopLoop();
            if (!_inRange) ClosePanel();
            return;
        }

        PlayLoop();
        _unscrewT += Time.deltaTime / unscrewDuration;
        progressFill.fillAmount = _unscrewT;

        if (_unscrewT >= 1f)
        {
            StopLoop();
            PrepareAlignCircle("Press [SPACE] to lock the screwdriver!");
            _phase = Phase.AlignScrew;
        }
    }

    private void BeginAlignWire()
    {
        PrepareAlignCircle("Press [SPACE] to cut the wire!");
        _phase = Phase.AlignWire;
    }

    private void PrepareAlignCircle(string hint)
    {
        _challengeNum++;
        _targetAngle = Random.Range(0f, 360f);
        needlePivot.localEulerAngles    = Vector3.zero;
        targetBoxPivot.localEulerAngles = new Vector3(0f, 0f, _targetAngle);
        alignHintText.text              = hint;
        alignPanel.SetActive(true);
    }

    private void TickAlign()
    {
        // Spin the needle
        float speed = baseSpinSpeed + spinSpeedIncrement * (_challengeNum - 1);
        needlePivot.Rotate(0f, 0f, speed * Time.deltaTime);

        if (!Input.GetKeyDown(KeyCode.Space)) return;

        float needleAngle = needlePivot.localEulerAngles.z;
        float diff        = Mathf.Abs(Mathf.DeltaAngle(needleAngle, _targetAngle));
        bool  hit         = diff <= alignTolerance;

        if (_phase == Phase.AlignScrew)
        {
            if (hit)
            {
                PlayOneShot(alignSuccessClip);
                alignPanel.SetActive(false);
                _screwsDone++;
                _unscrewT = 0f;
                progressFill.fillAmount = 0f;
                RefreshScrewIcons();

                if (_screwsDone >= totalScrews)
                    EnterWirePhase();
                else
                    StartUnscrewing();
            }
            else
            {
                // Drop screwdriver — restart current screw
                PlayOneShot(dropClip);
                alignPanel.SetActive(false);
                _unscrewT = 0f;
                progressFill.fillAmount = 0f;
                _phase = Phase.Idle;
                SetStatus("Dropped! Hold [E] to try again...");
            }
        }
        else // AlignWire
        {
            if (hit)
            {
                PlayOneShot(wireCutClip);
                alignPanel.SetActive(false);
                _wiresDone++;
                RefreshWireIcons();

                if (_wiresDone >= totalWires)
                    CompleteDoor();
                else
                    EnterWirePhase();
            }
            else
            {
                // Electric spark — reshuffle the target, try same wire again
                PlayOneShot(electricSparkClip);
                _targetAngle = Random.Range(0f, 360f);
                targetBoxPivot.localEulerAngles = new Vector3(0f, 0f, _targetAngle);
                needlePivot.localEulerAngles    = Vector3.zero;
            }
        }
    }

    private void EnterWirePhase()
    {
        _phase = Phase.WireReady;
        alignPanel.SetActive(false);
        SetStatus("Press [E] to cut wire " + (_wiresDone + 1) + "/" + totalWires + "...");
    }

    private void CompleteDoor()
    {
        _phase = Phase.Done;
        door.SetLocked(false);
        SetStatus("Door unlocked!");
        StartCoroutine(ClosePanelAfter(1.5f));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsPlayerLookingAtDoor()
    {
        Camera cam = Camera.main;
        if (cam == null) return false;
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange))
            return hit.collider.GetComponentInParent<Door>() == door;
        return false;
    }

    private void ClosePanel()
    {
        _phase = Phase.Idle;
        minigamePanel.SetActive(false);
        alignPanel.SetActive(false);
        StopLoop();
    }

    private void SetStatus(string msg)
    {
        if (statusText) statusText.text = msg;
    }

    private void RefreshScrewIcons()
    {
        for (int i = 0; i < screwIcons.Length; i++)
            if (screwIcons[i]) screwIcons[i].color = i < _screwsDone ? ColDone : ColPending;
    }

    private void RefreshWireIcons()
    {
        for (int i = 0; i < wireIcons.Length; i++)
            if (wireIcons[i]) wireIcons[i].color = i < _wiresDone ? ColDone : ColPending;
    }

    private void PlayLoop()
    {
        if (unscrewLoopClip == null) return;
        if (!_audio.isPlaying || _audio.clip != unscrewLoopClip)
        {
            _audio.clip = unscrewLoopClip;
            _audio.loop = true;
            _audio.Play();
        }
    }

    private void StopLoop()
    {
        if (_audio.isPlaying && _audio.clip == unscrewLoopClip)
            _audio.Stop();
    }

    private void PlayOneShot(AudioClip clip)
    {
        if (clip == null) return;
        _audio.loop = false;
        _audio.PlayOneShot(clip);
    }

    private IEnumerator ClosePanelAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        ClosePanel();
    }
}
