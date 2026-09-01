using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

// The angel who travels with you. She flies high over the boat, comes down onto rocks as you reach
// them, and can be talked to once she is settled.
//
//   flying ....... She holds a position above the boat, smoothed so she trails as you sail and
//                  settles as you slow. AngelFlying1.
//   swooping ..... The boat has entered a perch's radius, so she leaves the flight and curves down
//                  onto the tip of that rock. AngelLanding, tweened to at the start of the descent.
//   perched ...... Stood on the point, turned to face the boat. She stays for as long as you are
//                  inside that rock's perch radius — there is no clock on it. AngelPerched.
//   talking ...... Inside the same rock's TALK radius the talk key starts a conversation: the boat
//                  anchors, the camera cuts to her own camera, and she talks. The key ends all
//                  three again. AngelTalking.
//   taking off ... You left the radius, so she climbs back to the flight position. AngelFlying1.
//
// Perches are the procedural spikes the Grid Designer marked as angel perch points; each one gets
// an AngelPerchPoint at spawn holding the tip of the rock it was built to, its two radii, and
// whether it is a PRIORITY perch (always come down for it) or one she is merely WATCHING (settle
// there only when she happens to be looking for somewhere to land).
//
// Animation is driven BY STATE NAME (CrossFadeInFixedTime), because AngelAnimation has no
// parameters or transitions authored — the states sit disconnected in the graph on purpose, the
// same arrangement CreepGuyAniCtrl uses. Never CrossFade: its duration is normalised to the
// SOURCE clip's length, so a long flight cycle would stretch the blend past the whole descent.
[DisallowMultipleComponent]
public class AngelCompanion : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Animator holding AngelFlying1 / AngelLanding / AngelPerched. Found in children if unset.")]
    [SerializeField] Animator animator;

    [Header("Animation states")]
    [SerializeField] string flyingState  = "AngelFlying1";
    [SerializeField] string landingState = "AngelLanding";
    [SerializeField] string perchedState = "AngelPerched";

    [Tooltip("Played for as long as a conversation lasts — from the talk key to the talk key.")]
    [SerializeField] string talkingState = "AngelTalking";

    [Tooltip("Blend time between states, in seconds.")]
    [SerializeField] float crossFadeTime = 0.35f;

    [Header("Flight")]
    [Tooltip("How far above the boat she flies.")]
    [SerializeField] float flyHeight = 6f;

    [Tooltip("How long she takes to catch up to the point above the boat. Larger = she trails " +
             "further behind while you are sailing and drifts in as you slow.")]
    [SerializeField] float followSmoothTime = 1.2f;

    [Tooltip("Fastest she will chase the boat, in units per second. 0 = no limit.")]
    [SerializeField] float followMaxSpeed = 0f;

    [Header("Facing")]
    [Tooltip("How fast she turns, in degrees per second.")]
    [SerializeField] float turnSpeed = 180f;

    [Tooltip("Yaw correction for a model whose forward is not +Z.")]
    [SerializeField] float facingYawOffset = 0f;

    [Tooltip("Below this speed there is no travel direction to read, so she holds the heading she has.")]
    [SerializeField] float minTurnSpeed = 0.15f;

    [Header("Swooping down")]
    [Tooltip("Shortest time flying before she starts looking for somewhere to land.")]
    [SerializeField] float flightTimeMin = 20f;

    [Tooltip("Longest time flying before she starts looking for somewhere to land.")]
    [SerializeField] float flightTimeMax = 40f;

    [Tooltip("How much further out than its own perch radius the boat must get before she leaves a " +
             "rock. Without a margin she would land and take off repeatedly while you hover on the " +
             "line, since the same boundary brings her down and sends her up.")]
    [SerializeField] float perchLeaveMargin = 1.5f;

    [Tooltip("How long the descent takes, from leaving the flight to her feet touching the rock.")]
    [SerializeField] float swoopDuration = 2.5f;

    [Tooltip("Shape of the drop. Flat then steep = she holds her height and dives late; steep " +
             "then flat = she drops away and levels out onto the rock.")]
    [SerializeField] AnimationCurve swoopCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Lifts her off the exact point of the rock, for a model whose origin is not quite at " +
             "the soles of her feet.")]
    [SerializeField] float perchFootOffset = 0f;

    [Header("Perched")]
    [Tooltip("How long the climb back up to the flight takes.")]
    [SerializeField] float takeOffDuration = 2f;

    [Header("Talking")]
    [Tooltip("Pressed inside a perch's talk radius, with her perched there: the boat anchors, the " +
             "camera cuts to her, and she talks. Pressed again, all three go back.")]
    [SerializeField] KeyCode talkKey = KeyCode.E;

    [Tooltip("Her own camera, authored on the prefab at the angle the conversation should be seen " +
             "from. Found in her children if unset. Nothing to talk to without one — she will still " +
             "talk, the view just stays on the boat.")]
    [SerializeField] CinemachineCamera talkCamera;

    [Tooltip("Priority her camera is raised to while talking. Needs to beat the level's own cameras.")]
    [SerializeField] int talkCameraPriority = 100;

    [Tooltip("The brain that does the cutting. Falls back to the main camera's, then to whichever " +
             "one is in the scene.")]
    [SerializeField] CinemachineBrain brain;

    /// <summary>True while a conversation is running.</summary>
    public bool IsTalking => _mood == Mood.Talking;

    enum Mood { Flying, Swooping, Perched, Talking, TakingOff }

    Mood            _mood = Mood.Flying;
    Vector3         _followVelocity;
    float           _timer;      // counts down: to the next swoop, or to leaving the rock
    float           _tween;      // 0..1 through a swoop or a take-off
    Vector3         _tweenFrom;
    AngelPerchPoint _perch;
    Transform       _boat;
    bool            _placed;    // has she been put on her flight line yet?

    // Conversation bookkeeping — everything a talk borrows and has to hand back.
    bool                      _anchorBeforeTalk;
    int                       _camPriorityBeforeTalk;
    CinemachineBlendDefinition _blendBeforeTalk;
    bool                      _blendBorrowed;
    int                       _talkLine;   // which of this perch's lines is on screen

    // The landing curve, worked out once when she commits to a descent.
    Vector3 _arcCentre;      // what she circles on the way in
    float   _arcRadius;      // 0 = no curve, straight at the rock
    float   _arcEntryAngle;  // where she joins it
    float   _arcEndAngle;    // the rock
    Vector3 _arcEntry;       // that join, as a point
    float   _joinSplit;      // how much of the descent is spent flying to the join

    // Resolved lazily: LevelDataController builds the level — boat included — after she may
    // already be sitting in the scene, so asking once in Start would find nothing.
    Transform Boat
    {
        get
        {
            if (_boat == null)
            {
                var ldc = LevelDataController.Instance;
                if (ldc != null) _boat = ldc.GetBoatRoot();
            }
            return _boat;
        }
    }

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (talkCamera == null) talkCamera = GetComponentInChildren<CinemachineCamera>(true);

        // Held on standby. A Cinemachine camera registers itself the moment it is enabled and then
        // competes on priority, and ties are broken by whichever was enabled most recently — so an
        // angel spawned after the level's own cameras, with a camera left switched on in her
        // prefab, would take the view the instant she appeared. Off until she is spoken to.
        if (talkCamera != null) talkCamera.gameObject.SetActive(false);
    }

    void Start()
    {
        _timer = RandomBetween(flightTimeMin, flightTimeMax);
        PlayState(flyingState);
    }

    // LateUpdate, not Update: the boat is driven in FixedUpdate and then rides the waves in
    // Update, so its position only settles for the frame once both have run. Following it from
    // Update would leave her chasing where it was a frame ago.
    void LateUpdate()
    {
        Transform boat = Boat;
        if (boat == null) return;

        // First frame she can see the boat, she is simply put on her flight line — spawned at the
        // level's origin she would otherwise come sweeping in from the corner of the arena, and a
        // parent rotated after spawning (the maze's post-spawn Y180) would have her turning back
        // round for the first second. Snapped, not eased, and before anything renders.
        if (!_placed)
        {
            _placed            = true;
            transform.position = FlightPoint(boat);
            SnapFacing(boat.forward);
        }

        float dt = Time.deltaTime;

        switch (_mood)
        {
            case Mood.Flying:    UpdateFlying(boat, dt);    break;
            case Mood.Swooping:  UpdateSwooping(boat, dt);  break;
            case Mood.Perched:   UpdatePerched(boat, dt);   break;
            case Mood.Talking:   UpdateTalking(boat, dt);   break;
            case Mood.TakingOff: UpdateTakingOff(boat, dt); break;
        }
    }

    // ── Flying ──

    void UpdateFlying(Transform boat, float dt)
    {
        Vector3 before = transform.position;
        transform.position = Vector3.SmoothDamp(before, FlightPoint(boat), ref _followVelocity,
                                                followSmoothTime,
                                                followMaxSpeed <= 0f ? Mathf.Infinity : followMaxSpeed,
                                                dt);
        FaceTravel(transform.position - before, dt);

        // The timer only governs the rocks she is WATCHING. A priority perch is checked every
        // frame regardless, so sailing up to one always brings her down — that is what makes it a
        // place you can rely on meeting her.
        _timer -= dt;

        var perch = PickPerch(boat, lookingToLand: _timer <= 0f);
        if (perch != null) BeginSwoop(perch);
    }

    // The point she holds: straight above the boat. The smoothing is what turns that into a
    // trailing follow, so there is no authored lag distance to keep in step with boat speed.
    Vector3 FlightPoint(Transform boat) => boat.position + Vector3.up * flyHeight;

    /// <summary>
    /// The rock she should come down to, if any. Each rock carries its own radius, so this asks
    /// every perch whether the BOAT is inside ITS circle rather than measuring one range from her.
    /// A priority rock wins over one she is only watching; between two of a kind, the nearer.
    /// </summary>
    AngelPerchPoint PickPerch(Transform boat, bool lookingToLand)
    {
        AngelPerchPoint best         = null;
        float           bestDist     = 0f;
        bool            bestPriority = false;

        var all = AngelPerchPoint.All;
        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i];
            if (p == null || !p.isActiveAndEnabled) continue;

            // A watching perch is only on offer while she is actually looking for somewhere.
            if (!p.IsPriority && !lookingToLand) continue;

            float d = p.FlatDistanceTo(boat.position);
            if (d > p.PerchRadius) continue;

            bool better = best == null
                       || (p.IsPriority && !bestPriority)
                       || (p.IsPriority == bestPriority && d < bestDist);
            if (!better) continue;

            best         = p;
            bestDist     = d;
            bestPriority = p.IsPriority;
        }
        return best;
    }

    // ── Swooping down ──

    void BeginSwoop(AngelPerchPoint perch)
    {
        _perch     = perch;
        _tweenFrom = transform.position;
        _tween     = 0f;
        _mood      = Mood.Swooping;
        PlanLanding(_tweenFrom, PerchPoint(perch));
        PlayState(landingState);
    }

    // A quarter turn of arc: enough to bring her in from behind the rock rather than across the
    // front of it. Any less and she barely bends; a half turn puts the join out to the SIDE of the
    // rock instead of behind it, which is not the approach that was asked for.
    const float LandingSweep = 90f;

    /// <summary>
    /// Works out the curve she lands along, once, at the moment she commits to the descent — so a
    /// boat sailing on mid-dive cannot drag the path around behind her.
    ///
    /// The curve is defined by where it ENDS: on the rock, travelling toward the boat, so she is
    /// already facing forward when her feet land. From there it is swept backwards a quarter turn
    /// to find where it begins, which lands behind the rock and off to one side. She flies to that
    /// point first and only then starts to turn — which is what makes every landing on a rock look
    /// like the same manoeuvre, however she happened to arrive.
    /// </summary>
    void PlanLanding(Vector3 from, Vector3 to)
    {
        // The size belongs to the ROCK, authored in the Grid Designer against its surroundings —
        // a perch hemmed in by walls gets a tight curve, one in open water a wide one.
        _arcRadius = _perch != null ? Mathf.Max(0f, _perch.LandingCurveSize) : 0f;
        if (_arcRadius < 0.001f) { _joinSplit = 1f; return; }   // no curve — straight in, as before

        // The heading she should be on at touchdown: facing the boat.
        Transform boat = Boat;
        Vector3   h    = boat != null ? boat.position - to : from - to;
        h.y = 0f;
        if (h.sqrMagnitude < 1e-4f) h = Vector3.forward;
        h.Normalize();

        // Which way round she comes: whichever side she is already on, so she takes the short way
        // to the join instead of crossing over the rock to reach it.
        Vector3 right = new Vector3(h.z, 0f, -h.x);
        Vector3 toHer = from - to; toHer.y = 0f;
        float   side  = Vector3.Dot(toHer, right) >= 0f ? 1f : -1f;
        Vector3 n     = right * side;

        _arcCentre = new Vector3(to.x + n.x * _arcRadius, to.y, to.z + n.z * _arcRadius);

        // Travel runs one way round the circle or the other depending on which side we picked.
        float dir = -side;

        _arcEndAngle   = Mathf.Atan2(to.z - _arcCentre.z, to.x - _arcCentre.x);
        _arcEntryAngle = _arcEndAngle - dir * LandingSweep * Mathf.Deg2Rad;
        _arcEntry      = ArcPointAt(_arcEntryAngle, to.y);

        // Split the descent between flying to the join and riding the curve by how long each is,
        // so she holds one pace throughout instead of dawdling along the shorter leg.
        float joinLength = Vector2.Distance(new Vector2(from.x, from.z),
                                            new Vector2(_arcEntry.x, _arcEntry.z));
        float arcLength  = _arcRadius * LandingSweep * Mathf.Deg2Rad;

        _joinSplit = joinLength + arcLength < 0.0001f
            ? 0.5f
            : Mathf.Clamp(joinLength / (joinLength + arcLength), 0.05f, 0.95f);
    }

    Vector3 ArcPointAt(float angle, float y) =>
        new Vector3(_arcCentre.x + Mathf.Cos(angle) * _arcRadius,
                    y,
                    _arcCentre.z + Mathf.Sin(angle) * _arcRadius);

    void UpdateSwooping(Transform boat, float dt)
    {
        // The rock could go while she is on her way down (a level rebuild, say) — back to the
        // flight rather than diving at a position that no longer belongs to anything.
        if (_perch == null) { ReturnToFlight(); return; }

        _tween += dt / Mathf.Max(0.01f, swoopDuration);

        Vector3 before = transform.position;
        transform.position = ArcPoint(_tweenFrom, PerchPoint(_perch), Mathf.Clamp01(_tween));
        FaceTravel(transform.position - before, dt);

        if (_tween < 1f) return;

        _mood = Mood.Perched;
        PlayState(perchedState);
    }

    // Height and ground track are worked out separately, so the two stay independent dials: the
    // swoop curve shapes the DIVE seen from the side (hold height then plunge, or drop away and
    // level out), while the landing curve shapes the PATH seen from above.
    //
    // That separation is why the swoop curve alone could never bend her approach — X and Z both ran
    // off one parameter, and two lerps sharing a parameter trace a straight line whatever curve is
    // put on it. The ground track now has its own geometry.
    Vector3 ArcPoint(Vector3 from, Vector3 to, float t)
    {
        float across = Mathf.SmoothStep(0f, 1f, t);
        float height = swoopCurve != null ? swoopCurve.Evaluate(t) : t;
        float y      = Mathf.Lerp(from.y, to.y, height);

        // No curve planned — the straight run she used to fly, and what the take-off climbs.
        if (_arcRadius < 0.001f)
            return new Vector3(Mathf.Lerp(from.x, to.x, across), y, Mathf.Lerp(from.z, to.z, across));

        if (across < _joinSplit)
        {
            // Flying out to join the curve, still at height.
            float s = _joinSplit < 0.0001f ? 1f : across / _joinSplit;
            return new Vector3(Mathf.Lerp(from.x, _arcEntry.x, s), y, Mathf.Lerp(from.z, _arcEntry.z, s));
        }

        // Riding it round to the rock.
        float r     = Mathf.Approximately(_joinSplit, 1f) ? 1f : (across - _joinSplit) / (1f - _joinSplit);
        float angle = Mathf.Lerp(_arcEntryAngle, _arcEndAngle, r);
        return ArcPointAt(angle, y);
    }

    // Read live rather than cached, so a rock that is scaled, or turned by the spawner's
    // post-spawn Y180, still has her feet on its point.
    Vector3 PerchPoint(AngelPerchPoint perch) => perch.PerchWorld + Vector3.up * perchFootOffset;

    // ── Perched ──

    void UpdatePerched(Transform boat, float dt)
    {
        if (_perch == null) { ReturnToFlight(); return; }

        transform.position = PerchPoint(_perch);
        FaceHorizontal(boat.position - transform.position, dt);

        float distance = _perch.FlatDistanceTo(boat.position);

        if (_perch.TalkEnabled && TalkPressed() && distance <= _perch.TalkRadius) { BeginTalking(); return; }

        // She stays for as long as you are here. The margin is measured OUTSIDE the radius that
        // brought her down, so drifting on the line cannot make her land and leave repeatedly.
        if (distance > _perch.PerchRadius + Mathf.Max(0f, perchLeaveMargin)) BeginTakeOff();
    }

    // ── Talking ──

    /// <summary>True when the key would start a conversation right now — perched, and close enough.</summary>
    public bool CanTalk
    {
        get
        {
            Transform boat = Boat;
            return _mood == Mood.Perched && _perch != null && _perch.TalkEnabled && boat != null &&
                   _perch.FlatDistanceTo(boat.position) <= _perch.TalkRadius;
        }
    }

    bool TalkPressed() => !PauseManager.IsPaused && Input.GetKeyDown(talkKey);

    void BeginTalking()
    {
        _mood = Mood.Talking;

        // Remembered, not assumed: you may have dropped the anchor yourself before speaking to
        // her, and ending the conversation should not lift an anchor you meant to keep.
        var anchor = BoatAnchor.Instance;
        if (anchor != null)
        {
            _anchorBeforeTalk = anchor.IsAnchored;
            anchor.SetAnchored(true);
            anchor.SetKeyLocked(true);   // no sailing off mid-sentence with the camera still on her
        }
        else
        {
            _anchorBeforeTalk = false;
            Debug.LogWarning("[AngelCompanion] No BoatAnchor in the scene — the boat will not stop " +
                             "for the conversation. Add one and it will.", this);
        }

        CutToTalkCamera(true);
        PlayState(talkingState);

        // What she says here goes through the level's ONE dialogue system — the same controller,
        // text object and black fade the rest of the game writes to.
        _talkLine = 0;
        ShowTalkLine();
    }

    // One line at a time, in the order they were typed into the Grid Designer field, split on "/".
    //
    // ShowHeld, not PlayLine: each line waits for the player rather than timing out from under
    // them. Nothing to say is a valid conversation — the camera still cuts to her and she still
    // plays her talking animation, there is just no box.
    void ShowTalkLine()
    {
        var dialogue = DialogueTextController.Instance;
        if (dialogue == null || _perch == null) return;

        var lines = _perch.TalkLines;
        if (_talkLine < 0 || _talkLine >= lines.Length) return;

        dialogue.ShowHeld(lines[_talkLine]);
    }

    void UpdateTalking(Transform boat, float dt)
    {
        if (_perch == null) { EndTalking(); return; }

        // Held on the point and facing you. The boat is anchored, so there is no leaving to check
        // for — the conversation ends on the key and nothing else.
        transform.position = PerchPoint(_perch);
        FaceHorizontal(boat.position - transform.position, dt);

        if (!TalkPressed()) return;

        // The key steps through what she has to say, and ends the conversation off the back of the
        // last line — so one press is always "carry on", and never a surprise cut away mid-speech.
        _talkLine++;
        if (_talkLine < _perch.TalkLines.Length) ShowTalkLine();
        else                                     EndTalking();
    }

    void EndTalking()
    {
        var anchor = BoatAnchor.Instance;
        if (anchor != null)
        {
            anchor.SetKeyLocked(false);
            anchor.SetAnchored(_anchorBeforeTalk);
        }

        // HideAll, not Hide: Hide leaves the black panel up, since a timed sequence normally
        // lowers that itself at the end. Ending on the key has no such tail.
        var dialogue = DialogueTextController.Instance;
        if (dialogue != null) dialogue.HideAll();

        CutToTalkCamera(false);
        PlayState(perchedState);
        _mood = Mood.Perched;
    }

    // ── Her camera ──

    // A straight cut both ways. Cinemachine decides the transition from the brain's default blend
    // at the moment the priority changes, so the blend is borrowed for the cut and handed back a
    // frame later — restore it any sooner and the cut back to the boat gets eased instead.
    void CutToTalkCamera(bool toAngel)
    {
        var b = Brain;
        if (b != null)
        {
            if (toAngel && !_blendBorrowed)
            {
                _blendBeforeTalk = b.DefaultBlend;
                _blendBorrowed   = true;
            }
            b.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        }

        if (talkCamera != null)
        {
            if (toAngel)
            {
                _camPriorityBeforeTalk = talkCamera.Priority.Value;
                talkCamera.Priority    = talkCameraPriority;
                talkCamera.gameObject.SetActive(true);
            }
            else
            {
                // Switched off rather than merely out-prioritised: a live camera that is only
                // demoted stays registered and would come back the moment anything else changed.
                // The brain falls back to the boat's camera, and the forced Cut makes that a cut.
                talkCamera.gameObject.SetActive(false);
                talkCamera.Priority = _camPriorityBeforeTalk;
            }
        }

        if (!toAngel && _blendBorrowed && isActiveAndEnabled) StartCoroutine(GiveBackBlend());
    }

    IEnumerator GiveBackBlend()
    {
        yield return null;   // let the brain consume the cut first

        var b = Brain;
        if (b != null) b.DefaultBlend = _blendBeforeTalk;
        _blendBorrowed = false;
    }

    CinemachineBrain Brain
    {
        get
        {
            if (brain == null && Camera.main != null) brain = Camera.main.GetComponent<CinemachineBrain>();
            if (brain == null) brain = FindFirstObjectByType<CinemachineBrain>();
            return brain;
        }
    }

    // ── Taking off ──

    void BeginTakeOff()
    {
        _tweenFrom = transform.position;
        _tween     = 0f;
        _mood      = Mood.TakingOff;

        // Straight up and out. Without clearing this she would ride the LANDING curve back out
        // again — round a circle planned for a rock she is leaving, toward a boat that has moved.
        _arcRadius = 0f;
        _joinSplit = 1f;

        PlayState(flyingState);
    }

    void UpdateTakingOff(Transform boat, float dt)
    {
        _tween += dt / Mathf.Max(0.01f, takeOffDuration);

        // Aimed at where the flight is NOW, recomputed each frame, so a boat that sails on during
        // the climb is followed rather than left behind.
        Vector3 before = transform.position;
        transform.position = ArcPoint(_tweenFrom, FlightPoint(boat), Mathf.Clamp01(_tween));
        FaceTravel(transform.position - before, dt);

        if (_tween >= 1f) ReturnToFlight();
    }

    void ReturnToFlight()
    {
        _perch          = null;
        _followVelocity = Vector3.zero;   // the tween's speed is not the follow's — let it build again
        _timer          = RandomBetween(flightTimeMin, flightTimeMax);
        _mood           = Mood.Flying;
        PlayState(flyingState);
    }

    // ── Facing ──

    // She faces the way she is travelling. Below the threshold there is no direction in the
    // movement to read, so she keeps the heading she has rather than snapping to an arbitrary one.
    void FaceTravel(Vector3 movement, float dt)
    {
        Vector3 flat = new Vector3(movement.x, 0f, movement.z);
        float   min  = minTurnSpeed * dt;
        if (flat.sqrMagnitude < min * min) return;
        FaceHorizontal(flat, dt);
    }

    void FaceHorizontal(Vector3 direction, float dt)
    {
        if (!TryHeading(direction, out Quaternion want)) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * dt);
    }

    void SnapFacing(Vector3 direction)
    {
        if (TryHeading(direction, out Quaternion want)) transform.rotation = want;
    }

    bool TryHeading(Vector3 direction, out Quaternion heading)
    {
        heading = transform.rotation;

        Vector3 flat = new Vector3(direction.x, 0f, direction.z);
        if (flat.sqrMagnitude < 1e-6f) return false;

        heading = Quaternion.LookRotation(flat.normalized, Vector3.up) *
                  Quaternion.Euler(0f, facingYawOffset, 0f);
        return true;
    }

    // ── Animation ──

    void PlayState(string state)
    {
        if (animator == null || string.IsNullOrEmpty(state)) return;
        animator.CrossFadeInFixedTime(state, crossFadeTime);
    }

    static float RandomBetween(float a, float b) => Random.Range(Mathf.Min(a, b), Mathf.Max(a, b));

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        var       ldc  = LevelDataController.Instance;
        Transform boat = ldc != null ? ldc.GetBoatRoot() : null;
        if (boat == null) return;

        // The ranges belong to the rocks now, and each AngelPerchPoint draws its own.
        // The point she holds when flying.
        Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
        Gizmos.DrawLine(boat.position, FlightPoint(boat));
    }
#endif
}
