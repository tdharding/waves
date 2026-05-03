using UnityEngine;

public class LinkIdentityLabel : MonoBehaviour
{
    [Header("Link Identity")]
    [Tooltip("The Grid Index (0-1023). Used for map positioning and 'Caught' status.")]
    public int linkID = -1;

    [Header("Soul Identity")]
    [Tooltip("The Video ID (1-100). Used by the Video Player to find the SoulData asset.")]
    public int soulDataIdentity = -1; // THE NEW PASSPORT FIELD

    [Header("Editor Naming")]
    [SerializeField] bool renameGameObject = true;
    [SerializeField] string labelPrefix = "Link";

    void Awake()
    {
        ApplyName();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        ApplyName();
    }
#endif

    // --------------------------------------------------
    // PUBLIC API
    // --------------------------------------------------
    public void SetLabel(int id, string prefix)
    {
        linkID = id;
        labelPrefix = prefix;
        ApplyName();
    }

    void ApplyName()
    {
        if (!renameGameObject || linkID < 0)
            return;

        // Updated to show both IDs in the hierarchy for easier debugging
        string identitySuffix = soulDataIdentity > 0 ? $" ID:{soulDataIdentity}" : "";
        gameObject.name = $"{labelPrefix} [{linkID}]{identitySuffix}";
    }
}