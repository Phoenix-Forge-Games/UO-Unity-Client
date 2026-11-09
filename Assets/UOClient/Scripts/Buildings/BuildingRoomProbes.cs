using UnityEngine;
using UnityEngine.Rendering;

namespace UOClient.Buildings
{
    /// <summary>
    /// One reflection probe per room, sized to the room.
    ///
    /// SIZED TO THE ROOM, NOT TO ITS CONTENTS. The box comes from the room's own rectangle and storey
    /// height, which both generators already know exactly, because a bounds-of-contents box is wrong in both
    /// directions: an empty room would get a probe the size of nothing, and a room with a chimney or a
    /// staircase would get one that reaches through the ceiling into the storey above.
    ///
    /// THE SETTINGS, AND WHAT THEY COST (settlement scale is the constraint — see `Settlement_Generator.md`
    /// §11, where the risk is thousands of furnished interiors, and draw calls are what collapses first):
    ///
    ///   Realtime + ViaScripting  A ViaScripting probe renders ONLY when something calls RenderProbe(). Its
    ///                            standing cost is therefore zero GPU per frame — an unentered building
    ///                            costs memory and nothing else. `EveryFrame` on a city of these would be
    ///                            ruinous (6 faces per probe per frame); `OnAwake` would spike every time an
    ///                            interior streamed in. So the refresh is handed to the interior-streaming
    ///                            driver, which already knows when a player is actually inside.
    ///   NOT baked                Baked probe data lives in the scene's lighting data, and these interiors
    ///                            are addressable payloads loaded on demand (§11 "Resolved: addressables").
    ///                            A streamed-in interior cannot contribute to a bake it was not present for,
    ///                            so baked probes are not actually available here.
    ///   resolution 64            ~196 KB per probe (6 faces, RGBAHalf) against ~786 KB at 128 — a 4x saving
    ///                            for a reflection that, in a small plaster or stone room, is mostly ambient
    ///                            colour. Only interiors near the player are resident, but the multiplier is
    ///                            the whole building's room count.
    ///   boxProjection ON         This is the payoff for sizing to the room. Without it the reflection reads
    ///                            as infinitely distant and the careful box is wasted.
    ///
    /// The probes go in a `Functional` group under `Interior`, so they load and unload with the interior
    /// payload they exist to light — the same placement `BuildingSplit` gives the Polyperfect buildings, and
    /// for the same reason: a probe has nothing to reflect without the interior, and a city must not carry
    /// thousands of them on its exteriors.
    /// </summary>
    public static class BuildingRoomProbes
    {
        public const string GroupName = "Functional";

        /// <summary>Cubemap face size. See the cost note above.</summary>
        public const int Resolution = 64;

        /// <summary>
        /// Add one probe covering the world-space box <paramref name="centre"/> / <paramref name="size"/>.
        /// Both generators call this per room with the room's own rectangle and storey height.
        /// </summary>
        public static ReflectionProbe Add(Transform functional, string roomName, Vector3 centre, Vector3 size)
        {
            var go = new GameObject($"{roomName} Reflection Probe");
            go.transform.SetParent(functional, false);
            go.transform.localPosition = centre;

            var p = go.AddComponent<ReflectionProbe>();
            p.size = size;
            p.center = Vector3.zero;                       // the GameObject is already at the room centre
            p.mode = ReflectionProbeMode.Realtime;
            p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            p.resolution = Resolution;
            p.hdr = true;
            p.importance = 1;
            p.boxProjection = true;
            p.intensity = 1f;
            p.nearClipPlane = 0.1f;

            // Far clip is the room, not the world. A 1000 m far plane on an indoor probe renders the whole
            // settlement into a cubemap that a 4 m room can never show.
            p.farClipPlane = Mathf.Max(size.x, size.y, size.z) * 4f;
            p.cullingMask = ~0;
            return p;
        }

        /// <summary>Find or make the group the probes live in.</summary>
        public static Transform Group(Transform interior)
        {
            var existing = interior.Find(GroupName);
            if (existing != null) return existing;
            var go = new GameObject(GroupName);
            go.transform.SetParent(interior, false);
            return go.transform;
        }
    }
}
