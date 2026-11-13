using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient;
using UOClient.Buildings;
using UOGame.Net;

namespace UOGame.Menu
{
    /// <summary>
    /// The main menu (MainMenu scene, loaded over the Game scene): a live street of Britain behind a menu on the left.
    /// <list type="number">
    /// <item>Play Solo, or Multiplayer → Join Game (the connection is remembered; the password too, if asked): the menu
    /// fades and the camera moves to a door.</item>
    /// <item>Logged in: with no characters, the creation panel, and a new random-looking character steps out of the
    /// door; with characters, the list, and the one played last steps out. Choosing another dresses them.</item>
    /// <item>Start Game (offered once a new character's choices hold): a fade to black and the loading screen, which
    /// stays until the world round the character is in (chunks, far versions, room reflections), then fades away and
    /// hands over the controls. This scene unloads then.</item>
    /// </list>
    /// Errors (no answer, refused) bring the menu back with why.
    /// </summary>
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class MenuDirector : MonoBehaviour
    {
        [Tooltip("The camera behind the menu: a Cinemachine camera (the brain on the main camera blends shots by priority).")]
        [SerializeField] private Transform _townShot;
        [Tooltip("Near the door your character steps out of (the nearest unlocked door to this).")]
        [SerializeField] private Transform _door;
        [Tooltip("The camera at the door: a Cinemachine camera whose rotation composer keeps your character centred.")]
        [SerializeField] private Transform _doorShot;
        [Tooltip("Where your character walks to out of the door, and turns to the camera.")]
        [SerializeField] private Transform _standSpot;
        [SerializeField] private MenuWalkers _walkers;

        private enum Stage { Menu, Connecting, Characters, Entering, Loading, Done }

        private const float LoadTimeout = 90f;

        private Stage _stage = Stage.Menu;
        private VisualElement _root, _menuPanel, _characterPanel, _selectPage, _createPage, _loading, _loadingFill, _fade;
        private VisualElement _mainPage, _multiplayerPage, _joinPage, _optionsPage;
        private Label _status, _characterStatus, _busy, _loadingWhat, _loadingPercent;
        private ScrollView _characterList;
        private Button _start;
        private TextField _address, _account, _password;
        private IntegerField _port;
        private Toggle _saveCredentials;
        private UI.OptionsPanel _options;
        private UI.CharacterCreator _creator;

        private CharacterSummary[] _characters = Array.Empty<CharacterSummary>();
        private int _selected;
        private bool _creating;

        private BuildingDoor _doorObject;
        private Vector3 _inside, _outside;
        private MenuActor _standIn;
        private Look _standInLook;
        private bool _standInOut;
        private Coroutine _stepOut;

        private const int ShotOff = 0, TownPriority = 10, DoorPriority = 20;
        private const float BlendSeconds = 2.5f;
        private CinemachineCamera _townCam, _doorCam;
        private CinemachineRotationComposer _doorComposer;
        private CinemachineBrain _brain;
        private bool _ownBrain;

        private readonly Dictionary<VisualElement, (float Target, float Speed, Action Done)> _fades = new Dictionary<VisualElement, (float, float, Action)>();
        private float _loadStart, _shown, _settledAt = -1f;
        private bool _openedOnTown;
        private GameCamera _gameCamera;

        private static GameBoot Boot => GameBoot.Instance;

        // ------------------------------------------------------------------ setup

        private void OnEnable() => GetComponent<PanelRenderer>().RegisterUIReloadCallback(OnUIReload);

        private void OnDisable() => GetComponent<PanelRenderer>().UnregisterUIReloadCallback(OnUIReload);

        private void Start()
        {
            if (_townShot != null)
            {
                GameWorld.Follow(_townShot); // the town round the menu loads
            }

            if (_door != null)
            {
                GameWorld.Follow(_door);
            }

            _gameCamera = FindFirstObjectByType<GameCamera>();
            SetUpShots();

            if (Boot != null)
            {
                Boot.LoggedIn += OnLoggedIn;
                Boot.Failed += OnFailed;
                Boot.Refused += OnRefused;
            }

            GameInput.SetTyping(false);
        }

        private void OnDestroy()
        {
            if (Boot != null)
            {
                Boot.LoggedIn -= OnLoggedIn;
                Boot.Failed -= OnFailed;
                Boot.Refused -= OnRefused;
            }

            GameWorld.Unfollow(_townShot);
            GameWorld.Unfollow(_door);
            DropBrain();
            _creator?.Dispose();
            if (_gameCamera != null)
            {
                _gameCamera.enabled = true;
            }
        }

        private void OnUIReload(PanelRenderer panel, VisualElement root, int version)
        {
            _root = root;
            _menuPanel = root.Q("menu-panel");
            _characterPanel = root.Q("character-panel");
            _mainPage = root.Q("main-page");
            _multiplayerPage = root.Q("multiplayer-page");
            _joinPage = root.Q("join-page");
            _optionsPage = root.Q("options-page");
            _selectPage = root.Q("select-page");
            _createPage = root.Q("create-page");
            _loading = root.Q("loading");
            _loadingFill = root.Q("loading-fill");
            _loadingWhat = root.Q<Label>("loading-what");
            _loadingPercent = root.Q<Label>("loading-percent");
            _fade = root.Q("fade");
            _status = root.Q<Label>("status");
            _characterStatus = root.Q<Label>("character-status");
            _busy = root.Q<Label>("busy");
            _characterList = root.Q<ScrollView>("character-list");
            _start = root.Q<Button>("start-button");
            _address = root.Q<TextField>("address");
            _port = root.Q<IntegerField>("port");
            _account = root.Q<TextField>("account");
            _password = root.Q<TextField>("password");
            _saveCredentials = root.Q<Toggle>("save-credentials");

            root.Q<Button>("solo-button").clicked += () => Connect(() => Boot.PlaySolo());
            root.Q<Button>("multiplayer-button").clicked += () => ShowPage(_multiplayerPage);
            root.Q<Button>("options-button").clicked += () => { _options.Open(); ShowPage(_optionsPage); };
            root.Q<Button>("exit-button").clicked += Quit;
            Button host = root.Q<Button>("host-button");
            host.SetEnabled(false);
            host.tooltip = "Coming later.";
            root.Q<Button>("join-button").clicked += ShowJoin;
            root.Q<Button>("multiplayer-back").clicked += () => ShowPage(_mainPage);
            root.Q<Button>("join-back").clicked += () => ShowPage(_multiplayerPage);
            root.Q<Button>("connect-button").clicked += Join;
            root.Q<Button>("new-character").clicked += ShowCreate;
            root.Q<Button>("character-back").clicked += CharacterBack;
            _start.clicked += StartGame;

            _options = new UI.OptionsPanel(() => ShowPage(_mainPage));
            _optionsPage.Add(_options.Root);

            _creator?.Dispose();
            _creator = new UI.CharacterCreator(_createPage, request => Boot.Send(request), embedded: true);
            _creator.LookChanged += look => ShowStandIn(look);
            _creator.ValidityChanged += _ => UpdateStart();

            foreach (TextField field in new[] { _address, _account, _password })
            {
                field.RegisterCallback<FocusInEvent>(_ => GameInput.SetTyping(true));
                field.RegisterCallback<FocusOutEvent>(_ => GameInput.SetTyping(false));
            }

            GameAudio.WireClicks(root);
            ShowPage(_mainPage);

            // Black until the town behind the menu is in, then it fades up.
            SetOpacity(_fade, 1f);
            _openedOnTown = false;
            StartupCurtain.Lift(); // the menu's own black takes over from the one over the game's first frames
        }

        // ------------------------------------------------------------------ the menu

        private void ShowPage(VisualElement page)
        {
            foreach (VisualElement p in new[] { _mainPage, _multiplayerPage, _joinPage, _optionsPage })
            {
                p.EnableInClassList("hidden", p != page);
            }

            _menuPanel.style.width = page == _optionsPage ? 560 : StyleKeyword.Null;
        }

        private void ShowJoin()
        {
            _address.value = PlayerPrefs.GetString("game.address", Application.isEditor ? "127.0.0.1" : GameBoot.DefaultServer);
            _port.value = PlayerPrefs.GetInt("game.port", GameBoot.DefaultPort);
            _account.value = PlayerPrefs.GetString("game.account", string.Empty);
            bool save = PlayerPrefs.GetInt("game.saveCredentials", 0) == 1;
            _saveCredentials.value = save;
            _password.value = save ? PlayerPrefs.GetString("game.password", string.Empty) : string.Empty;
            ShowPage(_joinPage);
        }

        private void Join()
        {
            string address = _address.value?.Trim() ?? string.Empty;
            ushort port = (ushort)Mathf.Clamp(_port.value, 1, 65535);
            PlayerPrefs.SetString("game.address", address);
            PlayerPrefs.SetInt("game.port", port);
            PlayerPrefs.SetString("game.account", _account.value?.Trim() ?? string.Empty);
            PlayerPrefs.SetInt("game.saveCredentials", _saveCredentials.value ? 1 : 0);
            if (_saveCredentials.value)
            {
                PlayerPrefs.SetString("game.password", _password.value ?? string.Empty);
            }
            else
            {
                PlayerPrefs.DeleteKey("game.password");
            }

            PlayerPrefs.Save();
            Connect(() => Boot.Join(address, port, _account.value, _password.value));
        }

        /// <summary>The menu fades, the camera heads for the door, and we connect.</summary>
        private void Connect(Action connect)
        {
            if (Boot == null || _stage != Stage.Menu)
            {
                return;
            }

            _stage = Stage.Connecting;
            _status.text = string.Empty;
            _status.RemoveFromClassList("error");
            GameInput.SetTyping(false);
            FadeTo(_menuPanel, 0f, 3f, () => _menuPanel.AddToClassList("hidden"));
            Busy("Connecting…");
            FindDoor();
            ShowDoor(true);
            connect();
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ logged in: characters

        private void OnLoggedIn(LoginResult result)
        {
            if (_stage != Stage.Connecting)
            {
                return;
            }

            _stage = Stage.Characters;
            Busy(null);
            _characters = result.Characters ?? Array.Empty<CharacterSummary>();
            _characterStatus.text = string.Empty;
            _characterPanel.RemoveFromClassList("hidden");
            SetOpacity(_characterPanel, 0f);
            FadeTo(_characterPanel, 1f, 2.5f);
            if (_characters.Length == 0)
            {
                ShowCreate();
            }
            else
            {
                int last = Array.Exists(_characters, c => c.Id == result.LastPlayed) ? result.LastPlayed : _characters[0].Id;
                ShowSelect(last);
            }
        }

        private void ShowSelect(int id)
        {
            _creating = false;
            _selectPage.RemoveFromClassList("hidden");
            _createPage.AddToClassList("hidden");
            _creator.Close();
            _characterList.Clear();
            foreach (CharacterSummary c in _characters)
            {
                CharacterSummary character = c;
                var b = new Button(() => Select(character.Id)) { text = character.Name, name = "character-" + character.Id };
                b.AddToClassList("choice-button");
                _characterList.Add(b);
            }

            Select(id);
        }

        private void Select(int id)
        {
            _selected = id;
            foreach (VisualElement b in _characterList.Children())
            {
                b.EnableInClassList("choice-selected", b.name == "character-" + id);
            }

            CharacterSummary chosen = Array.Find(_characters, c => c.Id == id);
            ShowStandIn(chosen.Look);
            UpdateStart();
        }

        private void ShowCreate()
        {
            _creating = true;
            _selectPage.AddToClassList("hidden");
            _createPage.RemoveFromClassList("hidden");
            _creator.Open(string.Empty); // a random look: the new character steps out wearing it
            UpdateStart();
        }

        private void UpdateStart()
        {
            _start?.SetEnabled(_stage == Stage.Characters && (_creating ? _creator.Valid : _selected != 0));
        }

        private void CharacterBack()
        {
            if (_creating && _characters.Length > 0)
            {
                ShowSelect(_selected != 0 ? _selected : _characters[0].Id);
                return;
            }

            BackToMenu(null);
        }

        /// <summary>Disconnect and return to the menu (with why, if something went wrong).</summary>
        private void BackToMenu(string why)
        {
            Boot?.Leave();
            _stage = Stage.Menu;
            Busy(null);
            _creator.Close();
            if (_stepOut != null)
            {
                StopCoroutine(_stepOut);
                _stepOut = null;
            }

            if (_standIn != null)
            {
                Destroy(_standIn.gameObject);
            }

            _standInOut = false;
            if (_doorObject != null && _doorObject.State == DoorState.Open)
            {
                _doorObject.SetOpen(false);
            }

            if (_gameCamera != null)
            {
                _gameCamera.enabled = true;
            }

            FadeTo(_characterPanel, 0f, 3f, () => _characterPanel.AddToClassList("hidden"));
            _loading.AddToClassList("hidden");
            FadeTo(_fade, 0f, 2f);
            _menuPanel.RemoveFromClassList("hidden");
            FadeTo(_menuPanel, 1f, 2.5f);
            ShowPage(_mainPage);
            _status.text = why ?? string.Empty;
            _status.EnableInClassList("error", !string.IsNullOrEmpty(why));
            ShowDoor(false);
        }

        private void OnFailed(string why)
        {
            if (_stage != Stage.Menu && _stage != Stage.Done && PlayerMotor.Local == null)
            {
                BackToMenu(why);
            }
        }

        private void OnRefused(string why)
        {
            if (_stage == Stage.Entering && PlayerMotor.Local == null)
            {
                // Not let in (name taken...): back from black to the panel, with why.
                _stage = Stage.Characters;
                _loading.AddToClassList("hidden");
                if (_gameCamera != null)
                {
                    _gameCamera.enabled = true;
                }

                FadeTo(_fade, 0f, 2f);
                UpdateStart();
            }

            if (_characterStatus != null)
            {
                _characterStatus.text = why;
                _characterStatus.AddToClassList("error");
            }
        }

        // ------------------------------------------------------------------ your character at the door

        /// <summary>The nearest door to the marker that opens, and the spots just inside and outside it.</summary>
        private void FindDoor()
        {
            if (_door == null || _doorObject != null)
            {
                return;
            }

            float best = 15f * 15f;
            foreach (BuildingDoor d in BuildingDoor.All)
            {
                if (d == null || !d.CanOpen)
                {
                    continue;
                }

                float dist = (d.OpeningCentre - _door.position).sqrMagnitude;
                if (dist < best)
                {
                    best = dist;
                    _doorObject = d;
                }
            }

            if (_doorObject == null)
            {
                _inside = _outside = _door.position;
                return;
            }

            // Across the doorway: the thin way of the closed leaves, pointing away from the building's middle.
            Vector3 opening = _doorObject.OpeningCentre;
            Vector3 across = Vector3.forward;
            if (_doorObject.Leaves.Count > 0 && _doorObject.Leaves[0].Leaf != null &&
                _doorObject.Leaves[0].Leaf.GetComponentInChildren<Renderer>() is Renderer leaf)
            {
                across = leaf.bounds.size.x < leaf.bounds.size.z ? Vector3.right : Vector3.forward;
            }

            Transform house = _doorObject.transform;
            while (house != null && !house.name.StartsWith("House_", StringComparison.Ordinal))
            {
                house = house.parent;
            }

            // Outside is the side with open sky overhead (no roof); failing that, away from the building's middle.
            bool skyA = !Physics.Raycast(opening + across * 2.5f + Vector3.up * 1.5f, Vector3.up, 40f, ~0, QueryTriggerInteraction.Ignore);
            bool skyB = !Physics.Raycast(opening - across * 2.5f + Vector3.up * 1.5f, Vector3.up, 40f, ~0, QueryTriggerInteraction.Ignore);
            Vector3 outward;
            if (skyA != skyB)
            {
                outward = skyA ? across : -across;
            }
            else
            {
                Vector3 middle = house != null && house.Find("Interior") is Transform interior && Extent(interior, out Bounds b) ? b.center : opening - across;
                outward = Vector3.Dot(opening - middle, across) >= 0f ? across : -across;
            }
            _inside = MenuActor.Ground(opening - outward * 1.6f);
            _outside = MenuActor.Ground(_standSpot != null ? _standSpot.position : opening + outward * 2.8f);

        }

        private static bool Extent(Transform t, out Bounds b)
        {
            bool any = false;
            b = new Bounds();
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (!any)
                {
                    b = r.bounds;
                    any = true;
                }
                else
                {
                    b.Encapsulate(r.bounds);
                }
            }

            return any;
        }

        /// <summary>The camera at the door: as placed, or out in the street looking at the step, the character right of centre.</summary>
        private (Vector3, Quaternion) DoorShot()
        {
            if (_doorShot != null)
            {
                Vector3 at = _doorShot.position;
                return (at, Quaternion.LookRotation(_outside + Vector3.up * 1.4f - at));
            }

            Vector3 outward = _outside - _inside;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, outward);
            Vector3 target = _outside + Vector3.up * 1.15f;
            Vector3 pos = _outside + outward * 4.6f + side * 1.2f + Vector3.up * 1.75f;
            Vector3 forward = (target - pos).normalized;
            Vector3 camRight = Vector3.Cross(Vector3.up, forward).normalized;
            // Aim a little left of them, so they stand right of centre, clear of the panel.
            return (pos, Quaternion.LookRotation((target - camRight * 1.1f - pos).normalized));
        }

        /// <summary>Show this look at the door: the first time they step out of it; after that they change in place.</summary>
        private void ShowStandIn(Look look)
        {
            _standInLook = look;
            if (_standIn != null)
            {
                _standIn.Dress(look);
                return;
            }

            if (_stepOut == null)
            {
                _stepOut = StartCoroutine(StepOut());
            }
        }

        private IEnumerator StepOut()
        {
            var view = FindFirstObjectByType<UOClient.Bake.BakedWorldView>();
            while (view != null && !view.IsLoadedAt(_door != null ? _door.position : _outside))
            {
                yield return null;
            }

            FindDoor();
            bool throughDoor = _doorObject != null;
            _standIn = MenuActor.Make("Your character", _standInLook, throughDoor ? _inside : _outside, transform);
            _standIn.KeepClear = false; // out through the doorway
            if (_doorCam != null)
            {
                _doorCam.LookAt = _standIn.transform; // the camera keeps them centred all the way out
            }
            Vector3 outward = _outside - _inside;
            outward.y = 0f;
            if (outward.sqrMagnitude > 0.01f)
            {
                _standIn.transform.rotation = Quaternion.LookRotation(outward);
            }

            if (throughDoor)
            {
                _doorObject.SetOpen(true);
                yield return new WaitForSeconds(0.7f);
                _standIn.WalkTo(_outside, 1.3f);
                while (_standIn != null && !_standIn.Arrived)
                {
                    yield return null;
                }

                yield return new WaitForSeconds(0.4f);
                if (_doorObject != null)
                {
                    _doorObject.SetOpen(false);
                }
            }

            if (_standIn != null)
            {
                _standIn.Face(DoorShot().Item1); // toward where the camera settles
            }

            _standInOut = true;
            _stepOut = null;
        }

        // ------------------------------------------------------------------ into the world

        private void StartGame()
        {
            if (_stage != Stage.Characters)
            {
                return;
            }

            if (_creating)
            {
                if (!_creator.Valid)
                {
                    return;
                }

                _creator.Submit(); // the server makes the character and lets them in
            }
            else if (_selected != 0)
            {
                Boot.Send(new PlayCharacter { Id = _selected });
            }
            else
            {
                return;
            }

            _stage = Stage.Entering;
            _characterStatus.text = string.Empty;
            _characterStatus.RemoveFromClassList("error");
            UpdateStart();
            GameInput.SetTyping(true); // no control until the world is in
            if (_gameCamera != null)
            {
                _gameCamera.enabled = false; // it would jump to your character before the screen is black
            }

            _fade.RemoveFromClassList("hidden");
            FadeTo(_fade, 1f, 1.6f, () =>
            {
                if (_stage != Stage.Entering && _stage != Stage.Loading)
                {
                    return;
                }

                // Black: the menu goes, the loading screen comes up.
                _characterPanel.AddToClassList("hidden");
                _loading.RemoveFromClassList("hidden");
                SetOpacity(_loading, 1f);
                _shown = 0f;
                _loadStart = Time.unscaledTime;
                _settledAt = -1f;
                SetProgress(0.02f, "Waiting for the server");
                FadeTo(_fade, 0f, 2.5f);
            });
        }

        /// <summary>The world round your character: chunks, far versions, room reflections; the bar shows how far.</summary>
        private void UpdateLoading()
        {
            if (_loading.ClassListContains("hidden"))
            {
                return;
            }

            PlayerMotor me = PlayerMotor.Local;
            if (me != null && _stage == Stage.Entering)
            {
                // In: the menu's town and people go (the screen is covered), and the world loads round you instead.
                _stage = Stage.Loading;
                DropBrain();
                GameWorld.Unfollow(_townShot);
                GameWorld.Unfollow(_door);
                if (_walkers != null)
                {
                    Destroy(_walkers.gameObject);
                }

                if (_standIn != null)
                {
                    Destroy(_standIn.gameObject);
                }

                if (_gameCamera != null)
                {
                    _gameCamera.enabled = true;
                }
            }

            float target;
            string what;
            bool done = false;
            if (me == null)
            {
                target = 0.05f;
                what = "Waiting for the server";
            }
            else
            {
                var view = FindFirstObjectByType<UOClient.Bake.BakedWorldView>();
                float world = view != null ? view.Progress : 1f;
                bool ground = GameWorld.IsReadyAt(me.transform.position);
                int probes = RoomProbes.Instance != null ? RoomProbes.Instance.PendingNear : 0;
                float lit = probes == 0 ? 1f : 1f / (1f + probes * 0.25f);
                bool ready = ground && world >= 1f && probes == 0;
                if (ready && _settledAt < 0f)
                {
                    _settledAt = Time.unscaledTime;
                }
                else if (!ready)
                {
                    _settledAt = -1f;
                }

                // A moment after everything's in, for the first frames to draw it.
                float settle = _settledAt < 0f ? 0f : Mathf.Clamp01((Time.unscaledTime - _settledAt) / 0.8f);
                target = 0.15f + world * 0.6f + lit * 0.15f + (ground ? 0.05f : 0f) + settle * 0.05f;
                what = !ground || world < 0.9f ? "Loading the land" : world < 1f ? "Raising the far town" : probes > 0 ? "Lighting the rooms" : "Ready";
                done = settle >= 1f || Time.unscaledTime - _loadStart > LoadTimeout;
            }

            SetProgress(Mathf.Max(_shown, Mathf.MoveTowards(_shown, done ? 1f : target, Time.unscaledDeltaTime * 0.8f)), what);
            if (done && _shown >= 0.999f && _stage == Stage.Loading)
            {
                _stage = Stage.Done;
                FadeTo(_fade, 1f, 2.5f, () =>
                {
                    _loading.AddToClassList("hidden");
                    FadeTo(_fade, 0f, 1.5f, () =>
                    {
                        GameInput.SetTyping(false); // yours
                        GameBoot.HideMenu();
                    });
                });
            }
        }

        private void SetProgress(float value, string what)
        {
            _shown = Mathf.Clamp01(value);
            _loadingFill.style.width = Length.Percent(_shown * 100f);
            _loadingPercent.text = $"{Mathf.RoundToInt(_shown * 100f)}%";
            _loadingWhat.text = what;
        }

        // ------------------------------------------------------------------ every frame

        private void Update()
        {
            if (_root == null)
            {
                return;
            }

            // The town fades up from black once it's in (or after a few seconds regardless).
            if (!_openedOnTown && _stage == Stage.Menu)
            {
                var view = FindFirstObjectByType<UOClient.Bake.BakedWorldView>();
                if (view == null || view.Progress >= 0.95f || Time.timeSinceLevelLoad > 12f)
                {
                    _openedOnTown = true;
                    FadeTo(_fade, 0f, 1f);
                }
            }

            FrameOpenArea();
            UpdateFades();
            UpdateLoading();
            _start.style.display = _stage == Stage.Characters || _stage == Stage.Entering ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ------------------------------------------------------------------ camera shots (Cinemachine)

        /// <summary>
        /// The menu's shots are Cinemachine cameras in this scene; a brain on the main camera blends between them by
        /// priority (the town behind the menu, the door once you play). It goes when the game camera takes over.
        /// </summary>
        private void SetUpShots()
        {
            _townCam = _townShot != null ? _townShot.GetComponent<CinemachineCamera>() : null;
            _doorCam = _doorShot != null ? _doorShot.GetComponent<CinemachineCamera>() : null;
            _doorComposer = _doorShot != null ? _doorShot.GetComponent<CinemachineRotationComposer>() : null;
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            if (!cam.TryGetComponent(out _brain))
            {
                _brain = cam.gameObject.AddComponent<CinemachineBrain>();
                _ownBrain = true;
            }

            _brain.IgnoreTimeScale = true;
            _brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, BlendSeconds);
            if (_townCam != null)
            {
                _townCam.Priority = TownPriority;
            }

            ShowDoor(false);
        }

        /// <summary>To the door shot (looking where your character comes out, then at them), or back to the town.</summary>
        private void ShowDoor(bool on)
        {
            if (_doorCam == null)
            {
                return;
            }

            if (!on || _standIn == null)
            {
                _doorCam.LookAt = _standSpot;
            }

            _doorCam.Priority = on ? DoorPriority : ShotOff;
        }

        /// <summary>
        /// The door camera keeps your character in the middle of the open picture, right of the character panel (half its
        /// width right of the screen's middle), whatever the screen's size.
        /// </summary>
        private void FrameOpenArea()
        {
            if (_doorComposer == null || _root == null || _characterPanel == null)
            {
                return;
            }

            float width = _root.worldBound.width;
            float covered = width > 1f && !_characterPanel.ClassListContains("hidden") ? Mathf.Clamp01(_characterPanel.worldBound.xMax / width) : 0f;
            ScreenComposerSettings composition = _doorComposer.Composition;
            composition.ScreenPosition = new Vector2(covered * 0.5f, composition.ScreenPosition.y);
            _doorComposer.Composition = composition;
        }

        /// <summary>The game camera drives the main camera again: the menu's brain goes (if the menu added it).</summary>
        private void DropBrain()
        {
            if (_brain != null && _ownBrain)
            {
                Destroy(_brain);
            }

            _brain = null;
        }

        private void Busy(string text)
        {
            _busy.text = text ?? string.Empty;
            _busy.EnableInClassList("hidden", string.IsNullOrEmpty(text));
        }

        private static void SetOpacity(VisualElement e, float value) => e.style.opacity = value;

        /// <summary>Fade an element's opacity to a value at this rate (per second), then do something.</summary>
        private void FadeTo(VisualElement e, float target, float speed, Action done = null)
        {
            if (e == null)
            {
                return;
            }

            _fades[e] = (target, speed, done);
        }

        private readonly List<VisualElement> _finished = new List<VisualElement>();

        private void UpdateFades()
        {
            _finished.Clear();
            foreach (KeyValuePair<VisualElement, (float Target, float Speed, Action Done)> pair in _fades)
            {
                float now = pair.Key.resolvedStyle.opacity;
                float next = Mathf.MoveTowards(now, pair.Value.Target, pair.Value.Speed * Time.unscaledDeltaTime);
                pair.Key.style.opacity = next;
                pair.Key.pickingMode = next < 0.05f && pair.Key == _fade ? PickingMode.Ignore : pair.Key == _fade ? PickingMode.Position : pair.Key.pickingMode;
                if (Mathf.Approximately(next, pair.Value.Target))
                {
                    _finished.Add(pair.Key);
                }
            }

            foreach (VisualElement e in _finished)
            {
                Action done = _fades[e].Done;
                _fades.Remove(e);
                done?.Invoke();
            }
        }
    }
}
