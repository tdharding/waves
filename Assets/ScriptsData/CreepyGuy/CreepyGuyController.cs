using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Creepy Guy — an ambush enemy that ships with his own big spike (the rock is part of
// this prefab, so placing him places the rock too; there is no separate alignment step).
//
// Behaviour, gated purely on the boat's distance to the rock centre:
//
//   > activationRadius ............ DORMANT. The character is SetActive(false) — no
//                                   renderer, no animator, no collider. Only this root
//                                   stays awake, running one squared-distance check.
//   <= activationRadius ........... CREEPING. He drives round to the side of the rock
//                                   facing away from the boat and shuffles about there.
//   approaching the band .......... He climbs from the lower ring to the upper one as the boat
//                                   closes — the rock is a cone, so the upper ring is tighter.
//                                   Both thresholds are measured outward from attackBandOuter, so
//                                   he is always topped out before he can be armed.
//   inside the jump band .......... ARMED, if the boat is moving and is not facing him. He goes
//                                   still and holds the jump-ready pose — the tell — then leaps
//                                   at where the boat is about to be, from the TOP ring only.
//   boat out of pouncing range .... He HOPS to a rock closer to it, if one is within
//                                   maxHopDistance. Only to get in range — never to get ahead.
//   after the leap ................ FALLEN, then he surfaces at whichever rock is nearest the boat.
//
// The rock has no authored "front" or "back" — the back is recomputed every frame as
// whatever side faces away from the boat, which is what makes him scurry when you circle.
//
// All creep motion runs in this root's LOCAL space, so LevelSpawner's post-spawn Y180
// rotation of spawnParent cannot flip it. The leap switches to world space because it is
// chasing a world-space boat.
[DisallowMultipleComponent]
public class CreepyGuyController : MonoBehaviour
{
    enum State { Dormant, Creeping, Hopping, WindUp, AttackJumping, Falling, Fallen, Emerging, Retreating }

    // ─────────────────────────────────────────────
    // REFERENCES
    // ─────────────────────────────────────────────

    [Header("References")]
    [Tooltip("The node that moves around the rock — the character itself or any empty parent of it, " +
             "at any depth. The rings are driven in THIS node's own parent space, so wrapping the " +
             "character in an empty and assigning that works. Its authored position is only used " +
             "for his starting bearing; the ring radii and heights below place him.")]
    [SerializeField] Transform figure;

    [Tooltip("The rock he is currently on. It owns the ring shapes; this component owns the behaviour. " +
             "Swapping this is what will let him move between rocks — he adopts whatever rings the new " +
             "rock defines.")]
    [SerializeField] CreepClimbingArea climbingArea;

    [Tooltip("Animator holding CreepGuyOnRock / CreepGuyJump. Found under 'figure' if unset.")]
    [SerializeField] Animator animator;

    [Tooltip("Collider that strikes the boat. Enabled only while he is in the air. Found under " +
             "'figure' if unset. Needs a kinematic Rigidbody alongside it so the boat's " +
             "OnCollisionEnter fires reliably.")]
    [SerializeField] Collider hitCollider;

    [Tooltip("Sits on the waterline after spawn (the PrefabBaselineAlignment node) — its world Y " +
             "is the height he lands at. Found in children if unset.")]
    [SerializeField] Transform waterlineReference;

    [Tooltip("Optional splash spawned where he hits the water. Landing is silent while this is null.")]
    [SerializeField] GameObject splashPrefab;

    [Tooltip("How far the visible model is drawn ABOVE the 'figure' node. The animation clips carry " +
             "a constant root-position curve that overwrites the authored offset, so the body does " +
             "not sit on its node. Everything that places him — both rings and the landing — is " +
             "pushed down by this, so the ring heights and the waterline mean where the BODY ends " +
             "up. Set to 0 if the clips are ever fixed at the import.")]
    [SerializeField] float modelYOffset = 0f;

    // ─────────────────────────────────────────────
    // ANIMATION
    // ─────────────────────────────────────────────

    // Driven by state name, so CreepGuyAniCtrl needs no parameters or transitions authored.
    //
    // On the rock it is just two clips: still, or moving. Anything that shifts him — emerging,
    // retreating, darting, climbing — plays the moving clip, at a speed taken from how fast he is
    // actually travelling, so one clip covers every kind of movement.
    [Header("Animation States")]
    [Tooltip("Static pose held whenever he is not moving.")]
    [SerializeField] string onRockState = "CreepGuyOnRock";

    [Tooltip("Played whenever he IS moving on the rock, at a speed driven by how fast he is going.")]
    [SerializeField] string movingState = "CreepGuyClimbing";

    [FormerlySerializedAs("jumpReadyState")]
    [SerializeField] string attackJumpReadyState = "CreepGuyJumpReady";

    [FormerlySerializedAs("jumpState")]
    [SerializeField] string attackJumpState = "CreepGuyJump";

    [SerializeField] string sinkState = "CreepGuySink";

    [Tooltip("Blend into the jump/ready/sink poses, in SECONDS. Those are static poses, so this " +
             "blend is the only thing that reads as movement between them.")]
    [SerializeField] float crossFadeTime = 0.15f;

    [Tooltip("Blend between the still and moving clips, in SECONDS. Must be far shorter than a " +
             "dart (Dart Duration) or every dart smears instead of reading as a scuttle.")]
    [SerializeField] float movementBlendTime = 0.06f;

    [Tooltip("Travel speed, in world units per second, below which he counts as still. Stops him " +
             "flickering between the two clips on tiny drift.")]
    [SerializeField] float movingThreshold = 0.05f;

    [Tooltip("Travel speed at which the moving clip plays at its authored rate. Slower travel plays " +
             "it slower, faster plays it faster — so a crawl and a dart use the same clip.")]
    [SerializeField] float moveAnimReferenceSpeed = 0.5f;

    // ─────────────────────────────────────────────
    // ACTIVATION
    // ─────────────────────────────────────────────

    // Every distance the behaviour is gated on, in one place, in the order the boat crosses them
    // coming in. All are measured flat from the rock's centre, and all are drawn as gizmo rings —
    // the label each one carries in the Scene view is named in its tooltip.
    [Header("Radii — all drawn as gizmo rings")]

    [Tooltip("GIZMO: \"hop reach\" (green). Furthest he can jump rock to rock, centre to centre.")]
    [SerializeField] float maxHopDistance = 0.87f;

    [Tooltip("GIZMO: \"activate\" (yellow). Boat within this wakes him and he climbs out of the water.")]
    [SerializeField] float activationRadius = 1.8f;

    [Tooltip("No gizmo. Extra distance the boat must travel back out before he climbs down and " +
             "sleeps again, so hovering on the boundary does not flicker him on and off.")]
    [SerializeField] float deactivateBuffer = 0.5f;

    [Tooltip("GIZMO: \"climb starts\" (pale green). Measured OUTWARD from Jump Band Outer — how far " +
             "out he begins scrambling up the cone.")]
    [SerializeField] float climbLead = 0.6f;

    [Tooltip("GIZMO: \"fully up\" (pale green). Measured OUTWARD from Jump Band Outer — he must be " +
             "topped out by here. Needs to be more than zero because the climb only advances during " +
             "darts, and darts have rest gaps.")]
    [SerializeField] float climbTopMargin = 0.04f;

    [Tooltip("GIZMO: outer of the two red \"attack band\" rings. Further out than this and he keeps " +
             "hiding rather than attacking.")]
    [FormerlySerializedAs("jumpBandOuter")]
    [SerializeField] float attackBandOuter = 0.94f;

    [Tooltip("GIZMO: inner of the two red \"attack band\" rings. Closer than this and he keeps hiding " +
             "— hugging the rock is safe.")]
    [FormerlySerializedAs("jumpBandInner")]
    [SerializeField] float attackBandInner = 0.57f;

    [Tooltip("GIZMO: \"ducks down\" (cyan). Boat closer than this and he drops back to the LOWER " +
             "ring — right up against the rock he shrinks down it rather than staying on top. " +
             "Should sit inside Jump Band Inner, since he cannot pounce from the lower ring anyway.")]
    [SerializeField] float duckRadius = 0.51f;

    // Read off the prefab asset by the Grid Designer so the hop routes it draws are the same
    // reach the runtime will use — one number, no second copy to keep in sync.
    public float MaxHopDistance => maxHopDistance;

    // ─────────────────────────────────────────────
    // CREEPING
    // ─────────────────────────────────────────────

    // He does not rotate round the rock continuously. He sits dead still until the boat has
    // moved enough to expose him, then makes one sudden dart and freezes again. A large
    // correction is capped per dart, so recovering from it reads as a burst of two or three.
    [Header("Creeping — Darting")]
    [Tooltip("Degrees of exposure that build up before he moves at all. Larger = he lets you see " +
             "more of him before reacting, and the darts come further apart.")]
    [SerializeField] float dartTriggerAngle = 20f;

    [Tooltip("Most degrees a single dart covers. A bigger correction than this is split across " +
             "several darts — which is what produces the bursts.")]
    [SerializeField] float maxDartAngle = 45f;

    [Tooltip("Seconds a single dart takes. Short = a hard scuttle.")]
    [SerializeField] float dartDuration = 0.2f;

    [Tooltip("Minimum stillness between darts.")]
    [SerializeField] float dartRest = 0.3f;

    [Tooltip("Random extra rest on top, so bursts do not come out metronomic.")]
    [SerializeField] float dartRestRandom = 0.2f;

    [Tooltip("How much pending climb is enough to trigger a dart on its own, so a boat approaching " +
             "dead-on — which barely changes his bearing — still makes him scramble up the cone.")]
    [SerializeField] float climbTriggerDelta = 0.15f;

    [Tooltip("Drives his facing from the ring. Off = his authored rotation is held unchanged. " +
             "Because he always sits opposite the boat, an inward ring facing already points him " +
             "back at it — no separate look-at is computed anywhere except the leap.")]
    [SerializeField] bool faceOutward = true;

    [Tooltip("MODEL AXIS ONLY — which local axis of 'figure' counts as the model's forward. This is " +
             "what aims him at the boat for the wind-up and the leap, so it must be correct or he " +
             "will lunge sideways. Forward along +Z = 0, along -X = 90, along -Z = 180, along +X = -90.")]
    [SerializeField] float facingYawOffset = 0f;

    [Tooltip("Which way he faces on the rings, on top of the model axis above. 180 = inward, into " +
             "the rock. 0 = outward. ±90 = along the ring, the way he darts.")]
    [SerializeField] float ringFacingOffset = -90f;

    // ─────────────────────────────────────────────
    // CLIMB
    // The rock is a cone, so he rides two of its rings: a wide low one, and a tight high one he
    // climbs to as the boat closes. The leap can only happen from the top ring. The rings
    // themselves belong to the CreepClimbingArea — only when he climbs them is decided here.
    // ─────────────────────────────────────────────

    [Header("Climb")]
    [Tooltip("How fast the climb itself can move, as a fraction of the full climb per second. " +
             "Stops him snapping up and down when the boat crosses the thresholds quickly. " +
             "The distances that trigger it live under Radii.")]
    [SerializeField] float climbSpeed = 1.5f;

    // ─────────────────────────────────────────────
    // LEAN
    // Instead of a separate clip for moving sideways round the rock, one bone leans into the
    // direction of travel. Layered over the animated pose, so the moving clip stays generic.
    // ─────────────────────────────────────────────

    [Header("Lean")]
    [Tooltip("Bone that leans into the direction he is travelling round the rock, about its LOCAL X. " +
             "Layered on top of the animated pose, so it must be a bone the clips actually key — an " +
             "unkeyed bone would accumulate the lean and spin.")]
    [SerializeField] Transform leanBone;

    [Tooltip("Most it leans at full speed, in degrees. Clockwise round the rock leans -X, " +
             "anticlockwise +X.")]
    [SerializeField] float maxLeanAngle = 25f;

    [Tooltip("How fast the lean itself can change, in degrees per second. Lower makes him ease into " +
             "and out of the lean rather than snapping to it at the start of every dart.")]
    [SerializeField] float leanResponse = 180f;

    // ─────────────────────────────────────────────
    // JUMP BAND
    // ─────────────────────────────────────────────

    [Header("Pounce Conditions")]
    [Tooltip("Boat must be sailing at least this fast — he does not ambush a parked boat.")]
    [SerializeField] float minBoatSpeed = 0f;

    [Tooltip("dot(boatForward, directionToHim) below this counts as 'not facing'. " +
             "0.35 ≈ a 70° safety cone either side of the bow; 0 = exactly abeam.")]
    [SerializeField] float notFacingDot = 0.35f;

    // ─────────────────────────────────────────────
    // JUMP
    // ─────────────────────────────────────────────

    [Header("Attack Jump")]
    [Tooltip("Seconds held in the jump-ready pose before he leaps — the tell. He stops darting for " +
             "it, which is most of what sells it. He is committed once this starts; the boat cannot " +
             "call it off by turning or leaving the band.")]
    [FormerlySerializedAs("windUpTime")]
    [SerializeField] float attackWindUpTime = 0.5f;

    [Tooltip("Flight time. Constant regardless of distance, so a longer leap reads as a faster lunge.")]
    [FormerlySerializedAs("jumpDuration")]
    [SerializeField] float attackJumpDuration = 1.5f;

    [Tooltip("Peak height above the straight line from launch to landing, in world units.")]
    [FormerlySerializedAs("jumpHeight")]
    [SerializeField] float attackJumpHeight = 1f;

    [Tooltip("How far ahead of the boat he aims. 0 = at where you are now (always lands behind you), " +
             "1 = a perfect intercept if you hold your heading. He commits at launch and never " +
             "re-aims, so changing course or speed dodges him.")]
    [FormerlySerializedAs("leadFactor")]
    [SerializeField] float attackLeadFactor = 0.5f;

    [Tooltip("Safety clamp on how far the landing point can be from the launch point.")]
    [FormerlySerializedAs("maxJumpDistance")]
    [SerializeField] float maxAttackJumpDistance = 6f;

    [Tooltip("World units BELOW the waterline he keeps falling to after the lunge, before he stops " +
             "for good. This is phase two — it no longer affects where the lunge itself is aimed.")]
    [SerializeField] float landingDrop = 3f;

    [Tooltip("Downward acceleration during the fall. The fall inherits the lunge's horizontal speed " +
             "and its vertical speed at the moment the lunge ends, so the two phases join smoothly. " +
             "Applies only ABOVE the water — below the surface Sink Speed takes over.")]
    [SerializeField] float fallGravity = 12f;

    [Tooltip("Constant downward speed once he is under the water, replacing gravity. Horizontal drift " +
             "stops at the surface too, so he plummets in and then sinks slowly to Landing Drop.")]
    [SerializeField] float sinkSpeed = 0.4f;

    [Tooltip("Which way he faces in flight, measured against his direction of TRAVEL — the in-air " +
             "equivalent of Ring Facing Offset, which is measured against the rock's radial. The two " +
             "need different values because clinging to a rock and flying through the air are not the " +
             "same orientation. 180 from the ring value flips him head-first.")]
    [FormerlySerializedAs("jumpFacingOffset")]
    [SerializeField] float attackJumpFacingOffset = 90f;

    // ─────────────────────────────────────────────
    // RESPAWN
    // ─────────────────────────────────────────────

    // ─────────────────────────────────────────────
    // HOPPING
    // He moves rock to rock to get within pouncing range of the boat — never to get ahead of it.
    // Cautious: he only moves when he cannot reach the boat from where he is.
    // ─────────────────────────────────────────────

    [Header("Hopping")]
    [Tooltip("Travel speed of a hop, in world units per second. The hop's duration comes from the " +
             "distance, so a long hop takes longer rather than turning into a cannon shot.")]
    [SerializeField] float hopSpeed = 0.8f;

    [Tooltip("Peak height above the straight line of a hop.")]
    [SerializeField] float hopHeight = 0.5f;

    [Tooltip("Stillness after landing on a rock before he will consider hopping again — without it " +
             "he would chain across a whole chain of rocks in one unbroken movement.")]
    [SerializeField] float hopRest = 1f;

    [Tooltip("How much CLOSER to the boat a rock must be before he will bother moving to it — his " +
             "preference for staying put. Because the same threshold applies coming back, it leaves " +
             "a dead zone twice this wide between any two rocks, which is what stops him bouncing " +
             "between a pair as the boat passes them. Raise it until only real repositioning survives.")]
    [SerializeField] float minHopGain = 2f;

    [Tooltip("When he could BOTH hop to a better rock and pounce from this one, how often he " +
             "repositions instead of committing. 1 = always shuffle closer first and only attack " +
             "once there is nowhere better. 0 = always take the shot. Rolled once per opportunity, " +
             "so the same approach does not flip back and forth.")]
    [Range(0f, 1f)]
    [SerializeField] float hopPreference = 0.294f;

    // ─────────────────────────────────────────────
    // STREET LIGHT
    // Lamps light in path order as the player feeds them souls, so the lit area spreads along the
    // chain — and he gets pushed further down the level by the player's own progress.
    // ─────────────────────────────────────────────

    [Header("Street Light")]
    [Tooltip("How much faster he moves while escaping a lit rock, as a multiple of Hop Speed.")]
    [SerializeField] float fleeSpeedMultiplier = 1.5f;

    [Tooltip("Draws his escape route in the Scene view while he is fleeing, so you can see whether " +
             "your rock spacing actually gives him a way out.")]
    [SerializeField] bool showEscapeRoute = true;

    [Header("Respawn")]
    [Tooltip("Seconds face-down under the water before he crawls back out of the spawn ring. " +
             "0 = he stays down for good, as before.")]
    [SerializeField] float respawnDelay = 3f;

    [Tooltip("Seconds to climb from the spawn ring up to the lower ring before normal creeping resumes.")]
    [SerializeField] float crawlUpDuration = 2f;

    [Header("Debug")]
    [SerializeField] bool debugLogs = true;

    [Tooltip("Withholds the leap ONLY. He still arms, plays the jump-ready pose and turns to face " +
             "you, then drops back to creeping and re-arms — so you get unlimited passes at the same " +
             "rock to tune the creep, the climb and the tell. Safe to toggle during play.")]
    [FormerlySerializedAs("blockJump")]
    [SerializeField] bool blockAttackJump = false;

    // ─────────────────────────────────────────────
    // RUNTIME STATE
    // ─────────────────────────────────────────────

    State _state = State.Dormant;

    float      _angle;        // current bearing round the rock, degrees, 0° = local +Z
    float      _climb;        // 0 = lower ring, 1 = upper ring

    bool  _darting;
    float _dartElapsed;
    float _dartFrom;
    float _dartTo;
    float _restRemaining;

    Vector3    _lastFigurePos;
    bool       _wasMoving;
    bool       _movementAnimActive;

    float      _lean;
    float      _prevAngle;

    float      _windUpElapsed;
    float      _armedFacing;   // the dot that triggered him, kept for the launch log

    const float BlockedRearmDelay = 1.5f;   // debug only — stops blockAttackJump strobing the tell
    float       _blockedRearmAt;

    Vector3    _attackStart;
    Vector3    _attackTarget;
    Quaternion _attackRot;
    float      _attackElapsed;
    Vector3    _fallVelocity;

    float      _respawnAt;
    float      _crawlElapsed;
    float      _retreatFromRadius;
    float      _retreatFromHeight;

    CreepClimbingArea _hopTargetArea;
    float             _hopTargetAngle;
    Vector3           _hopFrom;
    Vector3           _hopTo;
    float             _hopElapsed;
    float             _hopDuration;
    Quaternion        _hopRot;
    float             _nextHopAt;

    // Rebuilt on arrival at each rock, so lighting another lamp mid-escape re-routes him rather
    // than leaving him committed to a route that has since become useless.
    readonly List<CreepClimbingArea> _escapeRoute = new List<CreepClimbingArea>();
    readonly List<int>               _routeParent = new List<int>();
    readonly List<bool>              _routeSeen   = new List<bool>();
    readonly Queue<int>              _routeQueue  = new Queue<int>();

    Transform    _boat;
    BoatMovement _boatMovement;
    Vector3      _lastBoatPos;
    float        _fallbackSpeed;
    bool         _warnedNoBoatMovement;

    // ─────────────────────────────────────────────
    // SETUP
    // ─────────────────────────────────────────────

    void Awake()
    {
        if (figure == null)
        {
            Debug.LogError("[CreepyGuy] No 'figure' assigned — disabling.", this);
            enabled = false;
            return;
        }

        // climbingArea may legitimately be empty: a creeper prefab with no rock of its own is
        // placed ONTO a spike in the Grid Designer, and adopts whichever one he is standing on.
        // That cannot be resolved here — the rocks' OnEnable has not necessarily run yet — so it
        // happens on the first tick instead.

        if (animator == null)           animator           = figure.GetComponentInChildren<Animator>(true);
        if (hitCollider == null)        hitCollider        = figure.GetComponentInChildren<Collider>(true);
        if (waterlineReference == null) waterlineReference = GetComponentInChildren<PrefabBaselineAlignment>(true)?.transform;

        // One creeper per rock — claimed straight away so nobody else picks this one.
        if (climbingArea != null)
        {
            climbingArea.Claim(this);
            CaptureAuthoredPose();
        }

        if (hitCollider != null) hitCollider.enabled = false;
        figure.gameObject.SetActive(false);
        _state = State.Dormant;

        if (debugLogs)
            Debug.Log($"[CreepyGuy] Spawned on '{(climbingArea != null ? climbingArea.name : "NONE")}' — " +
                      $"start angle={_angle:0.#}°, animator={animator != null}, collider={hitCollider != null}, " +
                      $"waterline={waterlineReference != null}", this);
    }

    // The spike he was placed on. Nearest with room to him, since the Grid Designer snaps a creeper
    // placement onto the rock it is allocated to — so "nearest" is exact, not a guess. Retried each
    // tick until the rocks have registered.
    bool AdoptNearestRock()
    {
        CreepClimbingArea best     = null;
        float             bestDist = float.MaxValue;

        foreach (var area in CreepClimbingArea.All)
        {
            if (area == null || !area.HasRoomFor(this)) continue;
            float d = FlatDistance(area.CentreWorld, transform.position);
            if (d < bestDist) { bestDist = d; best = area; }
        }

        if (best == null) return false;

        climbingArea = best;
        climbingArea.Claim(this);
        CaptureAuthoredPose();

        if (debugLogs)
            Debug.Log($"[CreepyGuy] Adopted rock '{best.name}' at {bestDist:0.###} away.", this);
        return true;
    }

    // Only his starting bearing comes from the authored layout now — the rock's rings decide
    // radius and height. Read through the rock, so it survives the spawnParent offset + Y180.
    void CaptureAuthoredPose()
    {
        _angle = climbingArea != null ? climbingArea.BearingOf(figure.position) : 0f;
        _climb = 0f;
    }

    // The rock he is on decides every position; he is never parented to it, so he cannot inherit
    // its scale. Placements of BigSpike can be scaled in the Grid Designer, and he must stay a
    // defined size — which is exactly why this reads the area rather than reparenting onto it.
    Vector3 CentreWorld => climbingArea != null ? climbingArea.CentreWorld : transform.position;

    float WaterY => waterlineReference != null ? waterlineReference.position.y : transform.position.y;

    // ─────────────────────────────────────────────
    // BOAT
    // ─────────────────────────────────────────────

    // Resolved lazily — the maze (and so this prefab) spawns before LevelDataController has
    // positioned and enabled the gameplay boat.
    Transform Boat
    {
        get
        {
            if (_boat == null)
            {
                var ldc = LevelDataController.Instance;
                if (ldc != null)
                {
                    _boat         = ldc.GetBoatRoot();
                    _boatMovement = ldc.GetBoatMovement();
                    if (_boat != null) _lastBoatPos = _boat.position;
                }
            }
            return _boat;
        }
    }

    // BoatMovement.CurrentSpeed when available, otherwise measured from position deltas so a
    // scene with the reference unassigned still behaves rather than silently never firing.
    float BoatSpeed
    {
        get
        {
            if (_boatMovement != null) return _boatMovement.CurrentSpeed;

            if (!_warnedNoBoatMovement)
            {
                Debug.LogWarning("[CreepyGuy] LevelDataController has no BoatMovement — falling back to " +
                                 "measuring boat speed from position deltas.", this);
                _warnedNoBoatMovement = true;
            }
            return _fallbackSpeed;
        }
    }

    void TrackFallbackSpeed(Transform boat)
    {
        if (_boatMovement != null || Time.deltaTime <= 0f) return;
        _fallbackSpeed = Vector3.Distance(boat.position, _lastBoatPos) / Time.deltaTime;
        _lastBoatPos   = boat.position;
    }

    // ─────────────────────────────────────────────
    // TICK
    // ─────────────────────────────────────────────

    void Update()
    {
        Transform boat = Boat;
        if (boat == null) return;

        // Placed on a spike rather than carrying one — work out which spike that was.
        if (climbingArea == null && !AdoptNearestRock()) return;

        TrackFallbackSpeed(boat);
        DrawEscapeRoute();

        // The one check a dormant creepy guy pays for.
        Vector3 toBoat = boat.position - CentreWorld;
        toBoat.y = 0f;
        float distSqr = toBoat.sqrMagnitude;

        switch (_state)
        {
            case State.Dormant:
                if (distSqr <= activationRadius * activationRadius) Activate(boat);
                break;

            // Cornered by the light and put down, or knocked into the water — either way he waits
            // here until an unlit rock is near the boat, then climbs back up at it.
            case State.Fallen:
                if (respawnDelay > 0f && Time.time >= _respawnAt)
                {
                    var rock = PickSurfacingRock(boat);
                    if (rock != null) BeginEmerge(boat, rock);
                }
                break;

            case State.Creeping:
                // Light beats everything — he will not stand and creep on a lit rock.
                if (IsRockLit(climbingArea)) { FleeTheLight(); break; }

                float sleepRadius = activationRadius + deactivateBuffer;
                if (distSqr > sleepRadius * sleepRadius) { BeginRetreat(); break; }
                float dist = Mathf.Sqrt(distSqr);
                UpdateCreep(boat, dist);
                ChooseHopOrAmbush(boat, dist);
                break;

            case State.Hopping:
                UpdateHop();
                break;

            case State.Retreating:
                UpdateRetreat();
                break;

            case State.WindUp:
                // He abandons the pounce if his rock lights while he is arming.
                if (IsRockLit(climbingArea)) { FleeTheLight(); break; }
                UpdateWindUp(boat);
                break;

            case State.AttackJumping:
                UpdateAttackJump();
                break;

            case State.Falling:
                UpdateFall();
                break;

            case State.Emerging:
                UpdateEmerge();
                break;
        }
    }

    // ─────────────────────────────────────────────
    // BONE WIGGLE
    // ─────────────────────────────────────────────

    // Must be LateUpdate: the Animator (Update Mode: Normal) writes the pose before this runs, so
    // the lean layers over the animated pose instead of being overwritten by it.
    void LateUpdate()
    {
        UpdateMovementAnimation();
        UpdateLean();
    }

    // One clip for every kind of movement on the rock, played at the speed he is actually moving —
    // so a slow crawl out of the water and a hard dart use the same animation and read differently.
    void UpdateMovementAnimation()
    {
        if (animator == null || figure == null) return;

        bool onTheRock = _state == State.Creeping || _state == State.Emerging || _state == State.Retreating;

        if (!onTheRock)
        {
            // Committed actions own their own clip, and must not inherit the climb's playback rate.
            if (_movementAnimActive) { animator.speed = 1f; _movementAnimActive = false; }
            _lastFigurePos = figure.position;
            return;
        }

        // First frame back on the rock — settle, don't measure a jump in position as movement.
        if (!_movementAnimActive)
        {
            _lastFigurePos      = figure.position;
            _wasMoving          = false;
            _movementAnimActive = true;
            animator.speed      = 1f;
            if (!string.IsNullOrEmpty(onRockState)) animator.CrossFadeInFixedTime(onRockState, movementBlendTime);
            return;
        }

        float rate = Time.deltaTime > 0f
            ? Vector3.Distance(figure.position, _lastFigurePos) / Time.deltaTime
            : 0f;
        _lastFigurePos = figure.position;

        bool moving = rate >= movingThreshold;
        if (moving != _wasMoving)
        {
            string clip = moving ? movingState : onRockState;
            if (!string.IsNullOrEmpty(clip)) animator.CrossFadeInFixedTime(clip, movementBlendTime);
            _wasMoving = moving;
        }

        animator.speed = moving
            ? Mathf.Clamp(rate / Mathf.Max(0.01f, moveAnimReferenceSpeed), 0.25f, 3f)
            : 1f;
    }

    // Leans into the direction he is travelling round the rock. Full lean at the fastest he can
    // ever turn — a dart at full tilt — so it scales itself to whatever the dart tuning is.
    void UpdateLean()
    {
        if (leanBone == null || maxLeanAngle <= 0f) return;

        float target = 0f;
        bool onTheRock = _state == State.Creeping || _state == State.Emerging || _state == State.Retreating;

        if (onTheRock && Time.deltaTime > 0f)
        {
            float angularSpeed = Mathf.DeltaAngle(_prevAngle, _angle) / Time.deltaTime;
            float peak         = maxDartAngle / Mathf.Max(0.01f, dartDuration);
            // Clockwise round the rock is a rising bearing, and leans -X.
            target = -Mathf.Clamp(angularSpeed / peak, -1f, 1f) * maxLeanAngle;
        }
        _prevAngle = _angle;

        _lean = Mathf.MoveTowards(_lean, target, leanResponse * Time.deltaTime);
        leanBone.localRotation *= Quaternion.Euler(_lean, 0f, 0f);
    }

    // ─────────────────────────────────────────────
    // ACTIVATION
    // ─────────────────────────────────────────────

    // He always comes into view the same way — up out of the spawn ring — whether he is waking
    // because the boat returned or coming back after being put in the water.
    void Activate(Transform boat)
    {
        CreepClimbingArea rock = PickSurfacingRock(boat);
        if (rock == null) return;   // everything near the boat is lit — stay down and keep checking

        if (debugLogs) Debug.Log("[CreepyGuy] Activated — boat in range, climbing up.", this);
        BeginEmerge(boat, rock);
    }

    // The boat has gone. He climbs back down to the spawn ring below the water before hiding,
    // rather than blinking out where he stood — the crawl-up run backwards.
    void BeginRetreat()
    {
        _retreatFromRadius = Mathf.Lerp(climbingArea.LowerRingRadius, climbingArea.UpperRingRadius, _climb);
        _retreatFromHeight = Mathf.Lerp(climbingArea.LowerRingHeight, climbingArea.UpperRingHeight, _climb);

        _crawlElapsed = 0f;
        _darting      = false;

        _state = State.Retreating;
        if (debugLogs) Debug.Log("[CreepyGuy] Boat left range — climbing down to the spawn ring.", this);
    }

    void UpdateRetreat()
    {
        _crawlElapsed += Time.deltaTime;
        float t = crawlUpDuration > 0.0001f ? Mathf.Clamp01(_crawlElapsed / crawlUpDuration) : 1f;
        float s = Mathf.SmoothStep(0f, 1f, t);

        PlaceOnRing(Mathf.Lerp(_retreatFromRadius, climbingArea.SpawnRingRadius, s),
                    Mathf.Lerp(_retreatFromHeight, climbingArea.SpawnRingHeight, s));

        if (t >= 1f) Deactivate();
    }

    void Deactivate()
    {
        figure.gameObject.SetActive(false);
        _climb   = 0f;
        _darting = false;
        _state   = State.Dormant;
        if (debugLogs) Debug.Log("[CreepyGuy] Dormant — down at the spawn ring.", this);
    }

    // ─────────────────────────────────────────────
    // CREEPING
    // ─────────────────────────────────────────────

    void UpdateCreep(Transform boat, float distance)
    {
        // Climb thresholds are measured outward from the jump band, so he is always topped out
        // before he is armed — the two can no longer be tuned into conflict.
        float climbStart = attackBandOuter + climbLead;
        float climbEnd   = attackBandOuter + climbTopMargin;

        // Climb target: 0 on the wide low ring, 1 on the tight high one.
        float climbTarget = climbStart > climbEnd
            ? Mathf.Clamp01(Mathf.InverseLerp(climbStart, climbEnd, distance))
            : (distance <= climbEnd ? 1f : 0f);

        // Right up against the rock he comes back down it — the top is no use to him at point
        // blank, and he cannot pounce from the lower ring, so ducking also stands the attack down.
        if (distance <= duckRadius) climbTarget = 0f;

        float hideAngle = HideAngle(boat);

        if (_darting)
        {
            _dartElapsed += Time.deltaTime;
            float t = dartDuration > 0.0001f ? Mathf.Clamp01(_dartElapsed / dartDuration) : 1f;

            // SmoothStep, not linear — a dart that eases out of the move lands rather than stops.
            _angle = Mathf.Lerp(_dartFrom, _dartTo, Mathf.SmoothStep(0f, 1f, t));

            // He only gains height while actually moving, so the climb comes in scrambles too
            // rather than gliding up the cone while he is supposed to be frozen.
            _climb = Mathf.MoveTowards(_climb, climbTarget, climbSpeed * Time.deltaTime);

            if (t >= 1f)
            {
                _darting       = false;
                _restRemaining = dartRest + Random.Range(0f, Mathf.Max(0f, dartRestRandom));
            }
        }
        else
        {
            _restRemaining -= Time.deltaTime;

            // Exposure builds while he sits still and the boat moves — so how often he darts is
            // driven entirely by how the player is moving. Park the boat and he never twitches.
            float angleError = Mathf.DeltaAngle(_angle, hideAngle);
            bool  exposed    = Mathf.Abs(angleError) >= dartTriggerAngle;
            bool  needsClimb = Mathf.Abs(climbTarget - _climb) >= climbTriggerDelta;

            if (_restRemaining <= 0f && (exposed || needsClimb))
            {
                _dartFrom    = _angle;
                _dartTo      = _angle + Mathf.Clamp(angleError, -maxDartAngle, maxDartAngle);
                _dartElapsed = 0f;
                _darting     = true;
            }
        }

        ApplyOrbit();
    }

    void ApplyOrbit()
    {
        // The cone means radius and height move together — blending both by the same climb
        // value keeps him on the surface all the way up.
        PlaceOnRing(Mathf.Lerp(climbingArea.LowerRingRadius, climbingArea.UpperRingRadius, _climb),
                    Mathf.Lerp(climbingArea.LowerRingHeight, climbingArea.UpperRingHeight, _climb));
    }

    // Bearing of the far side of the rock from the boat.
    float HideAngle(Transform boat) => climbingArea.BearingOf(boat.position) + 180f;

    // Puts him at the current bearing on a ring of the given radius and height. Shared by the
    // creep, the climb and the crawl-up so they cannot drift apart. World space via the rock's
    // own transform, so a scaled rock gets proportionally scaled rings while he does not scale.
    void PlaceOnRing(float radius, float height)
    {
        Vector3 p = climbingArea.RingPointToWorld(_angle, radius, height);
        p.y -= modelYOffset;
        figure.position = p;

        if (faceOutward)
            figure.rotation = climbingArea.RingRotation(_angle + facingYawOffset + ringFacingOffset);
    }

    // ─────────────────────────────────────────────
    // STREET LIGHT — FLEEING
    // ─────────────────────────────────────────────

    // Nothing is added to the lighting code for this: StreetLightController already keeps LitLights
    // up to date in SetLit(), which is the single place a lamp comes on.
    static bool IsRockLit(CreepClimbingArea rock)
    {
        if (rock == null) return false;

        foreach (var lamp in StreetLightController.LitLights)
        {
            if (lamp == null) continue;
            if (FlatDistance(rock.CentreWorld, lamp.InstLightPosition) <= lamp.InstLightRadius) return true;
        }
        return false;
    }

    /// <summary>
    /// Fewest hops from a rock to one outside every lit radius. Intermediate rocks may themselves
    /// be lit — he will cross a lit rock to reach a dark one. Returns false when there is no way
    /// out at all, which is when he gives up and climbs down. (Breadth-first over the rocks.)
    /// </summary>
    bool FindEscapeRoute(CreepClimbingArea from)
    {
        _escapeRoute.Clear();

        var rocks = CreepClimbingArea.All;
        int count = rocks.Count;
        int start = rocks.IndexOf(from);
        if (start < 0) return false;

        _routeSeen.Clear();
        _routeParent.Clear();
        for (int i = 0; i < count; i++) { _routeSeen.Add(false); _routeParent.Add(-1); }

        _routeQueue.Clear();
        _routeSeen[start] = true;
        _routeQueue.Enqueue(start);

        int goal = -1;
        while (_routeQueue.Count > 0 && goal < 0)
        {
            int at = _routeQueue.Dequeue();
            for (int to = 0; to < count; to++)
            {
                if (_routeSeen[to] || rocks[to] == null) continue;
                if (!rocks[to].HasRoomFor(this)) continue;      // occupied — not a way out
                if (FlatDistance(rocks[at].CentreWorld, rocks[to].CentreWorld) > maxHopDistance) continue;

                _routeSeen[to]   = true;
                _routeParent[to] = at;

                if (!IsRockLit(rocks[to])) { goal = to; break; }
                _routeQueue.Enqueue(to);
            }
        }

        if (goal < 0) return false;

        // Walk the parents back to the start, then flip it so the route reads forwards.
        for (int at = goal; at >= 0; at = _routeParent[at]) _escapeRoute.Add(rocks[at]);
        _escapeRoute.Reverse();
        return true;
    }

    // Fleeing outranks everything — he abandons a dart, a climb or an arming wind-up to go.
    void FleeTheLight()
    {
        if (!FindEscapeRoute(climbingArea) || _escapeRoute.Count < 2)
        {
            if (debugLogs) LogWhyNoEscape();
            _escapeRoute.Clear();
            BeginRetreat();
            return;
        }

        if (debugLogs)
            Debug.Log($"[CreepyGuy] Lit — escaping via {_escapeRoute.Count - 1} hop(s) to '{_escapeRoute[_escapeRoute.Count - 1].name}'.", this);

        BeginHop(_escapeRoute[1], Boat, fleeSpeedMultiplier);
    }

    // Spells out why he had nowhere to go, rather than leaving "climbing down" as the only clue.
    void LogWhyNoEscape()
    {
        int total = CreepClimbingArea.All.Count;
        int inReach = 0, inReachUnlit = 0;

        foreach (var rock in CreepClimbingArea.All)
        {
            if (rock == null || rock == climbingArea) continue;
            if (FlatDistance(rock.CentreWorld, climbingArea.CentreWorld) > maxHopDistance) continue;
            inReach++;
            if (!IsRockLit(rock)) inReachUnlit++;
        }

        string why =
            total <= 1                 ? "his is the only rock in the level — add CreepClimbingArea to other rocks" :
            inReach == 0               ? $"no rock is within maxHopDistance ({maxHopDistance:0.##}) of this one" :
            inReachUnlit == 0          ? "every rock he can reach is also lit, and none of them lead anywhere dark" :
                                         "no chain of hops reaches an unlit rock";

        Debug.Log($"[CreepyGuy] Lit with no way out — climbing down. Rocks: {total} total, " +
                  $"{inReach} within reach, {inReachUnlit} of those unlit. Reason: {why}", this);
    }

    // While he is lit, the whole picture is drawn — the route when he has one, and otherwise every
    // rock he can reach, so an escape that is not possible is as visible as one that is.
    void DrawEscapeRoute()
    {
        if (!showEscapeRoute || climbingArea == null) return;

        Color lit    = new Color(1f, 0.65f, 0.15f);
        Color unlit  = Color.green;

        if (_escapeRoute.Count >= 2)
        {
            for (int i = 0; i < _escapeRoute.Count - 1; i++)
            {
                if (_escapeRoute[i] == null || _escapeRoute[i + 1] == null) continue;
                bool lastLeg = i == _escapeRoute.Count - 2;
                Debug.DrawLine(_escapeRoute[i].CentreWorld, _escapeRoute[i + 1].CentreWorld,
                               lastLeg ? unlit : lit);
            }
            return;
        }

        // No route — show what he could reach, so it is obvious whether the problem is that
        // nothing is in range or that everything in range is lit.
        if (!IsRockLit(climbingArea)) return;

        foreach (var rock in CreepClimbingArea.All)
        {
            if (rock == null || rock == climbingArea) continue;
            if (FlatDistance(rock.CentreWorld, climbingArea.CentreWorld) > maxHopDistance) continue;
            Debug.DrawLine(climbingArea.CentreWorld, rock.CentreWorld, IsRockLit(rock) ? lit : unlit);
        }
    }

    // ─────────────────────────────────────────────
    // HOPPING
    // ─────────────────────────────────────────────

    // He hops only to get within pouncing range, never to get ahead of the boat. So: if he could
    // already reach it from here, he stays put — which is what keeps him unhurried.
    // The best rock to move to, or null if he is already on it. No distance gate any more — being
    // able to pounce from here no longer stops him preferring a better rock; that is now a weighing
    // in ChooseHopOrAmbush rather than a hard switch.
    CreepClimbingArea FindHopTarget(Transform boat, float distance)
    {
        if (_darting || Time.time < _nextHopAt) return null;

        // A candidate has to beat where he already is by minHopGain, not merely tie it. Applying
        // the same margin in both directions is what gives the hysteresis that stops the bouncing.
        CreepClimbingArea best     = null;
        float             bestDist = distance - minHopGain;

        foreach (var area in CreepClimbingArea.All)
        {
            if (area == null || area == climbingArea) continue;
            if (!area.HasRoomFor(this)) continue;          // another creeper already has it

            // Never move into the light of his own accord. Without this he hops onto a lit rock
            // because it is closer to the boat, immediately flees it, then hops back — an endless
            // loop at the edge of a lamp's radius.
            if (IsRockLit(area)) continue;

            // Centre to centre, so which rocks are reachable is fixed by the level layout.
            if (FlatDistance(area.CentreWorld, climbingArea.CentreWorld) > maxHopDistance) continue;

            // Strictly closer to the boat than where he stands. Because his new rock is then the
            // closest one, he cannot hop straight back — no oscillation without a fudge margin.
            float d = FlatDistance(area.CentreWorld, boat.position);
            if (d < bestDist) { bestDist = d; best = area; }
        }

        return best;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    void BeginHop(CreepClimbingArea target, Transform boat, float speedMultiplier)
    {
        // He lands on the NEAREST point of the new rock's ring — the bearing of where he is jumping
        // from, so the crossing is the shortest one the geometry allows, whatever side that is.
        // Front and back stop being a concept; the normal darting takes him round to hide after.
        _hopTargetArea  = target;
        _hopTargetAngle = target.BearingOf(figure.position);

        // Claimed on departure, not arrival — otherwise two creepers can be mid-air to the same
        // rock at once and both land on it.
        climbingArea.Release(this);
        target.Claim(this);

        _hopFrom = figure.position;
        _hopTo   = target.RingPointToWorld(_hopTargetAngle, target.LowerRingRadius, target.LowerRingHeight);
        _hopTo.y -= modelYOffset;

        _hopElapsed  = 0f;
        float speed  = Mathf.Max(0.01f, hopSpeed * Mathf.Max(0.01f, speedMultiplier));
        _hopDuration = Mathf.Max(0.1f, Vector3.Distance(_hopFrom, _hopTo) / speed);

        Vector3 travel = _hopTo - _hopFrom;
        travel.y = 0f;
        _hopRot  = travel.sqrMagnitude > 0.0001f ? FaceDir(travel.normalized) : figure.rotation;

        if (animator != null && !string.IsNullOrEmpty(attackJumpState))
            animator.CrossFadeInFixedTime(attackJumpState, crossFadeTime);

        _state = State.Hopping;
        if (debugLogs)
            Debug.Log($"[CreepyGuy] HOP to '{target.name}' — {Vector3.Distance(_hopFrom, _hopTo):0.##} units " +
                      $"over {_hopDuration:0.##}s, landing at bearing {_hopTargetAngle:0.#}°.", this);
    }

    void UpdateHop()
    {
        _hopElapsed += Time.deltaTime;
        float n = Mathf.Clamp01(_hopElapsed / _hopDuration);

        Vector3 p = Vector3.Lerp(_hopFrom, _hopTo, n);
        p.y += hopHeight * Mathf.Sin(n * Mathf.PI);

        figure.position = p;
        figure.rotation = _hopRot;

        if (n >= 1f) ArriveOnRock();
    }

    void ArriveOnRock()
    {
        climbingArea = _hopTargetArea;
        _angle       = _hopTargetAngle;

        // Low on the new rock, and the climb starts again from scratch as the boat closes.
        _climb         = 0f;
        _darting       = false;
        _restRemaining = 0f;
        _nextHopAt     = Time.time + hopRest;

        // Out of the light — the route has done its job and stops drawing. If he is still lit the
        // Creeping case picks it up next frame and re-routes from here.
        if (!IsRockLit(climbingArea)) _escapeRoute.Clear();

        ApplyOrbit();
        _prevAngle = _angle;   // he did not "turn" to get here — do not lean from the hop

        // Clip is not set here: back on the rock, the movement driver owns still vs moving.
        _state = State.Creeping;
        if (debugLogs) Debug.Log($"[CreepyGuy] Landed on '{climbingArea.name}'.", this);
    }

    // ─────────────────────────────────────────────
    // AMBUSH
    // ─────────────────────────────────────────────

    // Repositioning and attacking are weighed against each other rather than one always winning.
    // Rolled once here, and both outcomes leave Creeping immediately, so a single approach cannot
    // flip back and forth between the two.
    void ChooseHopOrAmbush(Transform boat, float distance)
    {
        CreepClimbingArea hopTarget = FindHopTarget(boat, distance);
        bool canPounce = AmbushReady(boat, distance, out float facing);

        if (hopTarget != null && (!canPounce || Random.value < hopPreference))
        {
            BeginHop(hopTarget, boat, 1f);
            return;
        }

        if (canPounce) BeginWindUp(facing);
    }

    bool AmbushReady(Transform boat, float distance, out float facing)
    {
        facing = 0f;

        // Outside the band in either direction he just keeps hiding.
        if (distance < attackBandInner || distance > attackBandOuter) return false;

        // He leaps from the top of the cone only, and never mid-dart.
        if (_climb < 0.99f || _darting) return false;

        if (BoatSpeed < minBoatSpeed) return false;

        // Measured against him, not the rock — he is what you failed to spot.
        Vector3 toCreep = figure.position - boat.position;
        toCreep.y = 0f;
        if (toCreep.sqrMagnitude < 0.0001f) return false;

        facing = Vector3.Dot(boat.forward, toCreep.normalized);
        if (facing >= notFacingDot) return false;

        if (Time.time < _blockedRearmAt) return false;

        return true;
    }

    // The tell. He drops the shuffle, assumes the jump-ready pose and turns to face the boat.
    // Aiming deliberately does NOT happen here — it happens at the end of the wind-up, so the
    // lead is computed from where the boat actually is when he launches.
    void BeginWindUp(float facing)
    {
        _windUpElapsed = 0f;
        _armedFacing   = facing;

        if (animator != null && !string.IsNullOrEmpty(attackJumpReadyState))
            animator.CrossFadeInFixedTime(attackJumpReadyState, crossFadeTime);

        _state = State.WindUp;
        if (debugLogs) Debug.Log($"[CreepyGuy] ARMED — jump-ready pose, facing={facing:0.##}", this);
    }

    // No turn-to-face here. He hides at boatBearing + 180° and faces inward, so he is already
    // pointed back through the rock at the boat — the orbit guarantees it. The only facing
    // computed anywhere is the leap's, at launch.
    void UpdateWindUp(Transform boat)
    {
        _windUpElapsed += Time.deltaTime;
        if (_windUpElapsed < attackWindUpTime) return;

        // blockAttackJump lets the whole tell play out — arming, the ready pose, the pause — and only
        // withholds the leap itself, dropping him back to creeping so the same rock can be
        // triggered over and over instead of being spent after one pass.
        if (blockAttackJump)
        {
            if (debugLogs)
                Debug.Log($"[CreepyGuy] JUMP BLOCKED (blockAttackJump) — would have launched now. " +
                          $"speed={BoatSpeed:0.##}, facing at arming={_armedFacing:0.##}", this);

            _blockedRearmAt = Time.time + BlockedRearmDelay;
            ReturnToCreeping();
            return;
        }

        BeginAttackJump(boat);
    }

    // World rotation that aims the model along its direction of travel. Uses attackJumpFacingOffset, not
    // ringFacingOffset: the ring value is measured against the rock's radial, this one against the
    // travel direction, and the two are only the same if he flies the way he clings.
    Quaternion FaceDir(Vector3 dir) =>
        Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, facingYawOffset + attackJumpFacingOffset, 0f);

    void ReturnToCreeping()
    {
        // Clip is not set here — the movement driver picks still or moving from what he does next.
        _darting       = false;
        _restRemaining = 0f;
        _state         = State.Creeping;
    }

    void BeginAttackJump(Transform boat)
    {
        _attackStart   = figure.position;
        _attackElapsed = 0f;

        // Aim once, at where the boat will be on arrival, then never re-aim. Holding your
        // heading gets you hit; reacting gets you clear.
        Vector3 target = boat.position + boat.forward * BoatSpeed * attackJumpDuration * attackLeadFactor;

        Vector3 flat = target - _attackStart;
        flat.y = 0f;
        if (flat.magnitude > maxAttackJumpDistance)
            target = _attackStart + flat.normalized * maxAttackJumpDistance;

        // PHASE ONE aims at the boat's own height, not the water — so the lunge genuinely arrives
        // at the boat with his collider on it. The drop is phase two's job.
        target.y    = boat.position.y;
        _attackTarget = target;

        Vector3 faceDir = _attackTarget - _attackStart;
        faceDir.y = 0f;
        _attackRot  = faceDir.sqrMagnitude > 0.0001f
            ? FaceDir(faceDir.normalized)
            : figure.rotation;

        // Fixed-time, not CrossFade — CrossFade's duration is normalised to the SOURCE clip's
        // length, so a long idle clip would stretch the blend past the whole flight.
        if (animator != null && !string.IsNullOrEmpty(attackJumpState))
            animator.CrossFadeInFixedTime(attackJumpState, crossFadeTime);
        if (hitCollider != null) hitCollider.enabled = true;

        _state = State.AttackJumping;

        if (debugLogs)
            Debug.Log($"[CreepyGuy] JUMP — start={_attackStart} target={_attackTarget} " +
                      $"travel={Vector3.Distance(_attackStart, _attackTarget):0.##} rise={_attackStart.y - _attackTarget.y:0.##} | " +
                      $"boat={boat.position} boatDistToRock={Vector3.Distance(boat.position, CentreWorld):0.##} " +
                      $"speed={BoatSpeed:0.##} | waterY={WaterY:0.###} facingAtArming={_armedFacing:0.##}", this);
    }

    // Kinematic arc: a straight line from launch to landing plus a sine hump. sin(πn) is 0 at
    // both ends and 1 at the midpoint, so attackJumpHeight is literally how far he peaks above that
    // line. Driven rather than physical so a graze off the boat cannot deflect him mid-flight.
    void UpdateAttackJump()
    {
        _attackElapsed += Time.deltaTime;
        float n = attackJumpDuration > 0.0001f ? Mathf.Clamp01(_attackElapsed / attackJumpDuration) : 1f;

        Vector3 p = Vector3.Lerp(_attackStart, _attackTarget, n);
        p.y += attackJumpHeight * Mathf.Sin(n * Mathf.PI);

        figure.position = p;
        figure.rotation = _attackRot;

        if (n >= 1f) BeginFall();
    }

    // ─────────────────────────────────────────────
    // PHASE TWO — THE FALL
    // ─────────────────────────────────────────────

    // Picks up exactly where the lunge left off: the same horizontal speed, and the arc's own
    // vertical speed at the instant it ended, so there is no kink between the two phases.
    void BeginFall()
    {
        Vector3 flat = _attackTarget - _attackStart;
        flat.y = 0f;

        // d/dt of  lerp(y0,y1,n) + h·sin(πn)  evaluated at n = 1, where cos(π) = -1.
        float vY = ((_attackTarget.y - _attackStart.y) - attackJumpHeight * Mathf.PI) / attackJumpDuration;

        _fallVelocity = flat / attackJumpDuration;
        _fallVelocity.y = vY;

        _state = State.Falling;
        if (debugLogs)
            Debug.Log($"[CreepyGuy] Lunge done at {figure.position} — falling to " +
                      $"y={WaterY - landingDrop:0.##} (v={_fallVelocity}).", this);
    }

    void UpdateFall()
    {
        float waterY = WaterY;

        if (figure.position.y > waterY)
        {
            // Above the surface — accelerating plummet.
            _fallVelocity.y -= fallGravity * Time.deltaTime;
        }
        else
        {
            // In the water — gravity and horizontal drift both stop dead, replaced by a slow
            // constant sink. The abrupt change at the surface is the point: it reads as entry.
            _fallVelocity = Vector3.down * sinkSpeed;
            if (animator != null && !string.IsNullOrEmpty(sinkState))
            animator.CrossFadeInFixedTime(sinkState, crossFadeTime);

        }

        figure.position += _fallVelocity * Time.deltaTime;
        figure.rotation  = _attackRot;

        if (figure.position.y <= waterY - landingDrop) Land();
    }

    // ─────────────────────────────────────────────
    // RESPAWN — CRAWLING BACK UP
    // ─────────────────────────────────────────────

    // He surfaces on the far side of the rock, not wherever he happened to drown, so he is never
    // seen reappearing. The whole creep/climb/ambush cycle is reset and armed again.
    void BeginEmerge(Transform boat, CreepClimbingArea rock)
    {
        // He does not necessarily come back where he went down — he surfaces at whichever unlit
        // rock is nearest the boat, so being put in the water costs him position, not the chase.
        if (rock != null && rock != climbingArea)
        {
            climbingArea.Release(this);
            rock.Claim(this);
            if (debugLogs) Debug.Log($"[CreepyGuy] Surfacing at '{rock.name}' instead of '{climbingArea.name}'.", this);
            climbingArea = rock;
        }

        // Always re-claim: Land() released his rock when he went in the water, so even coming back
        // up the same one he has to take it again.
        climbingArea.Claim(this);

        _escapeRoute.Clear();
        _angle         = HideAngle(boat);
        _climb         = 0f;
        _darting       = false;
        _restRemaining = 0f;
        _crawlElapsed  = 0f;

        PlaceOnRing(climbingArea.SpawnRingRadius, climbingArea.SpawnRingHeight);

        figure.gameObject.SetActive(true);
        if (hitCollider != null) hitCollider.enabled = false;

        // Clip is not set here — the climb is movement, so the driver plays the moving clip at
        // whatever speed the crawl is actually going.
        _prevAngle = _angle;
        _state     = State.Emerging;
        if (debugLogs) Debug.Log($"[CreepyGuy] Emerging from the spawn ring at {_angle:0.#}°.", this);
    }

    // Nearest UNLIT rock to the boat within awareness range. Null when nothing qualifies, in which
    // case he stays down and checks again — better to wait than to surface into a light.
    CreepClimbingArea PickSurfacingRock(Transform boat)
    {
        CreepClimbingArea best     = null;
        float             bestDist = activationRadius;

        foreach (var area in CreepClimbingArea.All)
        {
            if (area == null || IsRockLit(area)) continue;
            if (!area.HasRoomFor(this)) continue;
            float d = FlatDistance(area.CentreWorld, boat.position);
            if (d < bestDist) { bestDist = d; best = area; }
        }
        return best;
    }

    void UpdateEmerge()
    {
        _crawlElapsed += Time.deltaTime;
        float t = crawlUpDuration > 0.0001f ? Mathf.Clamp01(_crawlElapsed / crawlUpDuration) : 1f;
        float s = Mathf.SmoothStep(0f, 1f, t);

        PlaceOnRing(Mathf.Lerp(climbingArea.SpawnRingRadius, climbingArea.LowerRingRadius, s),
                    Mathf.Lerp(climbingArea.SpawnRingHeight, climbingArea.LowerRingHeight, s));

        if (t >= 1f)
        {
            _state = State.Creeping;
            if (debugLogs) Debug.Log("[CreepyGuy] Back on the rock — creeping again.", this);
        }
    }

    void Land()
    {
        // No snap to _attackTarget — that is the LUNGE's endpoint, up at boat height, and snapping
        // back to it would yank him out of the water he just fell into.
        if (hitCollider != null) hitCollider.enabled = false;

        if (splashPrefab != null)
            Instantiate(splashPrefab, figure.position, Quaternion.identity);

        _state = State.Fallen;
        climbingArea.Release(this);   // in the water, holding nothing — someone else may take it

        // Only hidden now he has sunk the full landingDrop below the water — he stays visible for
        // the whole descent, and vanishes out of sight rather than in front of you.
        if (respawnDelay > 0f)
        {
            figure.gameObject.SetActive(false);
            _respawnAt = Time.time + respawnDelay;
        }

        if (debugLogs)
            Debug.Log($"[CreepyGuy] Landed at {figure.position} — " +
                      (respawnDelay > 0f ? $"back in {respawnDelay:0.##}s." : "down for good."), this);
    }

    // ─────────────────────────────────────────────
    // VALIDATION
    // ─────────────────────────────────────────────

    void OnValidate()
    {
        attackBandInner = Mathf.Max(0f, attackBandInner);
        attackBandOuter = Mathf.Max(attackBandInner, attackBandOuter);
        duckRadius    = Mathf.Clamp(duckRadius, 0f, attackBandInner);

        // He must be awake during the window he can attack in.
        activationRadius = Mathf.Max(attackBandOuter + 0.5f, activationRadius);
        deactivateBuffer = Mathf.Max(0f, deactivateBuffer);

        // Both measured outward from the band, and he must top out before he reaches it.
        climbTopMargin = Mathf.Max(0f, climbTopMargin);
        climbLead      = Mathf.Max(climbTopMargin + 0.1f, climbLead);
        climbSpeed     = Mathf.Max(0.01f, climbSpeed);

        maxHopDistance  = Mathf.Max(0f, maxHopDistance);
        hopSpeed        = Mathf.Max(0.01f, hopSpeed);
        hopRest         = Mathf.Max(0f, hopRest);
        minHopGain      = Mathf.Max(0f, minHopGain);
        fleeSpeedMultiplier = Mathf.Max(0.01f, fleeSpeedMultiplier);

        respawnDelay    = Mathf.Max(0f, respawnDelay);
        crawlUpDuration = Mathf.Max(0.01f, crawlUpDuration);

        dartDuration     = Mathf.Max(0.01f, dartDuration);
        dartRest         = Mathf.Max(0f, dartRest);
        dartRestRandom   = Mathf.Max(0f, dartRestRandom);
        dartTriggerAngle = Mathf.Max(0.1f, dartTriggerAngle);
        maxDartAngle     = Mathf.Clamp(maxDartAngle, 1f, 180f);

        attackWindUpTime   = Mathf.Max(0f, attackWindUpTime);
        attackJumpDuration = Mathf.Max(0.01f, attackJumpDuration);
        landingDrop  = Mathf.Max(0f, landingDrop);
        fallGravity  = Mathf.Max(0.01f, fallGravity);
        sinkSpeed    = Mathf.Max(0.01f, sinkSpeed);
        maxLeanAngle = Mathf.Max(0f, maxLeanAngle);
        leanResponse = Mathf.Max(1f, leanResponse);

        movementBlendTime      = Mathf.Max(0f, movementBlendTime);
        movingThreshold        = Mathf.Max(0f, movingThreshold);
        moveAnimReferenceSpeed = Mathf.Max(0.01f, moveAnimReferenceSpeed);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 centre = CentreWorld;
        Vector3 plane  = new Vector3(centre.x, WaterY, centre.z);

        // Activation ring — where he wakes up.
        Handles.color = new Color(1f, 0.9f, 0.2f, 0.55f);
        Handles.DrawWireDisc(plane, Vector3.up, activationRadius);
        Handles.Label(plane + Vector3.right * activationRadius, $"activate r={activationRadius:0.##}");

        // Jump band — armed only between the two rings.
        // Ducks down — right up against the rock he drops back to the lower ring.
        Handles.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        Handles.DrawWireDisc(plane, Vector3.up, duckRadius);
        Handles.Label(plane + Vector3.back * duckRadius, $"ducks down r={duckRadius:0.##}");

        Handles.color = new Color(1f, 0.25f, 0.2f, 0.9f);
        Handles.DrawWireDisc(plane, Vector3.up, attackBandInner);
        Handles.DrawWireDisc(plane, Vector3.up, attackBandOuter);
        Handles.Label(plane + Vector3.forward * attackBandOuter, $"attack band {attackBandInner:0.##} → {attackBandOuter:0.##}");

        // Radial ticks so the annulus itself reads as the band, not just two loose circles.
        Handles.color = new Color(1f, 0.25f, 0.2f, 0.35f);
        for (int i = 0; i < 12; i++)
        {
            float a = i * (360f / 12f) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            Handles.DrawLine(plane + dir * attackBandInner, plane + dir * attackBandOuter);
        }

        // Climb thresholds, both measured outward from the band so they cannot conflict with it.
        float climbStart = attackBandOuter + climbLead;
        float climbEnd   = attackBandOuter + climbTopMargin;
        Handles.color = new Color(0.4f, 1f, 0.5f, 0.45f);
        Handles.DrawWireDisc(plane, Vector3.up, climbStart);
        Handles.Label(plane + Vector3.left * climbStart, $"climb starts r={climbStart:0.##}");
        Handles.DrawWireDisc(plane, Vector3.up, climbEnd);
        Handles.Label(plane + Vector3.left * climbEnd, $"fully up r={climbEnd:0.##}");

        // Hop reach — every rock whose centre falls inside this is one he can jump to from here.
        // Measured centre to centre, which is why it is drawn from the rock's centre, not from him.
        Handles.color = new Color(0.25f, 0.95f, 0.35f, 0.9f);
        Handles.DrawWireDisc(plane, Vector3.up, maxHopDistance);
        Handles.Label(plane + Vector3.forward * maxHopDistance, $"hop reach r={maxHopDistance:0.##}");

        // Rocks actually within reach, so a gap that is too wide to cross shows up here as well
        // as in the Grid Designer. Only meaningful in play, when the registry is populated.
        if (Application.isPlaying && climbingArea != null)
        {
            foreach (var area in CreepClimbingArea.All)
            {
                if (area == null || area == climbingArea) continue;

                Vector3 a = climbingArea.CentreWorld; a.y = plane.y;
                Vector3 b = area.CentreWorld;         b.y = plane.y;
                if (Vector3.Distance(a, b) > maxHopDistance) continue;

                Handles.color = new Color(0.25f, 0.95f, 0.35f, 0.9f);
                Handles.DrawAAPolyLine(5f, a, b);
            }
        }

        // The rings themselves are the rock's, not his — select the CreepClimbingArea to see them.
    }
#endif
}
