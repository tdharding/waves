using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class BoatCollisionHandler : MonoBehaviour
{
    [Header("Crash Settings")]
    public float crashCooldown = 0.75f;
    public CrashSFXPlayer crashSFXPlayer;

    [Header("Soul Loss")]
    [Range(0f, 1f)]
    public float loseSoulChance = 0.5f;
    public Transform soulsParent;

    [Header("Soul Projectile")]
    [Tooltip("Prefab with SoulProjectile component — visually matches boat soul visuals.")]
    public GameObject soulProjectilePrefab;

    [Header("SFX")]
    public AudioSource soulLostSFX;

    [Header("Collision Dither Material (Renderer Feature)")]
    public Material collisionMaterial;

    [Header("Material Animation")]
    public float effectInTime  = 0.5f;
    public float effectOutTime = 0.75f;

    static readonly int FactorID = Shader.PropertyToID("_Factor");
    static readonly int OffsetID = Shader.PropertyToID("_Offset");

    readonly string[] validTags =
        { "MazeWalls", "LowHMazeWall", "TallHMazeWall", "LowSpikeTrap", "BadGuySnake", "OuterWalls" };

    private bool canCrash = true;
    private Coroutine materialRoutine;

    // ─────────────────────────────────────────────
    // AWAKE
    // ─────────────────────────────────────────────

    void Awake()
    {
        if (collisionMaterial != null)
        {
            collisionMaterial.SetFloat(FactorID, 0f);
            collisionMaterial.SetVector(OffsetID, Vector2.zero);
        }
    }

    // ─────────────────────────────────────────────
    // COLLISION
    // ─────────────────────────────────────────────

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        bool hasValidTag = false;
        foreach (string tag in validTags)
        {
            if (hit.collider.CompareTag(tag))
            {
                hasValidTag = true;
                break;
            }
        }

        if (!hasValidTag || !canCrash) return;

        crashSFXPlayer?.PlayRandomCrash();
        StartCoroutine(CrashCooldown());

        if (collisionMaterial != null)
        {
            if (materialRoutine != null) StopCoroutine(materialRoutine);
            materialRoutine = StartCoroutine(AnimateCollisionMaterial());
        }

        if (Random.value <= loseSoulChance)
            LoseRandomSoul();
    }

    // ─────────────────────────────────────────────
    // COOLDOWN
    // ─────────────────────────────────────────────

    private IEnumerator CrashCooldown()
    {
        canCrash = false;
        yield return new WaitForSeconds(crashCooldown);
        canCrash = true;
    }

    // ─────────────────────────────────────────────
    // MATERIAL FX
    // ─────────────────────────────────────────────

    private IEnumerator AnimateCollisionMaterial()
    {
        Vector2 targetOffset = new Vector2(0f, -0.04f);
        float t = 0f;

        while (t < effectInTime)
        {
            t += Time.deltaTime;
            float n = t / effectInTime;
            collisionMaterial.SetFloat(FactorID, Mathf.Lerp(0f, 0.5f, n));
            collisionMaterial.SetVector(OffsetID, Vector2.Lerp(Vector2.zero, targetOffset, n));
            yield return null;
        }

        t = 0f;
        while (t < effectOutTime)
        {
            t += Time.deltaTime;
            float n = t / effectOutTime;
            collisionMaterial.SetFloat(FactorID, Mathf.Lerp(0.5f, 0f, n));
            collisionMaterial.SetVector(OffsetID, Vector2.Lerp(targetOffset, Vector2.zero, n));
            yield return null;
        }

        collisionMaterial.SetFloat(FactorID, 0f);
        collisionMaterial.SetVector(OffsetID, Vector2.zero);
    }

    // ─────────────────────────────────────────────
    // SOUL LOSS
    // ─────────────────────────────────────────────

    private void LoseRandomSoul()
    {
        if (LevelSoulTracker.Instance == null || LevelSoulTracker.Instance.SoulsOnBoat <= 0)
            return;

        // Get full identity list
        List<int> identities = LevelSoulTracker.Instance.GetAllCaughtIdentities();
        if (identities.Count == 0) return;

        // Pick a random soul to drop
        int randomIndex        = Random.Range(0, identities.Count);
        int soulIdentityToDrop = identities[randomIndex];

        // Retrieve the linkID for this soul so the full passport travels with it
        int linkIDToDrop = LevelSoulTracker.Instance.GetLinkIDForIdentity(soulIdentityToDrop);

        // Deactivate a visual
        Transform soulVisual = GetRandomActiveSoul();
        if (soulVisual == null) return;

        soulLostSFX?.Play();
        Vector3 soulWorldPos = soulVisual.position;
        soulVisual.gameObject.SetActive(false);

        // Remove from tracker
        LevelSoulTracker.Instance.RemoveTemporarySoul(soulIdentityToDrop);

        // Launch projectile with full passport
        if (soulProjectilePrefab != null)
        {
            GameObject   proj = Instantiate(soulProjectilePrefab, soulWorldPos, Quaternion.identity, null);
            SoulProjectile sp = proj.GetComponent<SoulProjectile>();

            if (sp != null)
                sp.Launch(soulWorldPos, transform, soulIdentityToDrop, linkIDToDrop);
        }
    }

    // ─────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────

    private Transform GetRandomActiveSoul()
    {
        var list = new List<Transform>();

        for (int i = 0; i < soulsParent.childCount; i++)
        {
            Transform s = soulsParent.GetChild(i);
            if (s.gameObject.activeSelf)
                list.Add(s);
        }

        return list.Count == 0 ? null : list[Random.Range(0, list.Count)];
    }
}