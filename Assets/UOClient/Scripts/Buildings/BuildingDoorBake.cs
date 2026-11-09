using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Buildings
{
    /// <summary>
    /// Measures a door's open pose ONCE, at generation time, and writes it into the component.
    ///
    /// READ <see cref="WindowShutters"/> BEFORE CHANGING ANYTHING HERE. Its swing-direction logic measures
    /// outward reach geometrically instead of deriving a sign, and it is written that way because four
    /// successive attempts to reason the rotation from first principles got the sign wrong. The same trap is
    /// here and worse: a door leaf's hinge edge, its parent panel's yaw, and whether the wall run is mirrored
    /// are three independent sign sources, and the placed instance may carry all three. So nothing below
    /// infers a direction. It rotates the leaf to each candidate, LOOKS at where the mesh ended up, and keeps
    /// the better one.
    ///
    /// Two different failures are measured separately, because they are different mistakes:
    ///
    ///   PROUDNESS  — did it open at all? A leaf swung 180 degrees lies back against its own wall and its
    ///                centroid never leaves the wall plane. This is the "a door that swings flat has not
    ///                opened, it has vanished" check, and it is a property of the pose alone.
    ///   OBSTRUCTION— did it open into something? Measured against the rest of the building, by sampling the
    ///                leaf's own volume at the candidate pose. This is what picks the SIDE.
    /// </summary>
    public static class BuildingDoorBake
    {
        /// <summary>Name fragments that identify a swing leaf inside a door prop. Measured off the packs.</summary>
        private static readonly string[] SwingNames = { "_Door", "Door_Bars" };

        /// <summary>The castle gate's vertical leaf. Checked first — it must never be treated as a swing.</summary>
        private const string LiftName = "Portcullis";

        /// <summary>Points sampled through the leaf's volume when testing a candidate pose for obstruction.</summary>
        private const int SamplesPerAxis = 3;

        /// <summary>The leaf volume is shrunk before sampling so a leaf merely TOUCHING its frame is not a clash.</summary>
        private const float SampleShrink = 0.8f;

        /// <summary>What the bake found, so the generator can log it and the verifier can assert on it.</summary>
        public struct Report
        {
            public int Leaves;
            public int Swing;
            public int Lift;
            public float MinProudFraction;
            public string Failure;      // null when fine
            public bool Ok => Failure == null && Leaves > 0;
        }

        /// <summary>
        /// Bake <paramref name="door"/>. <paramref name="explicitLeaves"/> is for kits whose leaf is a
        /// SIBLING of the panel rather than a child (the castle kit places them that way deliberately, so its
        /// entrance check can measure the panel's clear opening without a leaf in the way). Pass null to
        /// discover leaves among the door's own descendants instead.
        /// </summary>
        public static Report Bake(BuildingDoor door, Transform buildingRoot, IEnumerable<Transform> explicitLeaves = null)
        {
            var report = new Report { MinProudFraction = float.MaxValue };
            if (door == null) { report.Failure = "no door component"; return report; }

            var leaves = new List<Transform>();
            if (explicitLeaves != null) leaves.AddRange(explicitLeaves);
            else Discover(door.transform, leaves);
            leaves.RemoveAll(t => t == null || t.GetComponentInChildren<Renderer>(true) == null);

            if (leaves.Count == 0)
            {
                report.Failure = "no leaf — this door prop is a solid panel and cannot open";
                return report;
            }

            var obstacles = CollectObstacles(buildingRoot, door.transform, leaves);
            var poses = new List<DoorLeafPose>();

            foreach (var leaf in leaves)
            {
                var pose = IsLift(leaf) ? BakeLift(leaf) : BakeSwing(door, leaf, obstacles);
                if (pose == null) continue;
                poses.Add(pose);
                if (pose.Motion == DoorMotion.Swing) report.Swing++; else report.Lift++;

                float frac = pose.LeafWidth > 1e-4f ? pose.Proudness / pose.LeafWidth : 0f;
                if (frac < report.MinProudFraction) report.MinProudFraction = frac;
            }

            door.SetLeaves(poses);
            report.Leaves = poses.Count;

            if (poses.Count == 0) report.Failure = "leaves found but none could be posed";
            else if (report.MinProudFraction < BuildingDoor.MinProudFraction)
                report.Failure = $"open pose is flat against the wall (proud {report.MinProudFraction:F2} of leaf width, " +
                                 $"need {BuildingDoor.MinProudFraction:F2})";

            return report;
        }

        private static bool IsLift(Transform t) => t.name.IndexOf(LiftName, System.StringComparison.Ordinal) >= 0;

        private static void Discover(Transform root, List<Transform> into)
        {
            foreach (Transform child in root)
            {
                bool lift = IsLift(child);
                bool swing = false;
                if (!lift)
                    foreach (var n in SwingNames)
                        if (child.name.IndexOf(n, System.StringComparison.Ordinal) >= 0) { swing = true; break; }

                if (lift || swing) into.Add(child);
            }
        }

        /// <summary>
        /// Everything in the building that a leaf could swing INTO, minus the door prop it belongs to and
        /// every leaf of that door. The prop is excluded deliberately: a hinged leaf always touches its own
        /// frame near the hinge, so counting it would reject both candidates equally and tell us nothing.
        /// Whether the leaf lies back against that frame is what PROUDNESS measures instead.
        /// </summary>
        private static List<Bounds> CollectObstacles(Transform buildingRoot, Transform doorProp, List<Transform> leaves)
        {
            var list = new List<Bounds>();
            if (buildingRoot == null) return list;

            foreach (var r in buildingRoot.GetComponentsInChildren<Renderer>(true))
            {
                var t = r.transform;
                if (IsUnder(t, doorProp)) continue;

                bool isLeaf = false;
                foreach (var l in leaves) if (IsUnder(t, l)) { isLeaf = true; break; }
                if (isLeaf) continue;

                list.Add(r.bounds);
            }
            return list;
        }

        private static bool IsUnder(Transform t, Transform ancestor)
        {
            for (var c = t; c != null; c = c.parent) if (c == ancestor) return true;
            return false;
        }

        private static DoorLeafPose BakeSwing(BuildingDoor door, Transform leaf, List<Bounds> obstacles)
        {
            var rend = leaf.GetComponentInChildren<Renderer>(true);
            if (rend == null) return null;

            // The wall plane's normal. The leaf is authored flush in its panel, so the panel's own forward is
            // the direction "off the wall" — and taking it from the PANEL rather than the leaf is what makes
            // this survive a mirrored or rotated placement.
            Vector3 normal = door.transform.forward;

            float closedY = leaf.localEulerAngles.y;
            Vector3 closedCentre = rend.bounds.center;
            float width = HorizontalExtent(rend.bounds);

            float bestScore = float.NegativeInfinity;
            float bestY = closedY;
            float bestProud = 0f;

            foreach (float cand in new[] { closedY + door.SwingDegrees, closedY - door.SwingDegrees })
            {
                SetYaw(leaf, cand);

                float proud = Mathf.Abs(Vector3.Dot(rend.bounds.center - closedCentre, normal));
                int hits = Obstructions(rend, obstacles);

                // Fewest clashes wins outright; proudness only breaks the tie. Weighting it this way is what
                // stops a leaf choosing the "more open-looking" side straight into the neighbouring wall.
                float score = -hits * 1000f + proud;
                if (score > bestScore) { bestScore = score; bestY = cand; bestProud = proud; }
            }

            SetYaw(leaf, closedY);

            return new DoorLeafPose
            {
                Leaf = leaf,
                Motion = DoorMotion.Swing,
                ClosedValue = closedY,
                OpenValue = bestY,
                Proudness = bestProud,
                LeafWidth = width
            };
        }

        private static DoorLeafPose BakeLift(Transform leaf)
        {
            var rend = leaf.GetComponentInChildren<Renderer>(true);
            if (rend == null) return null;

            // A portcullis clears its opening by rising its own height — measured off the mesh, not guessed.
            float height = rend.bounds.size.y;
            float closed = leaf.localPosition.y;
            float travel = height * BuildingDoor.LiftFraction;

            return new DoorLeafPose
            {
                Leaf = leaf,
                Motion = DoorMotion.Lift,
                ClosedValue = closed,
                OpenValue = closed + travel,
                Proudness = travel,     // for a lift, "how far it got out of the way"
                LeafWidth = height      // so Proudness/LeafWidth reads as a fraction, same as a swing
            };
        }

        private static void SetYaw(Transform t, float y)
        {
            var a = t.localEulerAngles;
            a.y = y;
            t.localEulerAngles = a;
        }

        private static float HorizontalExtent(Bounds b) => Mathf.Max(b.size.x, b.size.z);

        /// <summary>
        /// How many sample points of the leaf's own volume land inside other building geometry at the
        /// current pose. Sampling the volume rather than intersecting AABBs matters: a leaf standing at 90
        /// degrees has a fat axis-aligned box that overlaps half the room while the leaf itself touches
        /// nothing.
        /// </summary>
        private static int Obstructions(Renderer rend, List<Bounds> obstacles)
        {
            if (obstacles.Count == 0) return 0;

            var local = rend.localBounds;
            var m = rend.transform.localToWorldMatrix;
            int hits = 0;

            for (int ix = 0; ix < SamplesPerAxis; ix++)
            for (int iy = 0; iy < SamplesPerAxis; iy++)
            for (int iz = 0; iz < SamplesPerAxis; iz++)
            {
                var f = new Vector3(Frac(ix), Frac(iy), Frac(iz));
                var lp = local.center + Vector3.Scale(f, local.extents) * SampleShrink;
                var wp = m.MultiplyPoint3x4(lp);

                foreach (var o in obstacles)
                    if (o.Contains(wp)) { hits++; break; }
            }
            return hits;
        }

        /// <summary>Maps a sample index to -1 .. +1 across the axis.</summary>
        private static float Frac(int i) => SamplesPerAxis == 1 ? 0f : (i / (float)(SamplesPerAxis - 1)) * 2f - 1f;
    }
}
