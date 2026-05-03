using System.Collections.Generic;
using UnityEngine;

public class SoulShoalController : MonoBehaviour
{
    [Header("Refs")]
    public GameObject boat;
    public GameObject fishContainer;

    [Header("Distances")]
    public float fishingDistance = 8f;
    public float soundFalloffDistance = 30f;

    [Header("Audio")]
    public AudioSource proximitySound;
    public float maxVolume = 0.9f;
    public float fadeSpeed = 2f;

    public bool IsActive { get; private set; }
    public bool CanFish { get; private set; }

    private bool soundPlaying = false;

    private List<Transform> fishList = new List<Transform>();
    private int remainingFish = 0;

    // ---------------------------------------------------------
    void Start()
    {
        RegisterAllFish();
    }

    /// <summary>
    /// Set the SoulBoat reference directly. Called from LevelDataController after SpawnSoulBoat().
    /// </summary>
    public void SetSoulBoat(Transform soulBoat)
    {
        boat = soulBoat != null ? soulBoat.gameObject : null;
    }

    bool EnsureBoat()
    {
        return boat != null;
    }

    // ---------------------------------------------------------
    void RegisterAllFish()
    {
        fishList.Clear();
        AddFishRecursive(fishContainer.transform);

        remainingFish = fishList.Count;

        if (remainingFish == 0)
            Debug.LogWarning($"{name}: Shoal contains NO fish objects!");
    }

    void AddFishRecursive(Transform root)
    {
        foreach (Transform child in root)
        {
            if (child.CompareTag("Fish"))
                fishList.Add(child);

            AddFishRecursive(child);
        }
    }

    // ---------------------------------------------------------
    void Update()
    {
        if (!EnsureBoat())
            return;

        int alive = 0;
        for (int i = 0; i < fishList.Count; i++)
        {
            if (fishList[i] != null)
                alive++;
        }

        remainingFish = alive;
        bool shoalEmpty = (remainingFish == 0);

        float d = Vector3.Distance(boat.transform.position, transform.position);

        // ---------------------------------------------
        // Shoal Active (distance-free)
        // ---------------------------------------------
        IsActive = !shoalEmpty;
        SetFishVisuals(IsActive);

        // ---------------------------------------------
        // Fishing Distance
        // ---------------------------------------------
        CanFish = !shoalEmpty && d <= fishingDistance;

        // ---------------------------------------------
        // Proximity Audio
        // ---------------------------------------------
        HandleAudio(shoalEmpty, d);
    }

    // ---------------------------------------------------------
    void SetFishVisuals(bool visible)
    {
        if (fishContainer == null)
            return;

        Renderer[] rends = fishContainer.GetComponentsInChildren<Renderer>(true);

        foreach (var r in rends)
            r.enabled = visible;
    }

    // ---------------------------------------------------------
    void HandleAudio(bool shoalEmpty, float distance)
    {
        if (proximitySound == null)
            return;

        if (shoalEmpty)
        {
            proximitySound.volume = Mathf.MoveTowards(
                proximitySound.volume,
                0f,
                Time.deltaTime * fadeSpeed
            );

            if (proximitySound.volume <= 0.01f && soundPlaying)
            {
                proximitySound.Stop();
                soundPlaying = false;
            }

            return;
        }

        float targetVolume = Mathf.Lerp(
            0f,
            maxVolume,
            Mathf.InverseLerp(soundFalloffDistance, 0f, distance)
        );

        if (!soundPlaying && targetVolume > 0.01f)
        {
            proximitySound.Play();
            soundPlaying = true;
        }

        proximitySound.volume = Mathf.MoveTowards(
            proximitySound.volume,
            targetVolume,
            Time.deltaTime * fadeSpeed
        );

        if (soundPlaying && proximitySound.volume <= 0.01f)
        {
            proximitySound.Stop();
            soundPlaying = false;
        }
    }

    // ---------------------------------------------------------
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, fishingDistance);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, soundFalloffDistance);
    }
}
