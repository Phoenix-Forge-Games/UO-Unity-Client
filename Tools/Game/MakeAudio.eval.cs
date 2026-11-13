// The game's audio setup, in Resources/Audio, for GameAudio:
//   AudioServices (the Phoenix Forge service loader) -> AudioManager -> Music, Ambient, Sfx and UiSfx services,
//   AudioSettings (volume keys and defaults), AudioEvents (the event channel), an AudioSource prefab per channel, and
//   GameMix.mixer: a group per channel with its gain, so channels sit right against each other (the packs differ a lot:
//   the music is mastered loud, -17..-23 dBFS RMS; the ambience loops are quiet, -37..-47; footsteps -19..-30).
//   SoundBank: each sound named in Assets/UOClient/Audio/Sounds.json with its clips, as ForgeAudioClips in
//   Resources/Audio/Clips/<sound>/ (each named after its clip, which the ambient service needs to stop it).
// Run on the open project after editing Sounds.json or installing a sound pack; it overwrites only these files.
string dir = "Assets/UOClient/Resources/Audio";
System.IO.Directory.CreateDirectory(dir);
System.IO.Directory.CreateDirectory(dir + "/Services");

T Asset<T>(string path) where T : UnityEngine.ScriptableObject
{
    var a = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
    if (a == null)
    {
        a = UnityEngine.ScriptableObject.CreateInstance<T>();
        UnityEditor.AssetDatabase.CreateAsset(a, path);
    }

    return a;
}

// ---------------------------------------------------------------- the mix: a group per channel, at its gain (dB)
var editorAsm = typeof(UnityEditor.Editor).Assembly;
var mixerType = editorAsm.GetType("UnityEditor.Audio.AudioMixerController");
var groupType = editorAsm.GetType("UnityEditor.Audio.AudioMixerGroupController");
string mixPath = $"{dir}/GameMix.mixer";
var mixer = UnityEditor.AssetDatabase.LoadAssetAtPath(mixPath, mixerType)
            ?? mixerType.GetMethod("CreateMixerControllerAtPath").Invoke(null, new object[] { mixPath });
var master = mixerType.GetProperty("masterGroup").GetValue(mixer);
var snapshot = mixerType.GetProperty("TargetSnapshot").GetValue(mixer);
UnityEngine.Audio.AudioMixerGroup Group(string name, float db)
{
    object group = null;
    foreach (var child in (System.Array)groupType.GetProperty("children").GetValue(master))
    {
        if (((UnityEngine.Object)child).name == name) group = child;
    }

    if (group == null)
    {
        group = mixerType.GetMethod("CreateNewGroup").Invoke(mixer, new object[] { name, false });
        mixerType.GetMethod("AddChildToParent").Invoke(mixer, new object[] { group, master });
    }

    groupType.GetMethod("SetValueForVolume").Invoke(group, new object[] { mixer, snapshot, db });
    return (UnityEngine.Audio.AudioMixerGroup)group;
}

// Music down, the quiet ambience loops up to sit level with it; effects as recorded (combat hits already peak at
// full scale), so footsteps and voices stand out over the quieter music.
var musicGroup = Group("Music", -10f);
var ambientGroup = Group("Ambient", 10f);
var sfxGroup = Group("Sfx", 0f);
var uiGroup = Group("UI", 0f);
UnityEditor.EditorUtility.SetDirty((UnityEngine.Object)mixer);

UnityEngine.GameObject SourcePrefab(string name, float blend, UnityEngine.Audio.AudioMixerGroup output)
{
    string path = $"{dir}/Services/{name}.prefab";
    var go = new UnityEngine.GameObject(name);
    var src = go.AddComponent<UnityEngine.AudioSource>();
    src.outputAudioMixerGroup = output;
    src.playOnAwake = false;
    src.spatialBlend = blend;
    src.rolloffMode = UnityEngine.AudioRolloffMode.Linear;
    src.dopplerLevel = 0f;
    var prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, path);
    UnityEngine.Object.DestroyImmediate(go);
    return prefab;
}

var settings = Asset<PhoenixForgeGames.Audio.Constants.ForgeAudioConstants>($"{dir}/AudioSettings.asset");
settings.audioContainerName = "Game Audio";
settings.masterVolumeKey = "uo.volume.master";
settings.musicVolumeKey = "uo.volume.music";
settings.ambientVolumeKey = "uo.volume.ambient";
settings.sfxVolumeKey = "uo.volume.sfx";
settings.uiSfxVolumeKey = "uo.volume.ui";
settings.voiceVolumeKey = "uo.volume.voice";
settings.defaultMasterVolume = 0.8f;
settings.defaultMusicVolume = 0.45f;
settings.defaultAmbientVolume = 0.5f;
settings.defaultSfxVolume = 0.8f;
settings.defaultUiSfxVolume = 0.6f;
UnityEditor.EditorUtility.SetDirty(settings);

var events = Asset<PhoenixForgeGames.Audio.Events.ForgeAudioEventChannel>($"{dir}/AudioEvents.asset");
var spatial = SourcePrefab("Source 3D", 1f, sfxGroup);
var flat = SourcePrefab("Source 2D", 0f, uiGroup);
var musicSource = SourcePrefab("Source Music", 0f, musicGroup);
var ambientSource = SourcePrefab("Source Ambient", 0f, ambientGroup);

S Service<S>(string name, PhoenixForgeGames.Audio.Enums.ForgeAudioChannelType type, UnityEngine.GameObject prefab, int pool) where S : PhoenixForgeGames.Audio.Service.AudioServiceBase
{
    var s = Asset<S>($"{dir}/Services/{name}.asset");
    s.audioServiceType = type;
    s.forgeAudioEventChannel = events;
    s.fadeDuration = 2f;
    var so = new UnityEditor.SerializedObject(s);
    so.FindProperty("audioSourcePrefab").objectReferenceValue = prefab;
    so.FindProperty("defaultPoolSize").intValue = pool;
    so.ApplyModifiedPropertiesWithoutUndo();
    UnityEditor.EditorUtility.SetDirty(s);
    return s;
}

var music = Service<PhoenixForgeGames.Audio.Service.ForgeMusicService>("Music", PhoenixForgeGames.Audio.Enums.ForgeAudioChannelType.Music, musicSource, 3);
music.poolSize = 3;
music.requeueMusic = false; // GameAudio queues the next track for wherever you are
music.fadeDuration = 3f;
var ambient = Service<PhoenixForgeGames.Audio.Service.ForgeAmbientService>("Ambient", PhoenixForgeGames.Audio.Enums.ForgeAudioChannelType.Ambient, ambientSource, 12);
var sfx = Service<PhoenixForgeGames.Audio.Service.ForgeSfxService>("Sfx", PhoenixForgeGames.Audio.Enums.ForgeAudioChannelType.Sfx, spatial, 128); // footsteps of a busy street, fights and spells at once
var ui = Service<PhoenixForgeGames.Audio.Service.ForgeUiSfxService>("UiSfx", PhoenixForgeGames.Audio.Enums.ForgeAudioChannelType.UiSfx, flat, 16);

var manager = Asset<PhoenixForgeGames.Audio.Service.ForgeAudioManagerService>($"{dir}/Services/AudioManager.asset");
manager.forgeAudioConstants = settings;
manager.forgeAudioEventChannel = events;
manager.audioServices = new System.Collections.Generic.List<PhoenixForgeGames.Audio.Service.AudioServiceBase> { music, ambient, sfx, ui };
UnityEditor.EditorUtility.SetDirty(manager);

var loader = Asset<PhoenixForgeGames.Core.Shared.ForgeServiceLoader>($"{dir}/AudioServices.asset");
var list = loader.GetList();
list.Clear();
list.Add(manager);
UnityEditor.EditorUtility.SetDirty(loader);

// ---------------------------------------------------------------- the sound bank
var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Assets/UOClient/Audio/Sounds.json"));
var allClips = new System.Collections.Generic.List<(string Path, string Name)>();
foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:AudioClip"))
{
    string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
    allClips.Add((p, System.IO.Path.GetFileNameWithoutExtension(p)));
}

System.Text.RegularExpressions.Regex Glob(string pattern) => new System.Text.RegularExpressions.Regex(
    "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$",
    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

var bank = Asset<UOGame.SoundBank>($"{dir}/SoundBank.asset");
bank.Entries.Clear();
string report = "";
int missing = 0, streamed = 0;
foreach (var prop in ((Newtonsoft.Json.Linq.JObject)json["Sounds"]).Properties())
{
    string key = prop.Name;
    var def = (Newtonsoft.Json.Linq.JObject)prop.Value;
    bool isMusic = key.StartsWith("music."), isAmbient = key.StartsWith("ambient."), isUi = key.StartsWith("ui.");
    bool flatSound = isMusic || isAmbient || isUi || key == "thunder";
    float min = (float?)def["Min"] ?? 2f, max = (float?)def["Max"] ?? 25f;
    var entry = new UOGame.SoundBank.Entry { Key = key };
    string clipDir = $"{dir}/Clips/{key}";
    var keep = new System.Collections.Generic.HashSet<string>();
    foreach (var pat in (Newtonsoft.Json.Linq.JArray)def["Clips"] ?? new Newtonsoft.Json.Linq.JArray())
    {
        string pattern = (string)pat;
        var rx = Glob(pattern);
        bool byPath = pattern.Contains("/");
        int found = 0;
        foreach (var c in allClips)
        {
            if (!(byPath ? rx.IsMatch(c.Path) || rx.IsMatch(c.Path.Substring(0, c.Path.Length - System.IO.Path.GetExtension(c.Path).Length)) : rx.IsMatch(c.Name)))
            {
                continue;
            }

            System.IO.Directory.CreateDirectory(clipDir);
            string path = $"{clipDir}/{c.Name}.asset";
            if (!keep.Add(path))
            {
                continue;
            }

            // Music and ambience loops are minutes long: stream them from disk rather than holding them
            // decompressed in memory (the music packs alone are about a gigabyte that way).
            if ((isMusic || isAmbient) && UnityEditor.AssetImporter.GetAtPath(c.Path) is UnityEditor.AudioImporter imp)
            {
                var s = imp.defaultSampleSettings;
                if (s.loadType != UnityEngine.AudioClipLoadType.Streaming || imp.ContainsSampleSettingsOverride("Standalone"))
                {
                    s.loadType = UnityEngine.AudioClipLoadType.Streaming;
                    s.compressionFormat = UnityEngine.AudioCompressionFormat.Vorbis;
                    s.quality = 0.7f;
                    s.preloadAudioData = false;
                    imp.defaultSampleSettings = s;
                    imp.ClearSampleSettingOverride("Standalone");
                    imp.SaveAndReimport();
                    streamed++;
                }
            }

            var fc = Asset<PhoenixForgeGames.Audio.Objects.ForgeAudioClip>(path);
            fc.audioClip = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AudioClip>(c.Path);
            fc.isSpatial = false;
            fc.spatialValue = flatSound ? 0f : 1f;
            fc.minDistance = min;
            fc.maxDistance = max;
            UnityEditor.EditorUtility.SetDirty(fc);
            entry.Clips.Add(fc);
            found++;
        }

        if (found == 0)
        {
            report += $"\n  {key}: nothing matches '{pattern}'";
            missing++;
        }
    }

    // Clip assets no longer named for this sound go.
    if (UnityEditor.AssetDatabase.IsValidFolder(clipDir))
    {
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:ForgeAudioClip", new[] { clipDir }))
        {
            string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (!keep.Contains(p))
            {
                UnityEditor.AssetDatabase.DeleteAsset(p);
            }
        }
    }

    bank.Entries.Add(entry);
    report += entry.Clips.Count > 0 ? $"\n  {key}: {entry.Clips.Count}" : "";
}

UnityEditor.EditorUtility.SetDirty(bank);
UnityEditor.AssetDatabase.SaveAssets();
int withClips = bank.Entries.FindAll(e => e.Clips.Count > 0).Count;
return $"audio services in {dir}; {allClips.Count} clips in the project; {withClips}/{bank.Entries.Count} sounds have clips ({missing} patterns matched nothing; {streamed} clips set to stream){report}";
