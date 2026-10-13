using System;
using System.Collections.Generic;
using System.IO;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllNativeContentWindow : EditorWindow
    {
        const int PageSize = 50;
        NativeContentCatalog Catalog;
        string CatalogPath = "";
        readonly List<NativeContentEntry> Visible = new List<NativeContentEntry>();
        NativeContentEntry Selected;
        string Search = "";
        int Page;
        Vector2 Scroll;
        Texture2D Thumbnail;
        PllProfilePublication.Target Profile;
        string BundlePath = "";
        string SeedText = "preview";
        int TargetOccurrence;
        string[] OccurrenceLabels = Array.Empty<string>();
        int ExpireMinutes = 60;
        NativeContentRequest Queued;
        string RequestId => Queued == null ? "" : Queued.Id;
        string ResultSummary = "";

        [MenuItem("PLL/Native Content/Browser and PEAK Preview", priority = 40)]
        static void Open() => GetWindow<PllNativeContentWindow>("Native Content").Show();

        void OnDisable() => ReleaseThumbnail();

        void OnGUI()
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            try
            {
                if (GUILayout.Button("Import Native Catalog"))
                {
                    var Path = EditorUtility.OpenFilePanel("Import PEAK native content catalog", "", "json");
                    if (Path != "")
                    {
                        var Imported = ReadCatalog(Path);
                        ReleaseThumbnail(); Catalog = Imported; CatalogPath = Path; Selected = null;
                        OccurrenceLabels = Array.ConvertAll(Catalog.Occurrences, Value =>
                            Value.SlotIndex + ": " + Value.SourceId + " (occurrence " + Value.OccurrenceRequestIndex + ")");
                        TargetOccurrence = 0; Page = 0; Filter();
                    }
                }
                if (Catalog != null)
                {
                    EditorGUILayout.LabelField("Captured runtime", Catalog.Identity.RuntimeMvid);
                    EditorGUILayout.LabelField("PEAK SHA-256", Catalog.Identity.PeakSha256);
                    EditorGUILayout.LabelField("Bundle", Catalog.Identity.BundleFile);
                    EditorGUI.BeginChangeCheck();
                    Search = EditorGUILayout.TextField("Search", Search);
                    if (EditorGUI.EndChangeCheck()) { Page = 0; Filter(); }
                    var Begin = Page * PageSize; var End = Math.Min(Begin + PageSize, Visible.Count);
                    for (var i = Begin; i < End; i++)
                    {
                        var Entry = Visible[i];
                        if (GUILayout.Toggle(ReferenceEquals(Selected, Entry), Entry.Kind + ": " + Entry.Name,
                                "Button") && !ReferenceEquals(Selected, Entry))
                        {
                            ReleaseThumbnail(); Selected = Entry;
                            if (Entry.ImageFile != "") Thumbnail = ReadThumbnail(CatalogPath, Entry);
                        }
                    }
                    EditorGUILayout.BeginHorizontal();
                    using (new EditorGUI.DisabledScope(Page == 0))
                        if (GUILayout.Button("Previous")) Page--;
                    EditorGUILayout.LabelField(Visible.Count + " selectors; page " + (Page + 1));
                    using (new EditorGUI.DisabledScope(End >= Visible.Count))
                        if (GUILayout.Button("Next")) Page++;
                    EditorGUILayout.EndHorizontal();
                    DrawSelected();
                    foreach (var Rig in Catalog.Rigs)
                        if (Rig.Actor == "RopeDynamic" && Rig.Skins.Length == 1)
                        {
                            if (GUILayout.Button("Apply Custom Rope Appearance")) PllApplyRopeAppearanceWindow.Open(Rig);
                        }
                        else if (Rig.Actor is "Beetle" or "Condor" && Rig.Skins.Length != 0
                            && GUILayout.Button("Bind Artist Mesh to " + Rig.Actor + " Rig"))
                            PllApplyEntityAppearanceWindow.Open(Rig);
                    EditorGUILayout.HelpBox("The catalog lists native content and rig settings; it contains no game prefabs or code. Content Spawner selects native prop families in PEAK.", MessageType.Info);
                }
                DrawProfile();
            }
            catch (Exception Failure) when (Failure is IOException || Failure is UnauthorizedAccessException
                || Failure is InvalidOperationException || Failure is ArgumentException
                || Failure is System.Security.SecurityException || Failure is BadImageFormatException)
            {
                EditorUtility.DisplayDialog("Native Content", Failure.Message, "Close");
            }
            EditorGUILayout.EndScrollView();
        }

        void Filter()
        {
            Visible.Clear();
            foreach (var Entry in Catalog.Entries)
                if (Search == "" || Entry.Name.IndexOf(Search, StringComparison.OrdinalIgnoreCase) >= 0
                    || Entry.Kind.ToString().IndexOf(Search, StringComparison.OrdinalIgnoreCase) >= 0)
                    Visible.Add(Entry);
        }

        void DrawSelected()
        {
            if (Selected == null) return;
            EditorGUILayout.LabelField("Selected operation", Selected.Kind + ": " + Selected.Name);
            if (Thumbnail)
            {
                EditorGUILayout.LabelField("Image source", Selected.PoseProvenance);
                GUILayout.Label(Thumbnail, GUILayout.Width(256), GUILayout.Height(256));
            }
            else EditorGUILayout.HelpBox(Selected.ImageAbsence, MessageType.Info);
            if (Selected.SelectionFailure != "")
            {
                EditorGUILayout.HelpBox(Selected.SelectionFailure, MessageType.Error);
                return;
            }
            var Spawner = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<PllNativeSpawnerAuthoring>() : null;
            if (Selected.Kind is NativeContentPickKind.ForceName or NativeContentPickKind.Item)
                using (new EditorGUI.DisabledScope(!Spawner))
                    if (GUILayout.Button("Add to Selected Food or Content Spawner")) AddToSpawner(Spawner, Selected.Name);
            if (Selected.Kind == NativeContentPickKind.ForceName)
            {
                EditorGUILayout.LabelField("Candidate family size", Selected.CandidateCount.ToString());
                EditorGUILayout.HelpBox("Selects the prop family by its exact force name, not an individual variant.", MessageType.Info);
                if (GUILayout.Button("Add Force Spawner to Selected Source"))
                    PllCreateForceSpawnerWindow.Create(Selection.activeGameObject, Selected.Name);
            }
            else if (Selected.Kind == NativeContentPickKind.Item)
            {
                EditorGUILayout.LabelField("Item identity", Selected.ItemId + " / " + Selected.ComponentType);
                if (GUILayout.Button("Create Identification-Only Item Prefab")) CreateItemCarrier(Selected);
            }
            else if (Selected.Kind == NativeContentPickKind.Visual)
            {
                if (GUILayout.Button("Add Selected Native Visual Preset")) ApplyVisual(Selected);
            }
            else if (GUILayout.Button("Open Existing Recipe Authoring"))
            {
                if (Selected.Kind == NativeContentPickKind.Resolver)
                {
                    if (!Enum.TryParse(Selected.Id, out PLLNativeResolverKind Kind)
                        || Kind == PLLNativeResolverKind.None || Selected.Id != Kind.ToString())
                        throw new InvalidOperationException("The captured native resolver is unsupported.");
                    PllCreateNativeResolverWindow.Open(Kind);
                }
                else if (Selected.Id == typeof(PllBeetleZoneSource).FullName)
                    PllCreateNativeActorZoneWindow.Open(new PllBeetleZoneSource());
                else if (Selected.Id == typeof(PllMushroomZombieZoneSource).FullName)
                    PllCreateNativeActorZoneWindow.Open(new PllMushroomZombieZoneSource());
                else if (Selected.Id == typeof(PllCondorZoneSource).FullName)
                    PllCreateNativeActorZoneWindow.Open(new PllCondorZoneSource());
                else if (Selected.Id == typeof(PllTornadoZoneSource).FullName)
                    PllCreateNativeActorZoneWindow.Open(new PllTornadoZoneSource());
                else if (Selected.Id == typeof(PllTumbleweedZoneSource).FullName)
                    PllCreateNativeActorZoneWindow.Open(new PllTumbleweedZoneSource());
                else throw new InvalidOperationException("The captured native actor is unsupported.");
            }
        }

        void DrawProfile()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Game Profile", EditorStyles.boldLabel);
            if (!NativeContentRequestFile.IsSupported)
            {
                EditorGUILayout.HelpBox("Sending capture or preview requests to PEAK requires Windows. You can still import catalogs and configure spawners.", MessageType.Info);
                return;
            }
            if (GUILayout.Button("Select and Inspect Game Profile"))
            {
                var Root = EditorUtility.OpenFolderPanel("Select the profile containing BepInEx", "", "");
                if (Root != "")
                {
                    var Runtime = EditorUtility.OpenFilePanel("Select the installed PLL runtime DLL",
                        Path.Combine(Root, "BepInEx", "plugins"), "dll");
                    if (Runtime != "")
                    {
                        Profile = PllProfilePublication.Target.Inspect(Root, Runtime, typeof(PLLLibrary).Assembly.FullName);
                        BundlePath = ""; Queued = null; ResultSummary = "";
                    }
                }
            }
            if (Profile == null) return;
            EditorGUILayout.LabelField("Profile", Profile.Root);
            if (GUILayout.Button("Inspect Pending Queue"))
            {
                var Path = System.IO.Path.Combine(Profile.Root, "BepInEx", "plugins",
                    NativeContentArtifact.DirectoryName, NativeContentArtifact.RequestFile);
                if (!File.Exists(Path)) ResultSummary = "This profile has no pending native content request.";
                else
                {
                    var Request = ReadJson<NativeContentRequest>(Path, NativeContentArtifact.MaximumRequestBytes,
                        FileShare.Read | FileShare.Delete);
                    RequireRecordedRequest(Request); Queued = Request;
                    ResultSummary = "The pending request has been loaded.";
                }
            }
            if (GUILayout.Button("Open Published Result"))
            {
                var Directory = Path.Combine(Profile.Root, "BepInEx", "plugins", NativeContentArtifact.DirectoryName);
                var ResultPath = EditorUtility.OpenFilePanel("Select a published native content result", Directory, "json");
                if (ResultPath != "")
                {
                    if (!PllCreatorFileSystem.Inside(ResultPath, Directory)
                        || Path.GetFileName(ResultPath) != NativeContentArtifact.ResultFile)
                        throw new InvalidOperationException("Select " + NativeContentArtifact.ResultFile + " inside this profile's native content output.");
                    var Request = ReadJson<NativeContentRequest>(Path.Combine(
                        Path.GetDirectoryName(ResultPath), NativeContentArtifact.RequestFile),
                        NativeContentArtifact.MaximumRequestBytes);
                    RequireRecordedRequest(Request);
                    if (Path.GetFileName(Path.GetDirectoryName(ResultPath)) != Request.Id)
                        throw new InvalidOperationException("The result directory does not match the recorded request.");
                    Queued = Request; ReadResult();
                }
            }
            if (GUILayout.Button("Select Installed Bundle"))
            {
                var Path = EditorUtility.OpenFilePanel("Select the installed .pll bundle", Profile.BundleDirectory, "pll");
                if (Path != "")
                {
                    if (!PllCreatorFileSystem.Inside(Path, Profile.BundleDirectory))
                        throw new InvalidOperationException("Select a bundle inside this profile's PLL_Biomes directory.");
                    BundlePath = Path;
                }
            }
            EditorGUILayout.LabelField("Bundle", BundlePath);
            ExpireMinutes = EditorGUILayout.IntField("Queue lifetime (minutes)", ExpireMinutes);
            using (new EditorGUI.DisabledScope(BundlePath == ""))
                if (GUILayout.Button("Queue Native Catalog Export")) Queue(NativeContentRequestKind.Export);
            if (Catalog != null && Catalog.Occurrences.Length != 0)
            {
                SeedText = EditorGUILayout.TextField("Seed Text", SeedText);
                TargetOccurrence = EditorGUILayout.Popup("Requested occurrence", TargetOccurrence, OccurrenceLabels);
                EditorGUILayout.LabelField("Native ascent", Catalog.Ascent.ToString());
                EditorGUILayout.LabelField("Native settings SHA-256", Catalog.NativeRunSettingsSha256);
                using (new EditorGUI.DisabledScope(BundlePath == "" || SeedText == ""))
                    if (GUILayout.Button("Queue Exact Snapshot PEAK Preview")) Queue(NativeContentRequestKind.Preview);
            }
            if (RequestId != "")
            {
                EditorGUILayout.LabelField("Request", RequestId);
                if (GUILayout.Button("Read Request Result")) ReadResult();
                if (GUILayout.Button("Cancel Queued Request")) CancelQueue();
                if (ResultSummary != "") EditorGUILayout.HelpBox(ResultSummary, MessageType.Info);
            }
            EditorGUILayout.HelpBox("Launch PEAK through the selected profile and start a new PLL run, not Continue. "
                + "Preview uses the catalog's layout and variations with your seed; keep the same game settings and ascent, then visit the selected biome. "
                + "Cancel stops pending requests or captures, but cannot undo an island load that has started.", MessageType.Info);
        }

        internal static void AddToSpawner(PllNativeSpawnerAuthoring Source, string Name)
        {
            var Entries = Source is PllFoodSpawnerAuthoring Food ? Food.CustomItems : ((PllContentSpawnerAuthoring)Source).Content;
            foreach (var Entry in Entries) if (Entry.Name == Name) return;
            PllSourceIdentityValidator.ResolveEditableOwner(Source.gameObject, "Add Native Spawn Content");
            Undo.RecordObject(Source, "Add Native Spawn Content");
            var Updated = new PllNativeSpawnEntry[Entries.Length + 1]; Entries.CopyTo(Updated, 0);
            Updated[Entries.Length] = new PllNativeSpawnEntry { Name = Name, Weight = 1 };
            if (Source is PllFoodSpawnerAuthoring Target) { Target.UseCustomList = true; Target.CustomItems = Updated; }
            else ((PllContentSpawnerAuthoring)Source).Content = Updated;
            EditorUtility.SetDirty(Source);
        }

        void Queue(NativeContentRequestKind Kind)
        {
            if (ExpireMinutes < 1 || ExpireMinutes > 1440)
                throw new InvalidOperationException("The queue lifetime must be between 1 and 1440 minutes.");
            var Installation = PllCreatorInstallation.RequireReady();
            var Identity = new NativeContentIdentity
            {
                RuntimeSha256 = PllCreatorFileSystem.Sha256(Profile.RuntimePath),
                RuntimeMvid = Profile.RuntimeMvid.ToString("D"),
                PeakSha256 = PllCreatorFileSystem.Sha256(Installation.GetAssemblyPath("Assembly-CSharp")),
                UnityVersion = Application.unityVersion, BundleFile = Path.GetFileName(BundlePath),
                BundleSha256 = PllCreatorFileSystem.Sha256(BundlePath)
            };
            if (Identity.RuntimeSha256 != Profile.RuntimeSha256)
                throw new InvalidOperationException("The selected runtime changed. Inspect the profile again.");
            var Request = new NativeContentRequest
            {
                Schema = NativeContentArtifact.RequestSchema,
                Id = Guid.NewGuid().ToString("N"), Kind = Kind, Identity = Identity,
                ExpiresUtcTicks = DateTime.UtcNow.AddMinutes(ExpireMinutes).Ticks
            };
            if (Kind == NativeContentRequestKind.Preview)
            {
                if (!SameIdentity(Identity, Catalog.Identity))
                    throw new InvalidOperationException("The imported catalog does not match this installed runtime and bundle.");
                var Target = Catalog.Occurrences[TargetOccurrence];
                Request.Configuration = Catalog.Configuration; Request.SeedText = SeedText;
                Request.SlotIndex = Target.SlotIndex; Request.SourceId = Target.SourceId;
                Request.OccurrenceRequestIndex = Target.OccurrenceRequestIndex;
                Request.NativeRunSettingsSha256 = Catalog.NativeRunSettingsSha256;
                Request.Ascent = Catalog.Ascent;
            }
            NativeContentArtifact.RequireRequest(Request);
            var Directory = Path.Combine(Profile.Root, "BepInEx", "plugins", NativeContentArtifact.DirectoryName);
            System.IO.Directory.CreateDirectory(Directory);
            var Pending = Path.Combine(Directory, Request.Id + ".queue");
            var Destination = Path.Combine(Directory, NativeContentArtifact.RequestFile);
            var Json = NativeContentArtifact.Serialize(Request);
            if (PllCreatorFileSystem.StrictUtf8.GetByteCount(Json) > NativeContentArtifact.MaximumRequestBytes)
                throw new InvalidOperationException("The native content request exceeds 128 KiB.");
            var OwnsPending = false;
            try
            {
                using (var Stream = File.Open(Pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    OwnsPending = true;
                    using (var Writer = new StreamWriter(Stream, PllCreatorFileSystem.StrictUtf8))
                        Writer.Write(Json);
                }
                File.Move(Pending, Destination); OwnsPending = false;
            }
            finally { if (OwnsPending) File.Delete(Pending); }
            Queued = Request; ResultSummary = "The request is queued. Start PEAK to process it.";
        }

        void ReadResult()
        {
            var Path = System.IO.Path.Combine(Profile.Root, "BepInEx", "plugins",
                NativeContentArtifact.DirectoryName, RequestId, NativeContentArtifact.ResultFile);
            if (!File.Exists(Path)) { ResultSummary = "No result is available for this request yet."; return; }
            var Result = ReadJson<NativeContentResult>(Path, NativeContentArtifact.MaximumRequestBytes);
            NativeContentArtifact.RequireResult(Result);
            if (Result.RequestId != RequestId || !SameIdentity(Result.Identity, Queued.Identity))
                throw new InvalidOperationException("The result does not match the queued request.");
            var Summary = Result.Outcome + (Result.Failure == "" ? "" : ": " + Result.Failure);
            if (Result.Outcome == "Rendered")
            {
                if (Result.SeedText != Queued.SeedText || Result.SlotIndex != Queued.SlotIndex
                    || Result.OccurrenceRequestIndex != Queued.OccurrenceRequestIndex
                    || Result.SourceId != Queued.SourceId || Result.Ascent != Queued.Ascent
                    || Result.NativeRunSettingsSha256 != Queued.NativeRunSettingsSha256)
                    throw new InvalidOperationException("The rendered inputs differ from the queued preview.");
                var ImagePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), Result.ImageFile);
                if (PllCreatorFileSystem.Sha256(ImagePath) != Result.ImageSha256)
                    throw new InvalidOperationException("The preview pixels differ from the correlated result.");
                Summary += "; resolved seed " + Result.ResolvedSeed;
                EditorUtility.RevealInFinder(ImagePath);
            }
            else if (Result.Outcome == "Exported") EditorUtility.RevealInFinder(Path);
            ResultSummary = Summary;
        }

        void CancelQueue()
        {
            var Directory = Path.Combine(Profile.Root, "BepInEx", "plugins", NativeContentArtifact.DirectoryName);
            var QueuePath = Path.Combine(Directory, NativeContentArtifact.RequestFile);
            if (!File.Exists(QueuePath)) { ResultSummary = "No pending queue entry remains."; return; }
            try
            {
                using (var Input = NativeContentRequestFile.Open(QueuePath, true))
                {
                    var Request = NativeContentArtifact.Parse<NativeContentRequest>(Input.ReadText());
                    if (Request == null || Request.Schema != NativeContentArtifact.RequestSchema
                        || Request.Id != RequestId)
                        throw new InvalidOperationException("Another request owns this profile's queue.");
                    Input.RenameTo(Path.Combine(Directory, RequestId + ".cancelled.json"));
                }
                ResultSummary = "The queued request was archived without starting a PEAK run.";
            }
            catch (IOException Failure) when ((Failure.HResult & 0xffff) is 32 or 33)
            {
                using (File.Open(Path.Combine(Directory, RequestId + ".cancel"),
                           FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read)) { }
                ResultSummary = "Cancellation requested. An active capture must finish stopping.";
            }
        }

        internal static NativeContentCatalog ReadCatalog(string Path)
        {
            var Catalog = ReadJson<NativeContentCatalog>(Path, NativeContentArtifact.MaximumCatalogBytes);
            NativeContentArtifact.RequireCatalog(Catalog);
            return Catalog;
        }

        static void RequireRecordedRequest(NativeContentRequest Request)
        {
            if (Request == null || Request.Schema != NativeContentArtifact.RequestSchema
                || !Guid.TryParseExact(Request.Id, "N", out _)
                || !Enum.IsDefined(typeof(NativeContentRequestKind), Request.Kind))
                throw new InvalidOperationException("The recorded native content request is malformed.");
            NativeContentArtifact.RequireIdentity(Request.Identity);
        }

        static T ReadJson<T>(string Path, int MaximumBytes, FileShare Sharing = FileShare.Read)
        {
            using (var Stream = File.Open(Path, FileMode.Open, FileAccess.Read, Sharing))
            {
                if (Stream.Length > MaximumBytes)
                    throw new InvalidOperationException("The native content artifact exceeds its metadata size limit.");
                using (var Reader = new StreamReader(Stream, PllCreatorFileSystem.StrictUtf8, false))
                    return NativeContentArtifact.Parse<T>(Reader.ReadToEnd());
            }
        }

        static bool SameIdentity(NativeContentIdentity Left, NativeContentIdentity Right) =>
            Left != null && Right != null
            && Left.RuntimeSha256 == Right.RuntimeSha256 && Left.RuntimeMvid == Right.RuntimeMvid
            && Left.PeakSha256 == Right.PeakSha256 && Left.UnityVersion == Right.UnityVersion
            && Left.BundleFile == Right.BundleFile && Left.BundleSha256 == Right.BundleSha256;

        internal static Texture2D ReadThumbnail(string CatalogPath, NativeContentEntry Entry)
        {
            var Path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(CatalogPath), Entry.ImageFile);
            byte[] Bytes;
            using (var Stream = File.OpenRead(Path))
            {
                if (Stream.Length > 4 * 1024 * 1024)
                    throw new InvalidOperationException("The native thumbnail exceeds 4 MiB.");
                Bytes = new byte[Stream.Length];
                var Offset = 0;
                while (Offset < Bytes.Length)
                {
                    var Count = Stream.Read(Bytes, Offset, Bytes.Length - Offset);
                    if (Count == 0) throw new EndOfStreamException("The native thumbnail is incomplete.");
                    Offset += Count;
                }
            }
            using (var Sha = System.Security.Cryptography.SHA256.Create())
                if (NativeContentArtifact.Hex(Sha.ComputeHash(Bytes)) != Entry.ImageSha256)
                    throw new InvalidOperationException("The native thumbnail differs from its catalog identity.");
            if (Bytes.Length < 33 || Bytes[0] != 137 || Bytes[1] != 80 || Bytes[2] != 78 || Bytes[3] != 71
                || Bytes[4] != 13 || Bytes[5] != 10 || Bytes[6] != 26 || Bytes[7] != 10
                || Dimension(Bytes, 8) != 13 || Bytes[12] != 73 || Bytes[13] != 72
                || Bytes[14] != 68 || Bytes[15] != 82
                || Dimension(Bytes, 16) == 0 || Dimension(Bytes, 16) > 512
                || Dimension(Bytes, 20) == 0 || Dimension(Bytes, 20) > 512)
                throw new InvalidOperationException("The native thumbnail must be a PNG no larger than 512 by 512 pixels.");
            var Image = new Texture2D(2, 2);
            try
            {
                if (!Image.LoadImage(Bytes, true)) throw new InvalidOperationException("The native thumbnail PNG cannot be decoded.");
                return Image;
            }
            catch { DestroyImmediate(Image); throw; }
        }

        static uint Dimension(byte[] Bytes, int Offset) => (uint)Bytes[Offset] << 24
            | (uint)Bytes[Offset + 1] << 16 | (uint)Bytes[Offset + 2] << 8 | Bytes[Offset + 3];

        void ReleaseThumbnail()
        {
            if (Thumbnail) DestroyImmediate(Thumbnail);
            Thumbnail = null;
        }

        static void CreateItemCarrier(NativeContentEntry Entry)
        {
            var Installation = PllCreatorInstallation.RequireReady();
            Type ComponentType = null;
            if (typeof(Item).FullName == Entry.ComponentType
                && typeof(Item).Assembly.GetName().Name == Entry.ComponentAssembly) ComponentType = typeof(Item);
            else foreach (var Type in TypeCache.GetTypesDerivedFrom<Item>())
                if (Type.FullName == Entry.ComponentType && Type.Assembly.GetName().Name == Entry.ComponentAssembly)
                { ComponentType = Type; break; }
            if (ComponentType == null || ComponentType.IsAbstract
                || !typeof(Item).IsAssignableFrom(ComponentType))
                throw new InvalidOperationException("The generated providers do not support this item component type.");
            var Scripts = MonoImporter.GetAllRuntimeMonoScripts();
            MonoScript Provider = null;
            foreach (var Script in Scripts)
                if (Script.GetClass() == ComponentType) { Provider = Script; break; }
            if (!Provider || !Installation.CheckSerializedTypeCompatibility(
                    ComponentType.Assembly.GetName().Name, AssetDatabase.GetAssetPath(Provider), ComponentType))
                throw new InvalidOperationException("This item has no compatible generated component provider.");
            var Path = EditorUtility.SaveFilePanelInProject("Save item identity carrier",
                "NativeItem-" + Entry.ItemId, "prefab", "Select the identification-only prefab path.");
            if (Path == "") return;
            if (File.Exists(Path) || AssetDatabase.LoadMainAssetAtPath(Path))
                throw new InvalidOperationException("Choose a new prefab path; existing assets are not replaced.");
            var Carrier = new GameObject(Entry.Name);
            try
            {
                ((Item)Carrier.AddComponent(ComponentType)).itemID = checked((ushort)Entry.ItemId);
                var Saved = PrefabUtility.SaveAsPrefabAsset(Carrier, Path);
                if (!Saved) throw new InvalidOperationException("Unity could not save the item identity carrier.");
                Selection.activeObject = Saved; EditorGUIUtility.PingObject(Saved);
            }
            finally { DestroyImmediate(Carrier); }
        }

        static void ApplyVisual(NativeContentEntry Entry)
        {
            const string Action = "Add Native Visual Preset";
            if (!Enum.TryParse(Entry.Id, out PllNativeVisualPreset Preset)
                || Preset == PllNativeVisualPreset.None || !Enum.IsDefined(typeof(PllNativeVisualPreset), Preset))
                throw new InvalidOperationException("The captured native visual preset is unsupported.");
            var Target = Selection.activeGameObject;
            var Owner = PllAuthoringCommands.RequireFeatureTarget<PLLNativeVisualResolver>(Target, Action);
            if (!Owner.IsBiomeRoot || Target != Owner.StageRoot)
                throw new InvalidOperationException("Select the editable biome root for its native visual preset.");
            PllAuthoringCommands.Create(Action, () =>
            {
                var Resolver = Undo.AddComponent<PLLNativeVisualResolver>(Target);
                using (var Serialized = new SerializedObject(Resolver))
                {
                    PllAuthoringCommands.Property(Serialized, "preset").intValue = (int)Preset;
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                return Resolver;
            });
        }
    }
}
