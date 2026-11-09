using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Buildings.Synty
{
    /// <summary>
    /// What a segment of a run is for. `Open` is a hole the tiler routes around and places nothing in.
    ///
    /// `Display` and `Counter` are the two SHOPFRONT roles. They are segment kinds rather than a separate
    /// assembler because a shopfront is not a different kind of wall — it is the same 2.5 m run with different
    /// panels in some of its modules, and routing it through this tiler is what guarantees a shop frontage
    /// closes its remainder exactly as a house frontage does.
    /// </summary>
    public enum SegKind { Plain, Door, Window, Display, DisplayQuiet, Counter, Open }

    /// <summary>
    /// The panel a run may place for each role, in one bag.
    ///
    /// This replaced four positional `string` parameters on `PlaceRun`. Four was already at the limit of what
    /// reads at a call site; the shopfront needs six, and six positional strings is a bug waiting for the day
    /// someone swaps the door and the window arguments. Base courses and parapets, which have no openings at
    /// all, use <see cref="Course"/> and name only the two pieces they actually place.
    /// </summary>
    public struct RunPieces
    {
        /// <summary>2.5 m plain panel.</summary>
        public string Plain;
        /// <summary>1.25 m plain panel — the piece that closes an odd remainder.</summary>
        public string Half;
        public string Door;
        public string Window;
        /// <summary>2.5 m glazed display bay. See <see cref="SyntyWallSet.Display"/>.</summary>
        public string Display;
        /// <summary>The narrower-lit display bay, alternated with <see cref="Display"/> along a frontage.</summary>
        public string DisplayQuiet;
        /// <summary>2.5 m shuttered counter window. See <see cref="SyntyWallSet.CounterWindow"/>.</summary>
        public string Counter;

        /// <summary>A run with no openings in it: a base course, a buried skirt, a parapet, a gable infill.</summary>
        public static RunPieces Course(string plain, string half) =>
            new RunPieces { Plain = plain, Half = half };
    }

    /// <summary>One placed segment of a run, in HALF-MODULES from the run origin.</summary>
    public struct Seg
    {
        /// <summary>Offset from the run origin, in half-modules (1.25 m).</summary>
        public int Start;
        /// <summary>Length in half-modules. Only 1 and 2 are placeable — the kit ships nothing else.</summary>
        public int Len;
        public SegKind Kind;
        public Seg(int start, int len, SegKind kind) { Start = start; Len = len; Kind = kind; }
    }

    /// <summary>
    /// The one and only place a wall run is divided into pieces.
    ///
    /// The brief on the Polyperfect generator is blunt about why this is a separate, shared, unit-tested thing:
    /// three separate bugs there came from the idiom `for (t += 2)` guarded by `t + 2 &lt;= length`, which
    /// silently drops the last metre of an odd run. That failure mode is WORSE in this kit, not better — Synty
    /// ships exactly two wall widths, 2.5 m and 1.25 m, so a dropped remainder is a 1.25 m hole in a wall rather
    /// than a 1 m one, and there is no third size to paper over it with.
    ///
    /// The defence is structural rather than careful: the tiler works entirely in **integer half-modules**, so
    /// there is no float comparison to get wrong, and it asserts that the segments it returns sum EXACTLY to the
    /// run length. A run that cannot be closed is a loud failure at generation time, not a hole discovered in a
    /// screenshot three passes later.
    ///
    /// Everything that runs along a wall line goes through here — exterior skin, interior skin, base plinth,
    /// buried skirt, head beams and the eave course — so a remainder is closed identically for all of them and
    /// they cannot drift out of agreement with each other.
    /// </summary>
    public static class SyntyTileRun
    {
        /// <summary>
        /// Fill `lenHalves` half-modules around fixed openings.
        ///
        /// Openings are placed verbatim; the gaps between them are filled greedily with full 2.5 m pieces and a
        /// single 1.25 m piece closing any odd remainder. Greedy-largest-first is correct here rather than merely
        /// convenient: a 1.25 m panel carries the same trim as a 2.5 m one over half the width, so scattering
        /// them reads as a busier wall, and pushing the one odd piece to the end of each gap keeps that busyness
        /// against an opening or a corner where it looks intentional.
        /// </summary>
        public static List<Seg> Tile(int lenHalves, List<Seg> openings)
        {
            var result = new List<Seg>();
            if (lenHalves <= 0) return result;

            var ops = new List<Seg>();
            if (openings != null)
                foreach (var o in openings)
                    if (o.Start >= 0 && o.Len > 0 && o.Start + o.Len <= lenHalves) ops.Add(o);
            ops.Sort((a, b) => a.Start.CompareTo(b.Start));

            int cursor = 0;
            foreach (var op in ops)
            {
                if (op.Start < cursor) continue;          // overlapping hint — first one wins
                FillPlain(result, cursor, op.Start - cursor);
                result.Add(op);
                cursor = op.Start + op.Len;
            }
            FillPlain(result, cursor, lenHalves - cursor);

            Assert(result, lenHalves);
            return result;
        }

        /// <summary>Full modules first, one half closing the remainder. Never drops the tail.</summary>
        private static void FillPlain(List<Seg> into, int start, int span)
        {
            int at = start;
            while (span >= 2) { into.Add(new Seg(at, 2, SegKind.Plain)); at += 2; span -= 2; }
            if (span == 1) { into.Add(new Seg(at, 1, SegKind.Plain)); at += 1; span -= 1; }
        }

        /// <summary>
        /// The guard the old generator learned to want. Segments must be contiguous from 0 and sum to the run
        /// length exactly — no gap, no overlap, no dropped tail.
        /// </summary>
        private static void Assert(List<Seg> segs, int lenHalves)
        {
            int at = 0;
            foreach (var s in segs)
            {
                if (s.Start != at)
                {
                    Debug.LogError($"[SyntyTileRun] run of {lenHalves} half-modules is discontinuous at {at} " +
                                   $"(next segment starts {s.Start}) — this is the dropped-remainder bug.");
                    return;
                }
                at += s.Len;
            }
            if (at != lenHalves)
                Debug.LogError($"[SyntyTileRun] run of {lenHalves} half-modules only closed {at} — " +
                               $"{lenHalves - at} half-module(s) of wall would be missing.");
        }

        /// <summary>
        /// The kit piece for a segment. Only the two widths exist, so anything else is a bug upstream and is
        /// reported rather than silently rounded.
        ///
        /// Every role except Plain falls back to the 1.25 m plain panel at half width, because the kit ships no
        /// half-width door, display bay or counter window and inventing one by scaling a full panel would put a
        /// 1.25 m-wide door in a wall. A caller that asks for a half-width shopfront gets plain wall and the
        /// frontage stays buildable; the SHOPFRONT COMPOSITION never asks, because it only ever assigns roles to
        /// whole modules.
        /// </summary>
        public static string PieceFor(Seg s, RunPieces p)
        {
            if (s.Len != 2) return p.Half;
            switch (s.Kind)
            {
                case SegKind.Door: return p.Door;
                case SegKind.Window: return p.Window;
                case SegKind.Display: return p.Display;
                case SegKind.DisplayQuiet: return p.DisplayQuiet;
                case SegKind.Counter: return p.Counter;
                default: return p.Plain;
            }
        }

        // ============================================================== the counter run

        /// <summary>
        /// One section of a counter: the piece, and how far its PIVOT sits along the run, in metres.
        ///
        /// The pivot, not the near end, and the difference is a whole counter's length. Measured, a bar section
        /// spans local z ∈ [-2.607, +0.128] — the body hangs BACKWARDS off its pivot, the way a wall panel's
        /// does off its own. The first version of this returned the near end and every section landed one
        /// length behind the run, which put the counter through the wall and out into the street; the verifier
        /// reported the leading section at cell (-1, 0).
        /// </summary>
        public struct CounterSeg
        {
            public string Piece;
            public float At;
            public CounterSeg(string piece, float at) { Piece = piece; At = at; }
        }

        /// <summary>Measured off the prefabs: the counter joinery's lengths along its own run axis.</summary>
        public const float BarLong = 2.735f;    // SM_Prop_Bar_02,      local z [-2.607, +0.128]
        public const float BarShort = 1.646f;   // SM_Prop_Bar_01,      local z [-1.583, +0.063]
        public const float BarGate = 1.106f;    // SM_Prop_Bar_Gate_01, local z [-0.937, +0.169]

        /// <summary>
        /// Divide a counter run into sections, closing the remainder the same way <see cref="Tile"/> does.
        ///
        /// ── Why this is a second tiler and not the first one ────────────────────────────────────────────────
        ///
        /// Every wall run in this generator goes through <see cref="Tile"/>, which works in integer HALF-MODULES
        /// precisely so there is no float comparison to get wrong. A counter cannot: measured, the pack's
        /// joinery is 2.735 m, 1.646 m and 1.106 m long — furniture, authored to look right rather than to sit
        /// on the 1.25 m building grid, and not a rational multiple of it. Forcing it onto the module would mean
        /// either scaling the pieces (which stretches modelled panelling) or leaving 0.235 m holes at every
        /// joint.
        ///
        /// So the discipline is carried over rather than the arithmetic: greedy longest-first, an explicit
        /// remainder, and an assert that what came back covers what was asked for. The remainder here is not
        /// closed with a piece — it is left as OPEN FLOOR at the far end, which is the correct answer for a
        /// counter in a way it never is for a wall: a counter that reaches both walls with no gap traps the
        /// shopkeeper behind it. That is what <paramref name="gate"/> is for, and why a run that ends up with
        /// neither a gate nor a gap is reported.
        /// </summary>
        /// <param name="lengthM">Clear run available, in metres.</param>
        /// <param name="gate">Reserve the lift-up counter flap at the far end.</param>
        public static List<CounterSeg> TileCounter(float lengthM, bool gate)
        {
            var result = new List<CounterSeg>();
            if (lengthM <= 0f) return result;

            float budget = lengthM - (gate ? BarGate : 0f);
            float at = 0f;
            while (budget - at >= BarLong - 0.001f) { at += BarLong; result.Add(new CounterSeg("SM_Prop_Bar_02", at)); }
            if (budget - at >= BarShort - 0.001f) { at += BarShort; result.Add(new CounterSeg("SM_Prop_Bar_01", at)); }
            if (gate) { at += BarGate; result.Add(new CounterSeg("SM_Prop_Bar_Gate_01", at)); }

            float slack = lengthM - at;
            if (slack < -0.001f)
                Debug.LogError($"[SyntyTileRun] counter of {lengthM:F2} m over-ran by {-slack:F2} m — " +
                               "sections would interpenetrate.");
            else if (!gate && slack < BarGate)
                Debug.LogWarning($"[SyntyTileRun] counter of {lengthM:F2} m leaves only {slack:F2} m of gap and " +
                                 "has no lift gate — there is no way to get behind it.");
            return result;
        }
    }
}
