using System;
using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Buildings
{
    /// <summary>How a leaf gets out of the way.</summary>
    public enum DoorMotion
    {
        /// <summary>Hinged about local Y at its own pivot. Every door leaf in both kits.</summary>
        Swing = 0,

        /// <summary>Rises straight up. The castle portcullis, which does not swing at all.</summary>
        Lift = 1
    }

    /// <summary>
    /// The three states a door can be in. `Locked` is a state, not a flag on `Closed`: it is what the
    /// player is told, and it is what the interact path branches on, so it belongs in the same enum.
    /// A locked door is always shut — locking an open door closes it (see <see cref="BuildingDoor.SetLocked"/>).
    /// </summary>
    public enum DoorState { Locked = 0, Closed = 1, Open = 2 }

    /// <summary>
    /// Whether a lock yields. The DEFAULT refuses everything, which is the honest Phase-2 answer: keys,
    /// ownership and the §17 Lockpicking check are not designed yet, so a locked door is simply shut.
    ///
    /// THIS IS THE SEAM. GDD §17.5 opens house theft with "pick the lock → stealth check", and §17.1 puts
    /// Lockpicking on the E–SSS band — so the thing that will eventually run is a *check against a
    /// difficulty*, not a bool flip. Installing that later is replacing <see cref="BuildingDoor.LockPolicy"/>,
    /// not editing every door or every call site.
    /// </summary>
    public interface IDoorLockPolicy
    {
        /// <summary>
        /// Called only when a locked door is interacted with. Return true to let it open (and to clear the
        /// lock). <paramref name="line"/> is what the player is told either way and must never be empty.
        /// </summary>
        bool TryUnlock(BuildingDoor door, GameObject actor, out string line);
    }

    /// <summary>
    /// The lock itself, authored as data. The settlement generator sets these per door — a jail's cells,
    /// a shop's back room, a private house — so nothing about *which* doors lock is hard-coded.
    /// </summary>
    [Serializable]
    public sealed class DoorLock
    {
        [Tooltip("Whether this door is locked right now. Set from data at generation time; the runtime may " +
                 "change it (a shop locking its back room at night).")]
        public bool Locked;


        [Tooltip("What the player is told when it refuses. §3.4 puts interaction in the VN band, so silence " +
                 "is not an option.")]
        public string RefusalLine = "Locked.";
    }

    /// <summary>
    /// One openable door, baked by the generators onto every door they place.
    ///
    /// WHY THE OPEN POSE IS BAKED, NOT DERIVED AT RUNTIME. <see cref="WindowShutters"/> is the precedent and
    /// the warning: four successive attempts to reason a swing direction from first principles got the sign
    /// wrong — closing on the wrong flat multiple, mirrored leaves swinging inward, instance-level mirroring
    /// the leaf's own scale sign could not see. The fix there was to stop deriving and start MEASURING, and
    /// this does the same, once, at generation time when the whole building is present to measure against.
    /// The runtime then only interpolates between two recorded numbers, so it cannot get the sign wrong and
    /// cannot disagree with the frozen LOD.
    ///
    /// WHAT "OPEN" HAS TO MEAN. A leaf that swings to lie flat against its own wall has not opened, it has
    /// vanished. So the bake records <see cref="DoorLeafPose.Proudness"/> — how far the leaf's centroid
    /// actually travels off the wall plane — and a pose that does not clear <see cref="MinProudFraction"/>
    /// of the leaf's own width is rejected rather than shipped.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildingDoor : MonoBehaviour
    {
        // ------------------------------------------------------------------ authored data

        [Tooltip("Seconds for a full swing. Zero snaps.")]
        public float SwingSeconds = 0.6f;

        [Tooltip("How far a swing leaf turns off flat. 90 stands it square to its wall; 180 would lay it " +
                 "back against the wall, which is not open.")]
        public float SwingDegrees = 90f;

        public DoorLock Lock = new DoorLock();

        [SerializeField] private bool _open;
        [SerializeField] private List<DoorLeafPose> _leaves = new List<DoorLeafPose>();

        /// <summary>An open leaf must carry its centroid at least this fraction of its own width off the
        /// wall plane. A 90 degree swing of an edge-hinged leaf reaches ~0.5; a 180 degree "swing" reaches 0.</summary>
        public const float MinProudFraction = 0.25f;

        /// <summary>Fraction of its own height a lift leaf rises. Just under 1 so it stays in its guides.</summary>
        public const float LiftFraction = 0.95f;

        // ------------------------------------------------------------------ the lock seam

        private static IDoorLockPolicy _policy;

        /// <summary>
        /// The installed lock policy. Defaults to <see cref="RefuseAll"/> — locked means locked until
        /// something better exists. Replace it once §17 Lockpicking is designed.
        /// </summary>
        public static IDoorLockPolicy LockPolicy
        {
            get => _policy ?? (_policy = new RefuseAll());
            set => _policy = value;
        }

        private sealed class RefuseAll : IDoorLockPolicy
        {
            public bool TryUnlock(BuildingDoor door, GameObject actor, out string line)
            {
                line = string.IsNullOrEmpty(door.Lock.RefusalLine) ? "Locked." : door.Lock.RefusalLine;
                return false;
            }
        }

        // ------------------------------------------------------------------ state

        /// <summary>Locked / Closed / Open. The door is the authority — nothing else should infer this.</summary>
        public DoorState State => Lock != null && Lock.Locked ? DoorState.Locked
                                : _open ? DoorState.Open : DoorState.Closed;

        /// <summary>How many leaves the bake found. Zero means this door cannot open.</summary>
        public int LeafCount => _leaves.Count;

        public IReadOnlyList<DoorLeafPose> Leaves => _leaves;

        /// <summary>
        /// Can this door be opened by anyone, ever, right now? The DOOR answers this — the interact path
        /// asks rather than checking a flag itself, so a future caller cannot forget to.
        /// </summary>
        public bool CanOpen => _leaves.Count > 0 && (Lock == null || !Lock.Locked);

        private float _t = 1f;
        private bool _target;

        /// <summary>
        /// AUTHOR-TIME ONLY. Replaces the baked leaf table — see <see cref="BuildingDoorBake"/>, which is the
        /// only thing that should call this. The runtime never re-derives these numbers, deliberately: the
        /// measurement needs the whole building present, and the frozen LOD is built from the saved pose, so
        /// a runtime re-derive is how LOD0 and LOD1 come to disagree.
        /// </summary>
        public void SetLeaves(List<DoorLeafPose> poses)
        {
            _leaves = poses ?? new List<DoorLeafPose>();
            SetOpenImmediate(false);
        }

        // UOClient port: Soulbound's IInteractable entry points are removed. In the UO client the server
        // owns doors; the client opens one by using the door item and follows its open/closed state.

        // ------------------------------------------------------------------ runtime

        private static readonly List<BuildingDoor> _all = new List<BuildingDoor>();

        /// <summary>The door's stable map ID (from the baked world), which the server and clients name it by; 0 if none.</summary>
        public ulong WorldId { get; set; }

        private Vector3? _opening;

        /// <summary>
        /// The middle of the doorway: the centre of the leaves as they hang shut (measured once). Picking a door and
        /// checking a player can reach it both measure from here, so the client and the server agree.
        /// </summary>
        public Vector3 OpeningCentre
        {
            get
            {
                if (_opening.HasValue)
                {
                    return _opening.Value;
                }

                var bounds = new Bounds(transform.position + Vector3.up, Vector3.zero);
                bool any = false;
                foreach (DoorLeafPose pose in _leaves)
                {
                    if (pose == null || pose.Leaf == null)
                    {
                        continue;
                    }

                    foreach (Renderer r in pose.Leaf.GetComponentsInChildren<Renderer>())
                    {
                        if (!any) { bounds = r.bounds; any = true; } else { bounds.Encapsulate(r.bounds); }
                    }
                }

                // Measured while open, the leaves have swung out: use the panel instead until it's shut.
                if (State == DoorState.Open && any)
                {
                    return bounds.center;
                }

                _opening = bounds.center;
                return _opening.Value;
            }
        }

        /// <summary>Every door in the world.</summary>
        public static IReadOnlyList<BuildingDoor> All => _all;

        private void OnDestroy() => _all.Remove(this);

        private void Awake()
        {
            _all.Add(this);
            _target = _open;
            _t = 1f;
            Apply(_open ? 1f : 0f);
        }

        /// <summary>Start a swing. A locked door ignores a request to open but may always be closed.</summary>
        public void SetOpen(bool open)
        {
            if (open && Lock != null && Lock.Locked) return;
            if (open == _target) return;
            _target = open;
            _open = open;
            _t = 0f;
            Swung?.Invoke(this, open);
        }

        /// <summary>A door started to open (true) or shut (false): the client plays its sound.</summary>
        public static event System.Action<BuildingDoor, bool> Swung;

        /// <summary>
        /// Opens the door and shuts it again after <paramref name="seconds"/>: the door's own logic for a door no
        /// server owns yet. (On the server, using a door sends the open trigger to every nearby client and the
        /// door's code sends the close trigger a moment later; clients only ever follow those triggers.)
        /// </summary>
        public void OpenFor(float seconds)
        {
            SetOpen(true);
            _closeAt = Time.time + seconds;
        }

        private float _closeAt = float.PositiveInfinity;

        public void Toggle() => SetOpen(!_target);

        /// <summary>Lock or unlock from data or from the world. Locking a door shuts it.</summary>
        public void SetLocked(bool locked)
        {
            if (Lock == null) Lock = new DoorLock();
            Lock.Locked = locked;
            if (locked && _target) SetOpenImmediate(false);
        }

        /// <summary>Set the pose with no animation — used by the generator to stamp an initial state.</summary>
        public void SetOpenImmediate(bool open)
        {
            _open = open;
            _target = open;
            _t = 1f;
            Apply(open ? 1f : 0f);
        }

        private void Update()
        {
            if (_t >= 1f && Time.time >= _closeAt)
            {
                _closeAt = float.PositiveInfinity;
                SetOpen(false);
            }

            if (_t >= 1f) return; // always on: a door is a permanent part of the map, it just waits for triggers
            _t = SwingSeconds <= 0f ? 1f : Mathf.Min(1f, _t + Time.deltaTime / SwingSeconds);
            float e = 1f - (1f - _t) * (1f - _t);   // ease out, so it settles rather than stopping dead
            Apply(_target ? e : 1f - e);
        }

        /// <summary>k = 0 closed, 1 open.</summary>
        private void Apply(float k)
        {
            for (int i = 0; i < _leaves.Count; i++)
            {
                var l = _leaves[i];
                if (l == null || l.Leaf == null) continue;

                if (l.Motion == DoorMotion.Swing)
                {
                    var a = l.Leaf.localEulerAngles;
                    a.y = Mathf.LerpAngle(l.ClosedValue, l.OpenValue, k);
                    l.Leaf.localEulerAngles = a;
                }
                else
                {
                    var p = l.Leaf.localPosition;
                    p.y = Mathf.Lerp(l.ClosedValue, l.OpenValue, k);
                    l.Leaf.localPosition = p;
                }
            }
        }
    }

    /// <summary>One leaf and the two numbers the bake measured for it.</summary>
    [Serializable]
    public sealed class DoorLeafPose
    {
        public Transform Leaf;
        public DoorMotion Motion;

        [Tooltip("Swing: local Y in degrees. Lift: local Y in metres.")]
        public float ClosedValue;

        public float OpenValue;

        [Tooltip("MEASURED at bake: how far the leaf's centroid travels off the wall plane when open, in " +
                 "metres. This is the number that proves the door does not simply lie back against its wall.")]
        public float Proudness;

        [Tooltip("MEASURED at bake: the leaf's own width, so Proudness can be read as a fraction of it.")]
        public float LeafWidth;
    }
}
