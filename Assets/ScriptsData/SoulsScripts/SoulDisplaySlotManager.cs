using UnityEngine;

/// <summary>
/// Defines a grid of soul display slots.
/// Provides slot positions to SoulsOnBoatDisplayManager.
/// Grid is visible as a gizmo in the Scene view for layout testing.
/// </summary>
public class SoulDisplaySlotManager : MonoBehaviour
{
    [Header("Grid")]
    [SerializeField] private int columns = 10;
    [SerializeField] private int rows    = 1;

    [Header("Slot Size")]
    [SerializeField] private float slotWidth  = 60f;
    [SerializeField] private float slotHeight = 60f;

    [Header("Spacing")]
    [SerializeField] private float spacingX = 8f;
    [SerializeField] private float spacingY = 8f;

    [Header("Gizmos")]
    [SerializeField] private bool  showGizmos = true;
    [SerializeField] private Color gizmoColor = new Color(0f, 1f, 1f, 0.6f);

    public int Columns    => columns;
    public int Rows       => rows;
    public int TotalSlots => columns * rows;

    /// <summary>
    /// Returns the anchored position (local to this RectTransform) for a given slot index.
    /// Index 0 = top-left, increases left-to-right then top-to-bottom.
    /// </summary>
    public Vector2 GetSlotAnchoredPosition(int index)
    {
        if (index < 0) return Vector2.zero;

        int col = index % columns;
        int row = index / columns;

        float x =  col * (slotWidth  + spacingX);
        float y = -row * (slotHeight + spacingY);

        return new Vector2(x, y);
    }

    // ─────────────────────────────────────────────
    // GAME VIEW OVERLAY (OnGUI — screen space)
    // ─────────────────────────────────────────────


    // ─────────────────────────────────────────────
    // SCENE VIEW GIZMOS
    // ─────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        RectTransform rt = GetComponent<RectTransform>();

        for (int i = 0; i < TotalSlots; i++)
        {
            Vector2 local = GetSlotAnchoredPosition(i);
            Vector3 world = transform.TransformPoint(new Vector3(local.x, local.y, 0f));

            // Account for RectTransform pivot if present
            if (rt != null)
            {
                Vector2 pivotOffset = new Vector2(
                    rt.rect.width  * rt.pivot.x,
                    rt.rect.height * rt.pivot.y
                );
                world = transform.TransformPoint(new Vector3(
                    local.x - pivotOffset.x,
                    local.y + pivotOffset.y,
                    0f));
            }

            // Slot outline
            Gizmos.color = gizmoColor;
            DrawWireRect(world, slotWidth, slotHeight);

            // Centre dot
            Gizmos.color = Color.white;
            Gizmos.DrawSphere(world, Mathf.Min(slotWidth, slotHeight) * 0.06f);
        }
    }

    private void DrawWireRect(Vector3 centre, float w, float h)
    {
        float hw = w * 0.5f;
        float hh = h * 0.5f;

        Vector3 tl = centre + transform.TransformDirection(new Vector3(-hw,  hh, 0f));
        Vector3 tr = centre + transform.TransformDirection(new Vector3( hw,  hh, 0f));
        Vector3 br = centre + transform.TransformDirection(new Vector3( hw, -hh, 0f));
        Vector3 bl = centre + transform.TransformDirection(new Vector3(-hw, -hh, 0f));

        Gizmos.DrawLine(tl, tr);
        Gizmos.DrawLine(tr, br);
        Gizmos.DrawLine(br, bl);
        Gizmos.DrawLine(bl, tl);
    }
}
