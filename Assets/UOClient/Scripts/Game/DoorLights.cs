using System.Collections.Generic;
using UnityEngine;
using UOClient.Buildings;

namespace UOGame
{
    /// <summary>
    /// Lanterns at the doors: every outside door of every building gets a lantern on an arm on the wall each side of it
    /// (one where only one side has wall), from the <see cref="RoomLightKit"/> DoorLantern, seeded into the world by the
    /// bake (<see cref="WorldSeeding"/>). An outside door is one with open sky on exactly one side.
    /// </summary>
    public static class DoorLights
    {
        private const string LanternsName = "Door lanterns";

        /// <summary>Where the arm sits against the top of the door (metres, below it): the lantern hangs beside the door at head height.</summary>
        private const float LanternAboveDoor = -0.3f;

        /// <summary>
        /// Hangs the door's lanterns if it is an outside door (once per door); returns them (empty for an inside door,
        /// or one already hung).
        /// </summary>
        public static List<Transform> HangLanterns(BuildingDoor door, RoomLightKit kit)
        {
            var placed = new List<Transform>();
            if (door == null || kit == null || kit.DoorLantern == null || door.transform.Find(LanternsName) != null ||
                !Outside(door, out Vector3 opening, out Vector3 outward, out float width))
            {
                return placed;
            }

            var root = new GameObject(LanternsName).transform;
            root.SetParent(door.transform, true);
            Vector3 side = Vector3.Cross(Vector3.up, outward);
            float top = DoorTop(door, opening.y + 1.1f);
            const float reach = 0.8f; // how far the fixture's arm and lantern stand out from the wall (its origin)
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 probe = opening + side * s * (width * 0.5f + 0.55f);
                probe.y = top + LanternAboveDoor;

                // A wall facing out, about in the door's plane (not a corner, a porch post or empty air) ...
                if (!Physics.Raycast(probe + outward * 1.5f, -outward, out RaycastHit wall, 2.2f, ~0, QueryTriggerInteraction.Ignore) ||
                    Vector3.Dot(wall.normal, outward) < 0.7f || Mathf.Abs(Vector3.Dot(wall.point - opening, outward)) > 0.9f)
                {
                    continue;
                }

                // ... with room out in front for the arm and its lantern.
                if (Physics.Raycast(wall.point + outward * 0.05f, outward, reach + 0.6f, ~0, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                // Named "...(Clone)": WorldLights lights it (and the kit finds its flames) by that.
                // The door lantern fixture: its origin on the wall, the lantern out along +Z.
                GameObject go = Object.Instantiate(kit.DoorLantern, wall.point, Quaternion.LookRotation(outward), root);
                StripColliders(go);

                placed.Add(go.transform);
            }

            return placed;
        }

        /// <summary>
        /// Whether the door leads outside, and if so its opening's middle, the way out, and how wide it is: across the
        /// doorway (the thin way of its closed leaves), the side with open sky over it, the other side roofed.
        /// </summary>
        public static bool Outside(BuildingDoor door, out Vector3 opening, out Vector3 outward, out float width)
        {
            opening = door.OpeningCentre;
            outward = Vector3.zero;
            width = 0f;
            Vector3 across = Vector3.forward;
            foreach (DoorLeafPose pose in door.Leaves)
            {
                if (pose.Leaf != null && pose.Leaf.GetComponentInChildren<Renderer>() is Renderer leaf)
                {
                    width += Mathf.Max(leaf.bounds.size.x, leaf.bounds.size.z);
                    across = leaf.bounds.size.x < leaf.bounds.size.z ? Vector3.right : Vector3.forward;
                }
            }

            width = width > 0.3f ? width : 1.2f;
            bool skyA = !Physics.Raycast(opening + across * 2.5f + Vector3.up * 1.5f, Vector3.up, 40f, ~0, QueryTriggerInteraction.Ignore);
            bool skyB = !Physics.Raycast(opening - across * 2.5f + Vector3.up * 1.5f, Vector3.up, 40f, ~0, QueryTriggerInteraction.Ignore);
            if (skyA == skyB)
            {
                return false; // roofed both sides (inside the house) or neither (a gate in the open)
            }

            outward = skyA ? across : -across;
            return true;
        }

        /// <summary>The top of the door's leaves (closed), or the fallback.</summary>
        private static float DoorTop(BuildingDoor door, float fallback)
        {
            float top = float.MinValue;
            foreach (DoorLeafPose pose in door.Leaves)
            {
                if (pose.Leaf != null && pose.Leaf.GetComponentInChildren<Renderer>() is Renderer leaf)
                {
                    top = Mathf.Max(top, leaf.bounds.max.y);
                }
            }

            return top > float.MinValue ? top : fallback;
        }

        /// <summary>A placed prop is decoration only: the chunk's own collision is what you bump into.</summary>
        public static void StripColliders(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(c);
                }
                else
                {
                    Object.DestroyImmediate(c);
                }
            }
        }

    }
}
