using UnityEngine;

public class VirtualCursorUILine : MonoBehaviour
{
    public VirtualCursor cursor;
    public RectTransform uiLine;    // the 1-pixel UI image
    public Canvas canvas;

    void Update()
    {
        if (cursor == null || uiLine == null || canvas == null)
            return;

        // Convert screen X to canvas local position
        Vector2 localPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            new Vector2(cursor.Position.x, 0),
            canvas.worldCamera,
            out localPos
        );

        // Set line's X position
        uiLine.anchoredPosition = new Vector2(localPos.x, 0);
    }
}
