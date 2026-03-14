using UnityEngine;
using UnityEngine.AI;

public class PrisonDoorJefferyChaseTrigger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Door monitoredDoor;
    [SerializeField] private Door companionDoor;
    [SerializeField] private NavMeshAgent jefferyAgent;
    [SerializeField] private Transform playerTarget;
    [SerializeField] private Animator jefferyAnimator;
    [SerializeField] private AudioSource chaseAudioSource;
    [SerializeField] private AudioClip chaseMoveLoop;

    [Header("Fallback Auto-Find")]
    [SerializeField] private string jefferyObjectName = "Jeffery";
    [SerializeField] private string playerTag = "Player";

    [Header("Chase Settings")]
    [SerializeField] private bool triggerOnlyOnce = true;
    [SerializeField] private float repathInterval = 0.2f;
    [SerializeField] private float navMeshSnapDistance = 5f;

    [Header("Door Obstacle Settings")]
    [SerializeField] private float doorCheckInterval = 0.2f;
    [SerializeField] private float doorCheckDistance = 1.5f;
    [SerializeField] private float doorCheckRadius = 0.2f;
    [SerializeField] private float doorCheckHeight = 1f;
    [SerializeField] private LayerMask doorBlockMask = ~0;

    [Header("Facing Settings")]
    [SerializeField] private float faceTurnSpeed = 10f;
    [SerializeField] private bool invertFacing = false;

    [Header("Animation Settings")]
    [SerializeField] private string speedFloatParameter = "Speed";
    [SerializeField] private string movingBoolParameter = "IsMoving";
    [SerializeField] private float animationDampTime = 0.1f;
    [SerializeField] private float movementThreshold = 0.03f;

    [Header("Audio Settings")]
    [SerializeField] private float movingAudioThreshold = 0.05f;

    private bool _hasTriggered;
    private bool _isChasing;
    private float _nextRepathTime;
    private bool _openedCompanionDoor;
    private int _speedHash;
    private int _movingHash;
    private bool _hasSpeedParameter;
    private bool _hasMovingParameter;
    private float _nextDoorCheckTime;

    private void Awake()
    {
        if (monitoredDoor == null)
            monitoredDoor = GetComponent<Door>();

        if (jefferyAgent == null)
        {
            GameObject jeffery = GameObject.Find(jefferyObjectName);
            if (jeffery != null)
                jefferyAgent = jeffery.GetComponent<NavMeshAgent>();
        }

        if (jefferyAnimator == null && jefferyAgent != null)
            jefferyAnimator = jefferyAgent.GetComponentInChildren<Animator>();

        if (chaseAudioSource == null && jefferyAgent != null)
            chaseAudioSource = jefferyAgent.GetComponent<AudioSource>();

        if (chaseAudioSource == null && jefferyAgent != null)
            chaseAudioSource = jefferyAgent.gameObject.AddComponent<AudioSource>();

        if (chaseAudioSource != null)
        {
            chaseAudioSource.playOnAwake = false;
            chaseAudioSource.loop = true;
            chaseAudioSource.spatialBlend = 1f;
            chaseAudioSource.clip = chaseMoveLoop;
        }

        if (playerTarget == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
                playerTarget = player.transform;
        }

        CacheAnimationParameters();
    }

    private void Update()
    {
        if (!_hasTriggered && monitoredDoor != null && monitoredDoor.IsOpen)
            StartChase();

        if (_isChasing)
        {
            UpdateChaseDestination();
            FacePlayer();
            TryOpenBlockingDoor();
        }

        UpdateAnimatorFromAgent();
        UpdateChaseAudio();
    }

    private void StartChase()
    {
        _hasTriggered = true;

        OpenCompanionDoorIfNeeded();

        if (jefferyAgent == null || playerTarget == null)
        {
            Debug.LogWarning("PrisonDoorJefferyChaseTrigger: Missing Jeffery NavMeshAgent or Player target.", this);
            return;
        }

        if (!TryEnsureAgentOnNavMesh())
        {
            Debug.LogWarning("PrisonDoorJefferyChaseTrigger: Jeffery is not on a valid NavMesh. Place Jeffery on blue NavMesh or increase Nav Mesh Snap Distance.", this);
            _isChasing = false;
            return;
        }

        jefferyAgent.isStopped = false;
        jefferyAgent.updatePosition = true;
        jefferyAgent.updateRotation = false;
        _isChasing = true;
        _nextRepathTime = 0f;

        if (!triggerOnlyOnce)
            _hasTriggered = false;
    }

    private void UpdateChaseDestination()
    {
        if (Time.time < _nextRepathTime)
            return;

        if (!TryEnsureAgentOnNavMesh())
            return;

        _nextRepathTime = Time.time + Mathf.Max(0.05f, repathInterval);

        Vector3 destination = playerTarget.position;
        if (TryGetNearestNavMeshPoint(playerTarget.position, out NavMeshHit hit))
            destination = hit.position;

        jefferyAgent.SetDestination(destination);
    }

    private void FacePlayer()
    {
        if (jefferyAgent == null || playerTarget == null)
            return;

        Vector3 toPlayer = playerTarget.position - jefferyAgent.transform.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude < 0.0001f)
            return;

        if (invertFacing)
            toPlayer = -toPlayer;

        Quaternion targetRotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        jefferyAgent.transform.rotation = Quaternion.Slerp(
            jefferyAgent.transform.rotation,
            targetRotation,
            Mathf.Max(0.1f, faceTurnSpeed) * Time.deltaTime);
    }

    private void OpenCompanionDoorIfNeeded()
    {
        if (_openedCompanionDoor)
            return;

        if (companionDoor != null && !companionDoor.IsOpen)
            companionDoor.InteractFrom(jefferyAgent != null ? jefferyAgent.transform : transform, true);

        _openedCompanionDoor = true;
    }

    private bool TryEnsureAgentOnNavMesh()
    {
        if (jefferyAgent == null)
            return false;

        if (jefferyAgent.isOnNavMesh)
            return true;

        if (!TryGetNearestNavMeshPoint(jefferyAgent.transform.position, out NavMeshHit hit))
            return false;

        return jefferyAgent.Warp(hit.position);
    }

    private bool TryGetNearestNavMeshPoint(Vector3 source, out NavMeshHit hit)
    {
        float maxDistance = Mathf.Max(0.1f, navMeshSnapDistance);
        return NavMesh.SamplePosition(source, out hit, maxDistance, NavMesh.AllAreas);
    }

    private void CacheAnimationParameters()
    {
        if (jefferyAnimator == null || jefferyAnimator.runtimeAnimatorController == null)
            return;

        if (!string.IsNullOrWhiteSpace(speedFloatParameter))
        {
            _speedHash = Animator.StringToHash(speedFloatParameter);
            _hasSpeedParameter = HasAnimatorParameter(jefferyAnimator, speedFloatParameter, AnimatorControllerParameterType.Float);
        }

        if (!string.IsNullOrWhiteSpace(movingBoolParameter))
        {
            _movingHash = Animator.StringToHash(movingBoolParameter);
            _hasMovingParameter = HasAnimatorParameter(jefferyAnimator, movingBoolParameter, AnimatorControllerParameterType.Bool);
        }
    }

    private void UpdateAnimatorFromAgent()
    {
        if (jefferyAnimator == null || jefferyAgent == null)
            return;

        float planarSpeed = new Vector3(jefferyAgent.velocity.x, 0f, jefferyAgent.velocity.z).magnitude;
        bool isMoving = planarSpeed > movementThreshold;

        if (_hasSpeedParameter)
            jefferyAnimator.SetFloat(_speedHash, planarSpeed, animationDampTime, Time.deltaTime);

        if (_hasMovingParameter)
            jefferyAnimator.SetBool(_movingHash, isMoving);
    }

    private void UpdateChaseAudio()
    {
        if (chaseAudioSource == null)
            return;

        if (chaseAudioSource.clip != chaseMoveLoop)
            chaseAudioSource.clip = chaseMoveLoop;

        bool canPlay = _isChasing && chaseMoveLoop != null;
        if (!canPlay)
        {
            if (chaseAudioSource.isPlaying)
                chaseAudioSource.Stop();
            return;
        }

        float speed = jefferyAgent != null ? jefferyAgent.velocity.magnitude : 0f;
        bool isMoving = speed > Mathf.Max(0.001f, movingAudioThreshold);

        if (isMoving)
        {
            if (!chaseAudioSource.isPlaying)
                chaseAudioSource.Play();
        }
        else if (chaseAudioSource.isPlaying)
        {
            chaseAudioSource.Stop();
        }
    }

    private void TryOpenBlockingDoor()
    {
        if (jefferyAgent == null || !jefferyAgent.isOnNavMesh)
            return;

        if (Time.time < _nextDoorCheckTime)
            return;

        _nextDoorCheckTime = Time.time + Mathf.Max(0.05f, doorCheckInterval);

        Vector3 forward = jefferyAgent.desiredVelocity;
        if (forward.sqrMagnitude < 0.01f && playerTarget != null)
            forward = playerTarget.position - jefferyAgent.transform.position;

        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
            return;
        forward.Normalize();

        Vector3 origin = jefferyAgent.transform.position + Vector3.up * Mathf.Max(0f, doorCheckHeight);

        if (!Physics.SphereCast(origin,
            Mathf.Max(0.01f, doorCheckRadius),
            forward,
            out RaycastHit hit,
            Mathf.Max(0.1f, doorCheckDistance),
            doorBlockMask,
            QueryTriggerInteraction.Ignore))
        {
            return;
        }

        Door blockingDoor = hit.collider.GetComponentInParent<Door>();
        if (blockingDoor == null)
            return;

        if (blockingDoor.IsOpen || blockingDoor.IsLocked)
            return;

        blockingDoor.InteractFrom(jefferyAgent.transform, true);
    }

    private static bool HasAnimatorParameter(Animator animator, string parameterName, AnimatorControllerParameterType type)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == type && parameter.name == parameterName)
                return true;
        }

        return false;
    }
}
