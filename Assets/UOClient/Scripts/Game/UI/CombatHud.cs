using System.Collections.Generic;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient;
using UOGame.Combat;
using UOGame.Net;
using UOGame.Server;

namespace UOGame.UI
{
    /// <summary>
    /// Combat on the client, MMO style:
    /// <list type="bullet">
    /// <item>left-click a creature or player to target it (click the ground to clear), Tab cycles the nearest hostiles
    /// in front of you, R attacks the target (R again stops);</item>
    /// <item>your frame (name, health, a red edge while in combat) and your target's frame;</item>
    /// <item>floating numbers over whoever is hit (misses say so).</item>
    /// </list>
    /// The server decides everything; this only asks and shows.
    /// </summary>
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class CombatHud : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;

        private const float TabRange = 30f;
        private const float FloatSeconds = 1.2f;

        private VisualElement _root;
        private Label _deadNote;
        private (VisualElement Frame, Label Name, VisualElement Fill, Label Value) _self, _target;

        /// <summary>Client: your own unit frame (the item HUD puts your mana and stamina bars in it, under your hits).</summary>
        public static VisualElement SelfFrame { get; private set; }
        private Combatant _targeted;
        private readonly List<(Label Label, Vector3 At, float Born)> _floating = new List<(Label, Vector3, float)>();
        private readonly List<Combatant> _tabOrder = new List<Combatant>();
        private static readonly RaycastHit[] Hits = new RaycastHit[32];

        private void OnEnable()
        {
            GetComponent<PanelRenderer>().RegisterUIReloadCallback(OnUIReload);
            CombatService.Swing += OnSwing;
        }

        private void OnDisable()
        {
            GetComponent<PanelRenderer>().UnregisterUIReloadCallback(OnUIReload);
            CombatService.Swing -= OnSwing;
        }

        private void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
        {
            GameHud.RegisterUI(root); // the mouse wheel and clicks over it are the UI's, not the camera's
            _root = root;
            root.pickingMode = PickingMode.Ignore;
            var frames = new VisualElement { pickingMode = PickingMode.Ignore };
            frames.AddToClassList("unit-frames");
            root.Add(frames);
            _self = Frame(frames);
            SelfFrame = _self.Frame;
            _target = Frame(frames);
            _deadNote = new Label("You are dead. Find a healer to be brought back to life.") { pickingMode = PickingMode.Ignore };
            _deadNote.AddToClassList("dead-note");
            _deadNote.style.display = DisplayStyle.None;
            root.Add(_deadNote);
            _floating.Clear();
        }

        private static (VisualElement, Label, VisualElement, Label) Frame(VisualElement parent)
        {
            var frame = new VisualElement { pickingMode = PickingMode.Ignore };
            frame.AddToClassList("unit-frame");
            var name = new Label { pickingMode = PickingMode.Ignore };
            name.AddToClassList("unit-name");
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("unit-bar");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("unit-fill");
            var value = new Label { pickingMode = PickingMode.Ignore };
            value.AddToClassList("unit-value");
            bar.Add(fill);
            bar.Add(value);
            frame.Add(name);
            frame.Add(bar);
            frame.style.display = DisplayStyle.None;
            parent.Add(frame);
            return (frame, name, fill, value);
        }

        private void Update()
        {
            PlayerMotor me = PlayerMotor.Local;
            if (_root == null || me == null)
            {
                return;
            }

            if (!GameInput.Typing)
            {
                Vector2 pointer = GameInput.Controls.Gameplay.Point.ReadValue<Vector2>();
                if (GameInput.Controls.Gameplay.Select.WasPressedThisFrame() && !GameInput.IsPointerOverUI(pointer))
                {
                    Pick(pointer, me);
                }

                if (GameInput.Controls.Gameplay.CycleTarget.WasPressedThisFrame())
                {
                    CycleHostile(me);
                }

                if (GameInput.Controls.Gameplay.Attack.WasPressedThisFrame() && _targeted != null)
                {
                    _network.ClientManager.Broadcast(new TargetRequest { ObjectId = _targeted.ObjectId, Attack = true });
                }
            }

            if (_targeted != null && (!_targeted.IsSpawned || !Combatant.Seen.Contains(_targeted)))
            {
                _targeted = null;
                Targeted = null;
            }
        }

        private void Pick(Vector2 pointer, PlayerMotor me)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            int count = Physics.RaycastNonAlloc(camera.ScreenPointToRay(pointer), Hits, 200f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(Hits, 0, count, Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance)));
            Combatant picked = null;
            for (int i = 0; i < count; i++)
            {
                Combatant c = Hits[i].collider.GetComponentInParent<Combatant>();
                if (c != null && c.transform != me.transform)
                {
                    picked = c;
                    break;
                }

                if (c == null)
                {
                    break; // a wall or the ground first
                }
            }

            SetTarget(picked);
        }

        private void CycleHostile(PlayerMotor me)
        {
            Camera camera = Camera.main;
            _tabOrder.Clear();
            foreach (Combatant c in Combatant.Seen)
            {
                if (c == null || c.Dead.Value || c.transform == me.transform)
                {
                    continue;
                }

                NpcMobile npc = c.GetComponent<NpcMobile>();
                Vector3 to = c.transform.position - me.transform.position;
                bool ahead = camera == null || Vector3.Dot(camera.transform.forward, to.normalized) > 0.2f;
                if (npc != null && npc.Hostile && ahead && to.magnitude <= TabRange)
                {
                    _tabOrder.Add(c);
                }
            }

            if (_tabOrder.Count == 0)
            {
                return;
            }

            Vector3 from = me.transform.position;
            _tabOrder.Sort((a, b) => (a.transform.position - from).sqrMagnitude.CompareTo((b.transform.position - from).sqrMagnitude));
            int next = (_tabOrder.IndexOf(_targeted) + 1) % _tabOrder.Count;
            SetTarget(_tabOrder[next]);
        }

        /// <summary>Client: whoever this player has targeted (housing adds a targeted player as a friend).</summary>
        public static Combatant Targeted { get; private set; }

        private void SetTarget(Combatant target)
        {
            _targeted = target;
            Targeted = target;
            _network.ClientManager.Broadcast(new TargetRequest { ObjectId = target != null ? target.ObjectId : -1, Attack = false });
        }

        private void OnSwing(SwingMessage message)
        {
            if (_root == null)
            {
                return;
            }

            Combatant target = Combatant.Seen.Find(c => c != null && c.ObjectId == message.TargetId);
            if (target == null)
            {
                return;
            }

            bool mine = PlayerMotor.Local != null && target.transform == PlayerMotor.Local.transform;
            var label = new Label(message.Amount > 0 ? message.Amount.ToString() : "miss") { pickingMode = PickingMode.Ignore };
            label.AddToClassList("float-number");
            if (mine)
            {
                label.AddToClassList("float-taken");
            }

            _root.Add(label);
            _floating.Add((label, target.transform.position + Vector3.up * 2.4f, Time.time));
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            PlayerMotor me = PlayerMotor.Local;
            if (_root == null || camera == null)
            {
                return;
            }

            Show(_self, me != null ? me.GetComponent<Combatant>() : null, me != null ? me.GetComponent<PlayerIdentity>()?.Name.Value : null);
            Show(_target, _targeted, (_targeted as Component)?.GetComponent<INamed>()?.DisplayName);
            Combatant self = me != null ? me.GetComponent<Combatant>() : null;
            _deadNote.style.display = self != null && self.Dead.Value ? DisplayStyle.Flex : DisplayStyle.None;

            for (int i = _floating.Count - 1; i >= 0; i--)
            {
                (Label label, Vector3 at, float born) = _floating[i];
                float age = Time.time - born;
                if (age > FloatSeconds)
                {
                    label.RemoveFromHierarchy();
                    _floating.RemoveAt(i);
                    continue;
                }

                Vector3 screen = camera.WorldToScreenPoint(at + Vector3.up * age * 1.2f);
                label.style.display = screen.z > 0f ? DisplayStyle.Flex : DisplayStyle.None;
                Vector2 panel = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screen.x, Screen.height - screen.y));
                label.style.left = panel.x;
                label.style.top = panel.y;
                label.style.opacity = 1f - Mathf.Clamp01((age - FloatSeconds * 0.6f) / (FloatSeconds * 0.4f));
            }
        }

        private static void Show((VisualElement Frame, Label Name, VisualElement Fill, Label Value) frame, Combatant c, string name)
        {
            if (frame.Frame == null)
            {
                return;
            }

            frame.Frame.style.display = c != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (c == null)
            {
                return;
            }

            frame.Name.text = c.Dead.Value ? $"{name} (dead)" : name;
            float share = c.HitsMax.Value > 0 ? Mathf.Clamp01(c.Hits.Value / (float)c.HitsMax.Value) : 0f;
            frame.Fill.style.width = new Length(share * 100f, LengthUnit.Percent);
            frame.Value.text = $"{c.Hits.Value} / {c.HitsMax.Value}";
            frame.Frame.EnableInClassList("in-combat", c.InCombat.Value);
            NpcMobile npc = c.GetComponent<NpcMobile>();
            frame.Frame.EnableInClassList("hostile", npc != null && npc.Hostile);
        }
    }
}
