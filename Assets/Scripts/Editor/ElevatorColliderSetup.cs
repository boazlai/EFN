using UnityEngine;
using UnityEditor;

/// <summary>
/// One-time tool: adds 5 BoxColliders to the Elevator (floor, ceiling,
/// left wall, right wall, back wall) leaving the front door side open.
/// Run via menu: Tools > Setup Elevator Colliders
/// </summary>
public class ElevatorColliderSetup : EditorWindow
{
    [MenuItem("Tools/Setup Elevator Colliders")]
    static void SetupColliders()
    {
        GameObject elevator = GameObject.Find("Elevator");
        if (elevator == null)
        {
            EditorUtility.DisplayDialog("Error", "No GameObject named 'Elevator' found in the scene.", "OK");
            return;
        }

        // Remove any previously generated collider container
        Transform existing = elevator.transform.Find("_Colliders");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        // Calculate bounds from all child renderers (in elevator local space)
        Renderer[] renderers = elevator.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", "No Renderers found under Elevator.", "OK");
            return;
        }

        // Accumulate bounds in world space, then convert to local
        Bounds worldBounds = renderers[0].bounds;
        foreach (var r in renderers)
            worldBounds.Encapsulate(r.bounds);

        // Convert world bounds center to elevator local space
        Vector3 localCenter = elevator.transform.InverseTransformPoint(worldBounds.center);
        Vector3 localSize   = elevator.transform.InverseTransformVector(worldBounds.size);
        localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));

        float thickness = 0.1f;

        // Container object so colliders are easy to find/remove
        GameObject container = new GameObject("_Colliders");
        Undo.RegisterCreatedObjectUndo(container, "Setup Elevator Colliders");
        container.transform.SetParent(elevator.transform, false);

        float halfX = localSize.x / 2f;
        float halfY = localSize.y / 2f;
        float halfZ = localSize.z / 2f;

        // Floor
        AddBox(container, "Floor",
            new Vector3(localCenter.x, localCenter.y - halfY + thickness / 2f, localCenter.z),
            new Vector3(localSize.x, thickness, localSize.z));

        // Ceiling
        AddBox(container, "Ceiling",
            new Vector3(localCenter.x, localCenter.y + halfY - thickness / 2f, localCenter.z),
            new Vector3(localSize.x, thickness, localSize.z));

        // Left wall
        AddBox(container, "WallLeft",
            new Vector3(localCenter.x - halfX + thickness / 2f, localCenter.y, localCenter.z),
            new Vector3(thickness, localSize.y, localSize.z));

        // Right wall
        AddBox(container, "WallRight",
            new Vector3(localCenter.x + halfX - thickness / 2f, localCenter.y, localCenter.z),
            new Vector3(thickness, localSize.y, localSize.z));

        // Back wall (positive Z direction — change to negative if door faces wrong way)
        AddBox(container, "WallBack",
            new Vector3(localCenter.x, localCenter.y, localCenter.z + halfZ - thickness / 2f),
            new Vector3(localSize.x, localSize.y, thickness));

        // Front is intentionally LEFT OPEN for the door entrance

        EditorUtility.DisplayDialog("Done",
            $"Added 5 box colliders to Elevator.\n\nBounds: {localSize.x:F2} x {localSize.y:F2} x {localSize.z:F2}\n\nIf the door faces the wrong wall, select the back wall collider and swap it to the -Z side, or re-run after rotating the elevator.",
            "OK");

        Selection.activeGameObject = container;
        EditorGUIUtility.PingObject(container);
    }

    static void AddBox(GameObject parent, string name, Vector3 localPos, Vector3 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.size = size;
    }
}
