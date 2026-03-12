using System.Collections;
using UnityEngine;

/// <summary>
/// Handles elevator sliding doors, call button interaction, and direction indicator.
/// Attach to the root "Elevator" GameObject.
///
/// Setup:
///  1. Attach this script to the Elevator root.
///  2. Fields will be auto-found by name at runtime, or assign them manually in the Inspector.
///  3. Press Play — the looping animation stops, Object_37 hides, Object_39 shows.
///  4. Look at the call button (Object_34) and press E to open/close the doors.
/// </summary>
public class ElevatorDoorController : MonoBehaviour, IInteractable
{
    [Header("Door Parent Transforms (slide to open)")]
    [Tooltip("Auto-found as 'LeftOutsideDoor_0' if left empty")]
    [SerializeField] private Transform leftOutsideDoor;
    [Tooltip("Auto-found as 'RightOutsideDoor_1' if left empty")]
    [SerializeField] private Transform rightOutsideDoor;
    [Tooltip("Auto-found as 'LeftInteriodDoor_8' if left empty")]
    [SerializeField] private Transform leftInsideDoor;
    [Tooltip("Auto-found as 'RightInteriorDoor_10' if left empty")]
    [SerializeField] private Transform rightInsideDoor;

    [Header("Door Mesh Objects (receive box colliders at runtime)")]
    [Tooltip("Auto-found as 'Object_4'")]
    [SerializeField] private Transform doorMeshLeftOutside;
    [Tooltip("Auto-found as 'Object_6'")]
    [SerializeField] private Transform doorMeshRightOutside;
    [Tooltip("Auto-found as 'Object_27'")]
    [SerializeField] private Transform doorMeshLeftInside;
    [Tooltip("Auto-found as 'Object_32'")]
    [SerializeField] private Transform doorMeshRightInside;

    [Header("Call Button")]
    [Tooltip("Auto-found as 'Object_34'. Gets a MeshCollider added so the raycast can hit it.")]
    [SerializeField] private Transform callButton;

    [Header("Direction Indicators")]
    [Tooltip("Object_37 = down arrow — will be hidden on Start")]
    [SerializeField] private GameObject indicatorDown;
    [Tooltip("Object_39 = up arrow — will be shown on Start")]
    [SerializeField] private GameObject indicatorUp;

    [Header("Sliding Settings")]
    [Tooltip("How far each door panel slides (metres in local space). Adjust if doors don't open fully.")]
    [SerializeField] private float slideDistance = 0.55f;
    [Tooltip("Speed the doors slide open/close.")]
    [SerializeField] private float slideSpeed = 1.5f;
    [Tooltip("Seconds before doors auto-close after opening. Set to 0 to disable.")]
    [SerializeField] private float autoCloseDelay = 5f;
    [Tooltip("Axis in the door parent's local space that the door slides along. Default X suits most models.")]
    [SerializeField] private Vector3 slideAxis = Vector3.right;

    [Header("Prompt Text")]
    [SerializeField] private string openPrompt = "Press [E] to open elevator";

    // Closed-position snapshots taken after stopping the animation
    private Vector3 _closedLeftOutside, _closedRightOutside;
    private Vector3 _closedLeftInside,  _closedRightInside;

    private bool _isOpen;
    private Coroutine _slideCoroutine;
    private Coroutine _autoCloseCoroutine;

    // -------------------------------------------------------------------------
    private void Start()
    {
        // 1. Stop the looping legacy animation so it doesn't fight our code
        var anim = GetComponent<Animation>();
        if (anim != null)
        {
            // Rewind to t=0 (closed pose) before freezing
            anim.Rewind();
            anim.Sample();
            anim.Stop();
            anim.enabled = false;
        }

        // 2. Auto-find all transforms by name if not assigned
        if (leftOutsideDoor  == null) leftOutsideDoor  = FindDeep(transform, "LeftOutsideDoor_0");
        if (rightOutsideDoor == null) rightOutsideDoor = FindDeep(transform, "RightOutsideDoor_1");
        if (leftInsideDoor   == null) leftInsideDoor   = FindDeep(transform, "LeftInteriodDoor_8");
        if (rightInsideDoor  == null) rightInsideDoor  = FindDeep(transform, "RightInteriorDoor_10");

        if (doorMeshLeftOutside  == null) doorMeshLeftOutside  = FindDeep(transform, "Object_4");
        if (doorMeshRightOutside == null) doorMeshRightOutside = FindDeep(transform, "Object_6");
        if (doorMeshLeftInside   == null) doorMeshLeftInside   = FindDeep(transform, "Object_27");
        if (doorMeshRightInside  == null) doorMeshRightInside  = FindDeep(transform, "Object_32");
        if (callButton           == null) callButton           = FindDeep(transform, "Object_34");

        if (indicatorDown == null) { var t = FindDeep(transform, "Object_37"); if (t != null) indicatorDown = t.gameObject; }
        if (indicatorUp   == null) { var t = FindDeep(transform, "Object_39"); if (t != null) indicatorUp   = t.gameObject; }

        // 3. Snapshot closed positions
        if (leftOutsideDoor  != null) _closedLeftOutside  = leftOutsideDoor.localPosition;
        if (rightOutsideDoor != null) _closedRightOutside = rightOutsideDoor.localPosition;
        if (leftInsideDoor   != null) _closedLeftInside   = leftInsideDoor.localPosition;
        if (rightInsideDoor  != null) _closedRightInside  = rightInsideDoor.localPosition;

        // 4. Add BoxColliders to door mesh panels (so player can't walk through them)
        AddBoxCollider(doorMeshLeftOutside);
        AddBoxCollider(doorMeshRightOutside);
        AddBoxCollider(doorMeshLeftInside);
        AddBoxCollider(doorMeshRightInside);

        // 5. Add collider to call button and put it on the "Door" layer so
        //    DoorInteraction's layer-filtered raycast can find it.
        if (callButton != null)
        {
            // Match the layer DoorInteraction raycasts against
            int doorLayer = LayerMask.NameToLayer("Door");
            if (doorLayer >= 0)
                callButton.gameObject.layer = doorLayer;

            if (callButton.GetComponent<Collider>() == null)
            {
                var mf = callButton.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var mc = callButton.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                }
                else
                {
                    // Fallback: box collider sized to local bounds, covers the button panel
                    callButton.gameObject.AddComponent<BoxCollider>();
                }
            }
        }

        // 6. Show "going up" indicator — hide down, show up
        if (indicatorDown != null) indicatorDown.SetActive(false);
        if (indicatorUp   != null) indicatorUp.SetActive(true);
    }

    // -------------------------------------------------------------------------
    // IInteractable — called by DoorInteraction when player looks at Object_34 and presses E
    public void Interact()
    {
        if (_isOpen) return; // E only opens — cannot close manually

        if (_slideCoroutine != null)    StopCoroutine(_slideCoroutine);
        if (_autoCloseCoroutine != null) StopCoroutine(_autoCloseCoroutine);

        _isOpen = true;
        _slideCoroutine = StartCoroutine(SlideDoors(true));

        if (autoCloseDelay > 0f)
            _autoCloseCoroutine = StartCoroutine(AutoClose());
    }

    public string GetPrompt() => _isOpen ? null : openPrompt;

    // -------------------------------------------------------------------------
    private IEnumerator SlideDoors(bool opening)
    {
        // Left doors slide in -slideAxis direction; right doors slide in +slideAxis direction
        Vector3 leftOutTarget  = opening
            ? _closedLeftOutside  - slideAxis * slideDistance
            : _closedLeftOutside;
        Vector3 rightOutTarget = opening
            ? _closedRightOutside + slideAxis * slideDistance
            : _closedRightOutside;
        Vector3 leftInTarget  = opening
            ? _closedLeftInside  - slideAxis * slideDistance
            : _closedLeftInside;
        Vector3 rightInTarget = opening
            ? _closedRightInside + slideAxis * slideDistance
            : _closedRightInside;

        while (true)
        {
            bool done = true;

            done &= MoveTowards(leftOutsideDoor,  leftOutTarget);
            done &= MoveTowards(rightOutsideDoor, rightOutTarget);
            done &= MoveTowards(leftInsideDoor,   leftInTarget);
            done &= MoveTowards(rightInsideDoor,  rightInTarget);

            if (done) yield break;
            yield return null;
        }
    }

    private bool MoveTowards(Transform t, Vector3 target)
    {
        if (t == null) return true;
        t.localPosition = Vector3.MoveTowards(t.localPosition, target, slideSpeed * Time.deltaTime);
        return Vector3.Distance(t.localPosition, target) < 0.001f;
    }

    private IEnumerator AutoClose()
    {
        yield return new WaitForSeconds(autoCloseDelay);
        if (_isOpen) Interact();
    }

    // -------------------------------------------------------------------------
    /// <summary>Adds a BoxCollider sized from the MeshFilter's local bounds.</summary>
    private static void AddBoxCollider(Transform t)
    {
        if (t == null) return;
        if (t.GetComponent<Collider>() != null) return; // already has one

        var mf = t.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        var bc = t.gameObject.AddComponent<BoxCollider>();
        bc.center = mf.sharedMesh.bounds.center;
        bc.size   = mf.sharedMesh.bounds.size;
    }

    /// <summary>Depth-first search for a child Transform by name.</summary>
    private static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            var result = FindDeep(child, name);
            if (result != null) return result;
        }
        return null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (callButton != null)
            Gizmos.DrawWireSphere(callButton.position, 0.1f);
    }
#endif
}
