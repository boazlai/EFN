using UnityEngine;
using TMPro;

public class DoorInteraction : MonoBehaviour
{
    [Header("Interaction Settings")]
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private LayerMask doorLayer;

    [Header("UI (Optional)")]
    [SerializeField] private TextMeshProUGUI promptText;   // assign a TextMeshPro - Text (UI) in the Inspector, or leave empty

    private Camera _cam;

    private void Start()
    {
        _cam = Camera.main;

        if (promptText != null)
            promptText.gameObject.SetActive(false);
    }

    private void Update()
    {
        IInteractable interactable = GetInteractableInSight();

        // Show / hide prompt
        if (promptText != null)
        {
            string prompt = interactable?.GetPrompt();
            bool show = !string.IsNullOrEmpty(prompt);
            if (promptText.gameObject.activeSelf != show)
                promptText.gameObject.SetActive(show);

            if (show)
                promptText.text = prompt;
        }

        // Interact on E
        if (interactable != null && Input.GetKeyDown(KeyCode.E))
            interactable.Interact();
    }

    private IInteractable GetInteractableInSight()
    {
        Ray ray = new Ray(_cam.transform.position, _cam.transform.forward);

        // If doorLayer is not configured (value 0 = Nothing), fall back to all layers
        LayerMask mask = doorLayer.value == 0 ? Physics.DefaultRaycastLayers : doorLayer;

        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, mask, QueryTriggerInteraction.Ignore))
        {
            IInteractable found = hit.collider.GetComponentInParent<IInteractable>();
            // Skip doors whose unlock minigame is still in progress — that script handles them
            var mb = found as MonoBehaviour;
            var minigame = mb != null ? mb.GetComponent<DoorUnlockMinigame>() : null;
            if (minigame != null && !minigame.IsCompleted)
                return null;
            return found;
        }

        return null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_cam == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(_cam.transform.position, _cam.transform.forward * interactRange);
    }
#endif
}
