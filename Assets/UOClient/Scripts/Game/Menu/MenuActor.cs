using System.Collections.Generic;
using UnityEngine;
using UOGame.Content;
using UOGame.Net;

namespace UOGame.Menu
{
    /// <summary>
    /// A character in the menu's town, on this machine only (no server): built from a <see cref="Look"/>, it walks
    /// to points on the ground with the walk animation and idles between. The town's walkers and your stand-in at the
    /// door are these.
    /// </summary>
    public sealed class MenuActor : MonoBehaviour
    {
        private static readonly int SpeedParam = Animator.StringToHash("Speed");

        private GameObject _model;
        private Animator _animator;
        private Vector3? _goal;
        private float _speed = 1.4f;
        private Quaternion? _face;

        /// <summary>Reached the point it was walking to (and stopped).</summary>
        public bool Arrived => _goal == null;

        public static MenuActor Make(string name, Look look, Vector3 at, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = Ground(at);
            var actor = go.AddComponent<MenuActor>();
            actor.Dress(look);
            return actor;
        }

        /// <summary>A random townsperson: a player-like look in everyday clothes.</summary>
        public static Look Townsperson(int seed)
        {
            Look look = Outfits.RollPlayer(seed);
            look.Gear = Clothes(look.Female, seed).ToArray();
            return look;
        }

        /// <summary>Everyday clothes (Creation.json Clothes), one per worn slot.</summary>
        public static List<Gear> Clothes(bool female, int seed)
        {
            var bySlot = new Dictionary<Items.WearSlot, Gear>();
            foreach (Gear g in Outfits.RollItems(CreationRules.Clothes, female, seed))
            {
                Items.ItemDef def = Server.ItemService.Catalog?.Get(g.Item);
                if (def != null && def.Wearable && !bySlot.ContainsKey(def.Slot))
                {
                    bySlot[def.Slot] = g;
                }
            }

            return new List<Gear>(bySlot.Values);
        }

        /// <summary>Wear this look (rebuilt in place, keeping where it stands and faces).</summary>
        public void Dress(Look look)
        {
            if (_model != null)
            {
                Destroy(_model);
            }

            _model = null;
            _animator = null;
            if (GameLook.Dresser != null)
            {
                Server.ItemService.EnsureCatalog();
                _model = GameLook.Dresser.Build(Looks.ToMobile(look, Server.ItemService.Catalog), transform);
            }

            if (_model == null)
            {
                // No UO data (or wardrobe) on this machine: a plain stand-in shape.
                _model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(_model.GetComponent<Collider>());
                _model.transform.SetParent(transform, false);
                _model.transform.localPosition = Vector3.up;
            }

            foreach (Collider c in _model.GetComponentsInChildren<Collider>(true))
            {
                Destroy(c);
            }

            _animator = _model.GetComponentInChildren<Animator>();
            if (_animator != null)
            {
                _animator.applyRootMotion = false;
            }
        }

        /// <summary>Their usual walking pace (metres a second).</summary>
        public float Pace { get; set; } = 1.4f;

        /// <summary>Stop at walls and buildings (townsfolk). Off for your character, who walks out through a doorway.</summary>
        public bool KeepClear { get; set; } = true;

        /// <summary>The last walk stopped short: something solid was in the way.</summary>
        public bool WasBlocked { get; private set; }

        /// <summary>Walk to a point (on the ground there), at this pace (metres a second).</summary>
        public void WalkTo(Vector3 point, float speed = 1.4f)
        {
            _goal = point;
            _speed = speed;
            _face = null;
            WasBlocked = false;
        }

        /// <summary>
        /// Is there something solid just ahead at knee or chest height (a wall, a house, a fence), or a drop or rise
        /// too big to step? Walkers never go through or over buildings.
        /// </summary>
        private bool Blocked(Vector3 dir, float step)
        {
            Vector3 at = transform.position;
            float reach = step + 0.35f;
            foreach (float h in new[] { 0.6f, 1.4f })
            {
                foreach (RaycastHit hit in Physics.RaycastAll(at + Vector3.up * h, dir, reach, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.GetComponentInParent<MenuActor>() == null && hit.collider.GetComponentInParent<Combat.Combatant>() == null)
                    {
                        return true;
                    }
                }
            }

            Vector3 next = Ground(at + dir * reach);
            return Mathf.Abs(next.y - at.y) > 0.45f;
        }

        /// <summary>Turn on the spot by this many degrees (dragging them round on the character screen), and stay so.</summary>
        public void Spin(float degrees)
        {
            if (_goal != null)
            {
                return; // still walking out
            }

            transform.Rotate(0f, degrees, 0f, Space.World);
            _face = transform.rotation;
        }

        /// <summary>Turn (gently) to face a point while standing.</summary>
        public void Face(Vector3 point)
        {
            Vector3 d = point - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.001f)
            {
                _face = Quaternion.LookRotation(d);
            }
        }

        private void Update()
        {
            float moved = 0f;
            if (_goal is Vector3 goal)
            {
                Vector3 to = goal - transform.position;
                to.y = 0f;
                float step = _speed * Time.deltaTime;
                if (to.magnitude <= step)
                {
                    transform.position = Ground(goal);
                    _goal = null;
                }
                else if (KeepClear && Blocked(to.normalized, step))
                {
                    // A wall, a fence, a house in the way: stop here (whoever sent them picks another way).
                    _goal = null;
                    WasBlocked = true;
                }
                else
                {
                    Vector3 dir = to.normalized;
                    transform.position = Ground(transform.position + dir * step);
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 8f);
                    moved = _speed;
                }
            }
            else if (_face is Quaternion face)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, face, Time.deltaTime * 5f);
            }

            if (_animator != null && _animator.isActiveAndEnabled)
            {
                // The humanoid controller takes Speed in metres a second (each clip at the ground speed it shows).
                _animator.SetFloat(SpeedParam, moved, 0.07f, Time.deltaTime);
            }
        }

        /// <summary>
        /// The ground (or floor) under a point: the highest solid surface no more than a step above it (so a walker
        /// keeps to the street and never climbs a wall or a window sill).
        /// </summary>
        public static Vector3 Ground(Vector3 at)
        {
            var from = new Vector3(at.x, at.y + 0.5f, at.z);
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (RaycastHit h in hits)
            {
                if (h.point.y > best && h.collider.GetComponentInParent<MenuActor>() == null &&
                    h.collider.GetComponentInParent<Combat.Combatant>() == null)
                {
                    best = h.point.y;
                }
            }

            return float.IsNegativeInfinity(best) ? at : new Vector3(at.x, best, at.z);
        }
    }
}
