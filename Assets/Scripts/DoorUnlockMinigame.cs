using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DoorUnlockMinigame : MonoBehaviour
{
    [Header("Door Reference")]
    [SerializeField] private Door door;
    [SerializeField] private float interactRange = 3f;

    [Header("Minigame Settings")]
    [SerializeField] private int   totalScrews           = 4;
    [SerializeField] private float unscrewDuration       = 10f;
    [SerializeField] private float alignTolerance        = 15f;
    [SerializeField] private float baseSpinSpeed         = 90f;
    [SerializeField] private float spinSpeedIncrement    = 25f;
    [SerializeField] private int   totalWires            = 3;
    [SerializeField] private float randomAlignMinTime    = 2f;
    [SerializeField] private float randomAlignMaxTime    = 6f;
    [Range(0f, 1f)]
    [SerializeField] private float alignInterruptChance  = 0.35f; // 0 = never, 1 = always

    [Header("Audio")]
    [SerializeField] private AudioClip unscrewLoopClip;
    [SerializeField] private AudioClip dropClip;
    [SerializeField] private AudioClip alignSuccessClip;
    [SerializeField] private AudioClip wireCutClip;
    [SerializeField] private AudioClip electricSparkClip;
    private AudioSource _audio;

    [Header("UI - Approach Prompt")]
    [SerializeField] private TextMeshProUGUI approachPrompt;

    [Header("UI - Root")]
    [SerializeField] private GameObject minigamePanel;

    [Header("UI - Screws")]
    [SerializeField] private Image[] screwIcons;
    [SerializeField] private Image   progressFill;

    [Header("UI - Align Circle")]
    [SerializeField] private GameObject       alignPanel;
    [SerializeField] private RectTransform    needlePivot;
    [SerializeField] private RectTransform    targetBoxPivot;
    [SerializeField] private Image            targetBoxImage;   // the arc/ring slice image
    [SerializeField] private TextMeshProUGUI  alignHintText;

    [Header("UI - Wires")]
    [SerializeField] private Image[] wireIcons;

    [Header("UI - Status")]
    [SerializeField] private TextMeshProUGUI statusText;

    private enum Phase { Idle, Unscrewing, AlignScrew, WireReady, AlignWire, Done }
    private Phase _phase = Phase.Idle;

    private int   _screwsDone          = 0;
    private int   _wiresDone           = 0;
    private float _unscrewT            = 0f;
    private int   _challengeNum        = 0;
    private float _targetAngle         = 0f;
    private bool  _inRange             = false;
    private float _nextAlignInterruptT = 0f;
    private bool  _interruptPending    = false;
    private bool  _mandatoryAlign      = false;  // true = bar just filled, must pass to advance screw

    // True while the align circle is visible — read by the player controller to suppress jumping
    public static bool IsActive { get; private set; }

    // True once the minigame has been completed (door unlocked)
    public bool IsCompleted => _phase == Phase.Done;

    private static readonly Color ColDone    = new Color(1f, 1f, 1f, 0.3f);
    private static readonly Color ColPending = Color.white;

    private void Awake()
    {
        _audio = gameObject.AddComponent<AudioSource>();
        if (door == null)
            door = GetComponentInParent<Door>();
    }

    private void Start()
    {
        if (minigamePanel)  minigamePanel.SetActive(false);
        if (alignPanel)     alignPanel.SetActive(false);
        if (approachPrompt) approachPrompt.gameObject.SetActive(false);

        // Configure target box as a curved arc slice matching the ring
        if (targetBoxImage != null)
        {
            targetBoxImage.type          = Image.Type.Filled;
            targetBoxImage.fillMethod    = Image.FillMethod.Radial360;
            targetBoxImage.fillOrigin    = 2; // 2 = Top for Radial360
            targetBoxImage.fillClockwise = true;
            targetBoxImage.fillAmount    = (alignTolerance * 2f) / 360f;
        }

        RefreshScrewIcons();
        RefreshWireIcons();
    }

    private void Update()
    {
        if (_phase == Phase.Done) return;

        _inRange = IsPlayerLookingAtDoor();

        if (approachPrompt != null)
        {
            bool showApproach = _inRange && _phase == Phase.Idle;
            if (approachPrompt.gameObject.activeSelf != showApproach)
                approachPrompt.gameObject.SetActive(showApproach);
        }

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

    private void StartUnscrewing()
    {
        _phase = Phase.Unscrewing;
        if (minigamePanel) minigamePanel.SetActive(true);
        if (alignPanel)    alignPanel.SetActive(false);
        ScheduleAlignInterrupt();
        SetStatus("Hold [E] to unscrew (" + (_screwsDone + 1) + "/" + totalScrews + ")...");
    }

    private void ScheduleAlignInterrupt()
    {
        if (Random.value > alignInterruptChance)
        {
            _interruptPending = false;
            return;
        }
        float t = Random.Range(randomAlignMinTime, Mathf.Min(randomAlignMaxTime, unscrewDuration - 0.5f));
        _nextAlignInterruptT = t / unscrewDuration;
        _interruptPending    = true;
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
        _unscrewT  = Mathf.Min(_unscrewT, 1f);
        if (progressFill) progressFill.fillAmount = _unscrewT;

        if (_interruptPending && _unscrewT >= _nextAlignInterruptT)
        {
            _interruptPending = false;
            StopLoop();
            PrepareAlignCircle("Hold the screwdriver! Press [SPACE]");
            _phase = Phase.AlignScrew;
            return;
        }

        if (_unscrewT >= 1f)
        {
            StopLoop();
            _interruptPending = false;
            _unscrewT = 1f;
            // Always trigger a mandatory align at the end of each screw
            _mandatoryAlign = true;
            PrepareAlignCircle("Tighten the screw! Press [SPACE]");
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
        if (needlePivot) needlePivot.localEulerAngles = Vector3.zero;

        // Arc fills clockwise, so pivot must start alignTolerance AHEAD of target
        // so the arc sweeps through [_targetAngle+tolerance ... _targetAngle-tolerance]
        if (targetBoxPivot)
            targetBoxPivot.localEulerAngles = new Vector3(0f, 0f, _targetAngle + alignTolerance);

        if (alignHintText)  alignHintText.text = hint;
        if (alignPanel)     alignPanel.SetActive(true);
        IsActive = true;
    }

    private void TickAlign()
    {
        float speed = baseSpinSpeed + spinSpeedIncrement * (_challengeNum - 1);
        if (needlePivot) needlePivot.Rotate(0f, 0f, speed * Time.deltaTime);

        if (!Input.GetKeyDown(KeyCode.Space)) return;

        float needleAngle = needlePivot ? needlePivot.localEulerAngles.z : 0f;
        float diff        = Mathf.Abs(Mathf.DeltaAngle(needleAngle, _targetAngle));
        bool  hit         = diff <= alignTolerance;

        if (_phase == Phase.AlignScrew)
        {
            if (hit)
            {
                PlayOneShot(alignSuccessClip);
                if (alignPanel) alignPanel.SetActive(false);
                IsActive = false;

                if (_mandatoryAlign)
                {
                    // Bar was full — advance screw
                    _mandatoryAlign = false;
                    _screwsDone++;
                    _unscrewT = 0f;
                    if (progressFill) progressFill.fillAmount = 0f;
                    RefreshScrewIcons();

                    if (_screwsDone >= totalScrews)
                        EnterWirePhase();
                    else
                        StartUnscrewing();
                }
                else
                {
                    // Random interrupt — resume filling
                    ScheduleAlignInterrupt();
                    _phase = Phase.Unscrewing;
                    SetStatus("Hold [E] to unscrew (" + (_screwsDone + 1) + "/" + totalScrews + ")...");
                }
            }
            else
            {
                PlayOneShot(dropClip);
                if (alignPanel) alignPanel.SetActive(false);
                IsActive = false;
                _mandatoryAlign = false;
                _unscrewT = 0f;
                if (progressFill) progressFill.fillAmount = 0f;
                _phase = Phase.Idle;
                SetStatus("Dropped! Hold [E] to try again...");
            }
        }
        else
        {
            if (hit)
            {
                PlayOneShot(wireCutClip);
                if (alignPanel) alignPanel.SetActive(false);
                IsActive = false;
                _wiresDone++;
                RefreshWireIcons();

                if (_wiresDone >= totalWires)
                    CompleteDoor();
                else
                    EnterWirePhase();
            }
            else
            {
                PlayOneShot(electricSparkClip);
                _targetAngle = Random.Range(0f, 360f);
                if (targetBoxPivot) targetBoxPivot.localEulerAngles = new Vector3(0f, 0f, _targetAngle + alignTolerance);
                if (needlePivot)    needlePivot.localEulerAngles    = Vector3.zero;
            }
        }
    }

    private void EnterWirePhase()
    {
        _phase = Phase.WireReady;
        if (alignPanel) alignPanel.SetActive(false);
        SetStatus("Press [E] to cut wire " + (_wiresDone + 1) + "/" + totalWires + "...");
    }

    private void CompleteDoor()
    {
        _phase = Phase.Done;
        door.SetLocked(false);
        if (approachPrompt) approachPrompt.gameObject.SetActive(false);
        SetStatus("Door unlocked!");
        StartCoroutine(ClosePanelAfter(1.5f));
    }

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
        if (minigamePanel) minigamePanel.SetActive(false);
        if (alignPanel)    alignPanel.SetActive(false);
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
        if (minigamePanel) minigamePanel.SetActive(false);
        if (alignPanel)    alignPanel.SetActive(false);
    }
}
