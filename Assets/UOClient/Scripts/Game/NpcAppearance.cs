using FishNet.Object;
using UnityEngine;
using UOClient;

namespace UOGame
{
    /// <summary>
    /// What a creature or NPC looks like, on clients only: its UO body's model from the creature catalog (humans use
    /// the Synty human), animated from how fast it actually moves.
    /// </summary>
    public sealed class NpcAppearance : NetworkBehaviour
    {
        [SerializeField] private CreatureCatalog _catalog;
        [SerializeField] private GameObject _human;
        [SerializeField] private RuntimeAnimatorController _humanAnimator;
        [SerializeField] private Transform _graphic;
        /// <summary>Worn by a bonded pet that died, until it's brought back.</summary>
        [SerializeField] private Material _ghost;

        private Material[][] _living;
        private Renderer[] _renderers;

        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private Animator _animator;
        private Vector3 _last;
        private float _speed;
        private GameObject _model;

        private bool _hooked;

        public override void OnStartClient()
        {
            base.OnStartClient();
            NpcMobile mobile = GetComponent<NpcMobile>();
            if (!_hooked)
            {
                _hooked = true;
                mobile.Resolved += () => Build(mobile.BodyId);
                var combat = GetComponent<UOGame.Combat.Combatant>();
                if (combat != null)
                {
                    combat.Dead.OnChange += (previous, next, asServer) => ShowGhost();
                }

                mobile.Bonded.OnChange += (previous, next, asServer) => ShowGhost();
            }

            if (mobile.Def != null)
            {
                Build(mobile.BodyId);
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            ActionAnimator.Unregister(ObjectId);
        }

        private void Build(int body)
        {
            if (_graphic == null || body <= 0)
            {
                return;
            }

            if (_model != null)
            {
                Destroy(_model);
            }

            _catalog = global::UOClient.Content.SecureArt.Resolve(_catalog, global::UOClient.Content.SecureArtAddresses.CreatureCatalog); // paid art by address when the prefab holds none (Docs/SecureArt.md)
            _human = global::UOClient.Content.SecureArt.Resolve(_human, global::UOClient.Content.SecureArtAddresses.HumanModel);
            _humanAnimator = global::UOClient.Content.SecureArt.Resolve(_humanAnimator, global::UOClient.Content.SecureArtAddresses.HumanAnimator);
            _model = ActorModel.Build(body, _graphic, _catalog, _human, _humanAnimator, GetComponent<NpcMobile>().Look, out _animator);
            NpcMobile self = GetComponent<NpcMobile>();
            ActionAnimator.Register(ObjectId, _animator, () => self.Look); // attacks and flinches
            float height = 1.8f;
            if (ActorModel.Bounds(_model, out Bounds bounds))
            {
                height = bounds.max.y - _graphic.position.y;
            }

            GetComponent<NpcMobile>().SetModelHeight(height);
            var steps = GetComponent<UOGame.Combat.Combatant>();
            _footed = GameAudio.Footed(steps);
            _stepFamily = steps != null ? GameAudio.Family(steps) : null;
            _stepScale = height / 1.8f;
            _humanGait = ActionAnimator.IsHumanoid(_animator);
            _last = _graphic.position;
            _renderers = _model != null ? _model.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            _living = new Material[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _living[i] = _renderers[i].sharedMaterials;
            }

            ShowGhost();
        }

        /// <summary>A dead bonded pet walks on as a ghost (other dead creatures leave a corpse and go).</summary>
        private void ShowGhost()
        {
            var mobile = GetComponent<NpcMobile>();
            var combat = GetComponent<UOGame.Combat.Combatant>();
            if (_renderers == null || _ghost == null || mobile == null || combat == null)
            {
                return;
            }

            bool ghost = combat.Dead.Value && mobile.IsPet && mobile.Bonded.Value;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null)
                {
                    continue;
                }

                if (ghost)
                {
                    var ghostly = new Material[_living[i].Length];
                    for (int m = 0; m < ghostly.Length; m++)
                    {
                        ghostly[m] = _ghost;
                    }

                    _renderers[i].sharedMaterials = ghostly;
                }
                else
                {
                    _renderers[i].sharedMaterials = _living[i];
                }
            }
        }

        private void LateUpdate()
        {
            var hiddenBy = GetComponent<UOGame.Combat.Combatant>();
            if (_model != null && hiddenBy != null && _model.activeSelf == hiddenBy.Hidden.Value)
            {
                _model.SetActive(!hiddenBy.Hidden.Value);
            }

            if (_animator == null || Time.deltaTime <= 0f)
            {
                return;
            }

            Vector3 moved = _graphic.position - _last;
            _last = _graphic.position;
            moved.y = 0f;
            _speed = Mathf.Lerp(_speed, moved.magnitude / Time.deltaTime, 1f - Mathf.Exp(-10f * Time.deltaTime));
            // People: Speed in metres a second (the humanoid controller has each clip at its own ground speed);
            // creatures' controllers take 0 idle, 1 walk, 2 run.
            float parameter = _speed < 0.2f ? 0f : _humanGait ? _speed : _speed <= 3f ? _speed / 3f : 1f + Mathf.Clamp01((_speed - 3f) / 3f);
            _animator.SetFloat(SpeedParameter, parameter);
            var mobile = GetComponent<NpcMobile>();
            ActionAnimator.Drive(_animator, hiddenBy != null && hiddenBy.InCombat.Value && !hiddenBy.Dead.Value,
                mobile != null && ActorModel.IsHumanBody(mobile.BodyId) ? mobile.Look : (Look?)null);
            if (_footed && _model != null && _model.activeSelf && (hiddenBy == null || !hiddenBy.Dead.Value))
            {
                _steps.Update(_graphic.position, parameter > 0f ? _speed : 0f, _stepScale, _stepFamily, false);
            }
        }

        private readonly Footsteps _steps = new Footsteps();
        private bool _footed;
        private string _stepFamily;
        private float _stepScale = 1f;
        private bool _humanGait;
    }
}
