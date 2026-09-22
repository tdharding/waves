using System;
using UnityEngine;

/// <summary>
/// Shape of the door standing in an arena's archway — the keyhole, and the soul opening at its
/// foot that the souls come through.
///
/// The door is a sheet filling the arch's opening with a keyhole frame standing proud on it, and
/// a panel filling the keyhole inside that frame. The sheet is solid everywhere except the soul
/// opening, which is cut clean through, so from the river you look through the flap and see the
/// way in and nowhere else.
///
///            ___
///          /     \          <- bulb radius
///         |       |
///          \     /
///           |   |           <- waist width, at waist height
///          /     \
///         |  ___  |         <- the soul opening: its own little arch
///         | |   | |
///       __|_|___|_|__       <- the door's foot, standing on the water
///        base width
///
/// <see cref="height"/> and <see cref="baseWidth"/> set to 0 mean "fill the arch" — the same
/// 0-means-inherit convention <see cref="ArenaArchwayProfile"/> uses against the river. They are
/// authored outright by default, though, because a door is a shape you want to look at rather
/// than one that should quietly restate whatever arch it lands in. Every other number is
/// absolute, because a rim that changed width with its arch would be a nuisance to author
/// against.
///
/// Nothing here is clamped against anything else. A number typed in is the number built, even
/// where it puts one part of the door through another — the author asked for exactly that, and
/// seeing the shape go wrong is how you find the number you wanted.
/// </summary>
[Serializable]
public class ArenaDoorProfile
{
    [Header("The keyhole")]
    [Tooltip("Top of the bulb, measured up from the water the door stands on. Set it to 0 to " +
             "take the arch opening's own height above the water, so the door fills it.")]
    [Min(0f)] public float height = 2f;

    [Tooltip("Width across the foot of the door. Set it to 0 to take the arch's opening " +
             "width, so the door spans it exactly.")]
    [Min(0f)] public float baseWidth = 1f;

    [Tooltip("Radius of the round bulb at the top.")]
    [Min(0.001f)] public float bulbRadius = 0.7f;

    [Tooltip("Width across the narrowest part, under the bulb.")]
    [Min(0.001f)] public float waistWidth = 0.6f;

    [Tooltip("How far up the door the waist sits, measured from the water. Below it the sides " +
             "run straight out to the foot; above it they curve up into the bulb.")]
    [Min(0f)] public float waistHeight = 0.8f;

    [Tooltip("How far the bottom edge sags below the two base corners, which sit on the water. " +
             "0 cuts the door off flat at the water; anything more closes it underneath with a " +
             "curve, so the frame runs all the way round and the soul opening sits inside it.")]
    [Min(0f)] public float baseCurve = 0.3f;

    [Header("Rims")]
    [Tooltip("Width of the frame band running round the keyhole. The sheet behind shows through " +
             "inside it.")]
    [Min(0.001f)] public float frameWidth = 0.1f;

    [Tooltip("How far the frame and the flap rim stand proud of the sheet, toward the river.")]
    [Min(0f)] public float frameDepth = 0.3f;

    [Tooltip("Width of the rim band running round the soul opening.")]
    [Min(0.001f)] public float flapRimWidth = 0.1f;

    [Tooltip("How far the panel inside the frame stands proud of the sheet — the leaf of the " +
             "door itself, filling the keyhole with the soul opening left out of it. Less than " +
             "Stand Proud sits it in the frame; 0 builds no panel and leaves the plain sheet " +
             "showing behind the frame.")]
    [Min(0f)] public float panelDepth = 0.15f;

    [Header("The soul opening")]
    [Tooltip("Width of the opening the souls come through, cut clean through the sheet.")]
    [Min(0.001f)] public float flapWidth = 0.3f;

    [Tooltip("The straight part of the opening's sides, up from the water.")]
    [Min(0f)] public float flapLegHeight = 0.3f;

    [Tooltip("The curved part above them. Half the flap width gives a plain semicircle; more " +
             "gives a taller opening.")]
    [Min(0.001f)] public float flapCrownHeight = 0.2f;

    [Tooltip("How far the opening's bottom edge sags below the water, the way the door's own " +
             "base curve does. 0 stands it flat on the water; more closes it underneath, so its " +
             "rim runs all the way round and reads as a ring under the door's. " +
             "It has to share the room under the door with the frame: this plus Flap Rim Width " +
             "has to stay under Base Curve minus Frame Width, or the two rims meet underneath.")]
    [Min(0f)] public float flapCurve = 0.05f;

    [Header("The numeral disc")]
    [Tooltip("Radius of the round disc standing on the bulb, which carries the arena's numeral. " +
             "0 leaves the bulb plain and the numeral is drawn straight on it, spanning the bulb " +
             "rather than the disc and lifted off the panel instead of the disc's face.")]
    [Min(0f)] public float discRadius = 0.45f;

    [Tooltip("How far the disc stands proud of the sheet, toward the river. Independent of the " +
             "rims' Stand Proud, so the numeral can sit shallower than the frame around it.")]
    [Min(0f)] public float discDepth = 0.06f;

    [Tooltip("How far the disc sits above the middle of the bulb. 0 centres it there; negative " +
             "drops it toward the waist.")]
    public float discRise;

    [Tooltip("How many rings the disc's side is built in, from the sheet out to its face. 1 " +
             "takes it in a single step; more breaks the wall up without changing its shape.")]
    [Min(1)] public int discSideSteps = 1;

    [Tooltip("How much of the disc's width the numeral drawing spans — or the bulb's, when " +
             "there is no disc. 1 fills it corner to corner; 0.7 leaves a margin of stone round " +
             "the number. Over 1 is allowed, and spills the drawing over the edge.")]
    [Min(0f)] public float numeralSize = 0.7f;

    [Tooltip("How far the numeral sits above the middle of the disc. 0 centres it there, and " +
             "negative drops it. Its own, separate from Disc Rise, so the drawing can be " +
             "nudged on the disc without moving the disc on the door.")]
    public float numeralRise;

    [Tooltip("How far the numeral floats off the face of the disc. Only enough to keep the two " +
             "from fighting over which is in front — a larger number reads as the numeral " +
             "hovering.")]
    [Min(0f)] public float numeralLift = 0.004f;

    public ArenaDoorProfile Clone() => (ArenaDoorProfile)MemberwiseClone();

    /// <summary>
    /// Takes every number from another shape, in place. The designer holds one of these per
    /// arena and hands it out by reference, so loading a preset has to fill the shape that is
    /// already there rather than swap in a new one.
    /// </summary>
    public void CopyFrom(ArenaDoorProfile other)
    {
        if (other == null) return;

        height          = other.height;
        baseWidth       = other.baseWidth;
        baseCurve       = other.baseCurve;
        bulbRadius      = other.bulbRadius;
        waistWidth      = other.waistWidth;
        waistHeight     = other.waistHeight;
        frameWidth      = other.frameWidth;
        frameDepth      = other.frameDepth;
        flapRimWidth    = other.flapRimWidth;
        panelDepth      = other.panelDepth;
        flapWidth       = other.flapWidth;
        flapLegHeight   = other.flapLegHeight;
        flapCrownHeight = other.flapCrownHeight;
        flapCurve       = other.flapCurve;
        discRadius      = other.discRadius;
        discDepth       = other.discDepth;
        discRise        = other.discRise;
        discSideSteps   = other.discSideSteps;
        numeralRise     = other.numeralRise;
        numeralSize     = other.numeralSize;
        numeralLift     = other.numeralLift;
    }

    /// <summary>Identifies the shape, so meshes can be reused when nothing changed.</summary>
    public string ShapeKey =>
        $"{height:F4}|{baseWidth:F4}|{baseCurve:F4}|{bulbRadius:F4}|{waistWidth:F4}|{waistHeight:F4}|" +
        $"{frameWidth:F4}|{frameDepth:F4}|{flapRimWidth:F4}|{panelDepth:F4}|" +
        $"{flapWidth:F4}|{flapLegHeight:F4}|{flapCrownHeight:F4}|{flapCurve:F4}|" +
        $"{discRadius:F4}|{discDepth:F4}|{discRise:F4}|{discSideSteps}|" +
        $"{numeralSize:F4}|{numeralRise:F4}|{numeralLift:F4}";

    /// <summary>
    /// The shape with its two inherited numbers settled against the arch it stands in — what
    /// the mesh is built from.
    ///
    /// <paramref name="arch"/> must already be resolved. <paramref name="waterY"/> is where the
    /// water surface sits in the arch's own frame, which is what the door stands on: below the
    /// rim top, so it is normally negative.
    ///
    /// Nothing is clamped against anything else here — see the note on the class. The mesh is
    /// built at exactly what was typed, and a shape that puts one part through another comes out
    /// looking like it.
    /// </summary>
    public ArenaDoorProfile Resolve(ArenaArchwayProfile arch, float waterY)
    {
        var settled = Clone();

        // Filling the arch means reaching its crown and spanning its legs.
        if (arch != null)
        {
            if (settled.height <= 0.0001f)
                settled.height = arch.legHeight + arch.archHeight - waterY;

            if (settled.baseWidth <= 0.0001f)
                settled.baseWidth = arch.openingWidth;
        }

        // The only floors here are the ones that stop a number being zero, because a zero
        // radius or a zero width has no shape to build rather than a wrong one. Nothing is
        // measured against anything else: the mesh is built at exactly what was typed.
        settled.height          = Mathf.Max(0.001f, settled.height);
        settled.baseWidth       = Mathf.Max(0.001f, settled.baseWidth);
        settled.baseCurve       = Mathf.Max(0f,     settled.baseCurve);
        settled.bulbRadius      = Mathf.Max(0.001f, settled.bulbRadius);
        settled.waistWidth      = Mathf.Max(0.001f, settled.waistWidth);
        settled.waistHeight     = Mathf.Max(0f,     settled.waistHeight);
        settled.frameWidth      = Mathf.Max(0.001f, settled.frameWidth);
        settled.frameDepth      = Mathf.Max(0f,     settled.frameDepth);
        settled.flapRimWidth    = Mathf.Max(0.001f, settled.flapRimWidth);
        settled.panelDepth      = Mathf.Max(0f,     settled.panelDepth);
        settled.flapWidth       = Mathf.Max(0.001f, settled.flapWidth);
        settled.flapLegHeight   = Mathf.Max(0f,     settled.flapLegHeight);
        settled.flapCrownHeight = Mathf.Max(0.001f, settled.flapCrownHeight);
        settled.flapCurve       = Mathf.Max(0f,     settled.flapCurve);
        settled.discRadius      = Mathf.Max(0f,     settled.discRadius);
        settled.discDepth       = Mathf.Max(0f,     settled.discDepth);
        settled.discSideSteps   = Mathf.Max(1,      settled.discSideSteps);
        settled.numeralSize     = Mathf.Max(0f,     settled.numeralSize);
        settled.numeralLift     = Mathf.Max(0f,     settled.numeralLift);

        return settled;
    }
}
