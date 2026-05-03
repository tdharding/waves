using UnityEngine;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;

public class MemoryTreePlayer : MonoBehaviour
{
    [System.Serializable]
    public class OrbConnection
    {
        public string label; 
        public GameplaySoulSlot slot; 
        public Renderer orbRenderer;
        [Tooltip("The exact name of the RenderTexture file in Resources/TreeTextures/")]
        public string resourceTextureName;
        
        [HideInInspector] public VideoPlayer player;
        [HideInInspector] public RenderTexture rt;
        [HideInInspector] public Material matInstance;
    }

    [Header("Tree Setup")]
    [SerializeField] private List<OrbConnection> connections = new List<OrbConnection>();
    
    [Header("Material Mapping")]
    [SerializeField] private string videoTextureProperty = "_VideoRenderTexture";
    [SerializeField] private string blendProperty = "_VideoBlend";
    [SerializeField] private string fresnelProperty = "_FresnalPower";

    [Header("Visual Settings")]
    [SerializeField] private float targetVideoBlend = 1.0f;
    [SerializeField] private float targetFresnel = 2.0f;
    [SerializeField] private float fadeDuration = 1.0f;

    private void Awake()
    {
        foreach (var conn in connections)
        {
            SetupOrbFromResources(conn);
        }
    }

private void SetupOrbFromResources(OrbConnection conn)
{
    if (conn.orbRenderer == null)
    {
        Debug.LogError($"[Tree] {conn.label} is missing an Orb Renderer!");
        return;
    }

    // 1. CREATE UNIQUE MATERIAL INSTANCE
    // This ensures Orb A doesn't overwrite Orb B's video
    conn.matInstance = conn.orbRenderer.material; 

    // 2. LOAD RENDER TEXTURE FROM RESOURCES
    // Path: Assets/Resources/TreeTextures/[resourceTextureName].renderTexture
    conn.rt = Resources.Load<RenderTexture>($"TreeTextures/{conn.resourceTextureName}");

    if (conn.rt == null)
    {
        Debug.LogError($"[Tree] Failed to load RT: 'Resources/TreeTextures/{conn.resourceTextureName}'");
        return;
    }

    // 3. GET PROPERTY IDs (Direct GPU Addressing)
    int texID = Shader.PropertyToID(videoTextureProperty); // Should be "_VideoRenderTexture"
    int blendID = Shader.PropertyToID(blendProperty);      // Should be "_VideoBlend"

    // 4. VERIFY PROPERTY EXISTS ON SHADER
    if (!conn.matInstance.HasProperty(texID))
    {
        Debug.LogError($"[Tree] SHADER ERROR: The material on {conn.label} does not have a REFERENCE named '{videoTextureProperty}'. Check Shader Graph Node Settings!");
    }

    // 5. THE ASSIGNMENT (The "Slam")
    conn.matInstance.SetTexture(texID, conn.rt);
    
    // --- COLOR TEST ---
    // If your orb turns BRIGHT RED, the script is successfully talking to the material.
    // If it stays black/normal, we are targeting the wrong Material Slot or Mesh.
    // conn.matInstance.color = Color.red; 
    // ------------------

    // 6. SETUP HIDDEN VIDEO PLAYER
    GameObject go = new GameObject($"VP_{conn.label}");
    go.transform.SetParent(this.transform);
    conn.player = go.AddComponent<VideoPlayer>();
    
    conn.player.renderMode = VideoRenderMode.RenderTexture;
    conn.player.targetTexture = conn.rt;
    conn.player.isLooping = true;
    conn.player.playOnAwake = false;
    conn.player.skipOnDrop = true;

    // 7. INITIALIZE SHADER STATE
    conn.matInstance.SetFloat(blendID, 0f);
    
    Debug.Log($"<color=cyan>[Tree]</color> {conn.label} Pipeline Ready: Player -> {conn.rt.name} -> {conn.matInstance.name}");
}

    public void RefreshState()
    {
        foreach (var conn in connections)
        {
            if (conn.slot == null || conn.player == null) continue;

            bool hasSoul = conn.slot.IsFilled;
            VideoClip clip = hasSoul ? VideoPlayerController.Instance?.GetClipForSoul(conn.slot.SoulIdentity) : null;

            if (clip != null)
            {
                if (conn.player.clip != clip)
                {
                    conn.player.Stop();
                    conn.player.clip = clip;
                    conn.player.Play();
                    StartCoroutine(FadeOrb(conn, targetVideoBlend, targetFresnel));
                }
            }
            else
            {
                if (conn.player.isPlaying || conn.matInstance.GetFloat(blendProperty) > 0.01f)
                {
                    StartCoroutine(FadeOrb(conn, 0f, 0f, () => conn.player.Stop()));
                }
            }
        }
    }

    private IEnumerator FadeOrb(OrbConnection conn, float targetBlend, float targetFresnel, System.Action onComplete = null)
    {
        float startBlend = conn.matInstance.GetFloat(blendProperty);
        float startFresnel = conn.matInstance.GetFloat(fresnelProperty);
        float elapsed = 0;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            conn.matInstance.SetFloat(blendProperty, Mathf.Lerp(startBlend, targetBlend, t));
            conn.matInstance.SetFloat(fresnelProperty, Mathf.Lerp(startFresnel, targetFresnel, t));
            yield return null;
        }

        conn.matInstance.SetFloat(blendProperty, targetBlend);
        conn.matInstance.SetFloat(fresnelProperty, targetFresnel);
        onComplete?.Invoke();
    }
}