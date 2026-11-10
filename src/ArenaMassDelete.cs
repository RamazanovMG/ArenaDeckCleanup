using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Wizards.Arena.Promises;
using Wizards.Mtga;
using Wizards.Mtga.Decks;
using Wotc.Mtga.Network.ServiceWrappers;

[BepInPlugin("local.maga.arena.massdelete", "Arena Deck Cleanup", "2.0.0")]
public sealed class ArenaMassDelete : BaseUnityPlugin
{
    static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArenaDeckCleanup");
    const int BatchSize = 4;
    DeckDataProvider provider;
    DeckManagerController deckScreen;
    GameObject blocker;
    Rect window = new Rect(100, 100, 860, 710);
    Vector2 scroll;
    string query = "", status = "Open the panel to load your personal decks.", runPath;
    bool visible, busy, confirming, uncertain, onDeckScreen, browsingBackups;
    class SavedDeck { public string Source; public Client_DeckSummary Summary; public Client_DeckContents Contents; }
    List<SavedDeck> savedDecks = new List<SavedDeck>();
    List<string> backupFiles = new List<string>();
    int backupIndex;
    string backupQuery = "";
    GUIStyle deckNameStyle, badgeStyle, detailStyle;
    float nextProbe;
    List<ManagedDeck> rows = new List<ManagedDeck>();
    HashSet<Guid> keep = new HashSet<Guid>();
    Dictionary<Guid, bool> preferences = new Dictionary<Guid, bool>();
    List<ManagedDeck> confirmedInventory, confirmedTargets;
    List<ManagedDeck> succeeded = new List<ManagedDeck>();
    GUIStyle label, heading, button, toggle, box, field;
    Texture2D panelTexture;

    void Awake()
    {
        Directory.CreateDirectory(Root);
        try
        {
            var path = Path.Combine(Root, "preferences.json");
            if (File.Exists(path)) preferences = JsonConvert.DeserializeObject<Dictionary<Guid, bool>>(File.ReadAllText(path)) ?? preferences;
        }
        catch (Exception ex) { Logger.LogWarning("Could not read keep preferences: " + ex.Message); }
        Logger.LogInfo("Ready. Open Decks, then Deck cleanup or F8. No automatic deletion.");
    }

    void Update()
    {
        if (Time.realtimeSinceStartup >= nextProbe)
        {
            nextProbe = Time.realtimeSinceStartup + 1;
            deckScreen = UnityEngine.Object.FindAnyObjectByType<DeckManagerController>();
            onDeckScreen = deckScreen != null && deckScreen.gameObject.activeInHierarchy;
            if (!onDeckScreen && visible && !busy) ClosePanel();
        }

    }

    void OnDestroy()
    {
        if (blocker != null) Destroy(blocker);
        if (panelTexture != null) Destroy(panelTexture);
    }

    void SetBlocker(bool active)
    {
        if (blocker == null && active)
        {
            blocker = new GameObject("ArenaDeckCleanupInputBlocker", typeof(Canvas), typeof(GraphicRaycaster));
            DontDestroyOnLoad(blocker);
            var canvas = blocker.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            var shade = new GameObject("Shade", typeof(RectTransform), typeof(Image));
            shade.transform.SetParent(blocker.transform, false);
            var rt = shade.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            shade.GetComponent<Image>().color = new Color(0, 0, 0, .6f);
            shade.GetComponent<Image>().raycastTarget = true;
        }
        if (blocker != null) blocker.SetActive(active);
    }

    float UiScale { get { return Mathf.Max(1f, Screen.height / 1080f); } }

    void OpenPanel()
    {
        visible = true; confirming = false; browsingBackups = false;
        window.width = Mathf.Min(860, Screen.width / UiScale - 30);
        window.height = Mathf.Min(710, Screen.height / UiScale - 40);
        window.x = (Screen.width / UiScale - window.width) / 2;
        window.y = (Screen.height / UiScale - window.height) / 2;
        SetBlocker(true);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        StartTask(LoadRows());
    }

    void ClosePanel()
    {
        if (busy) return;
        SavePreferences();
        visible = false; confirming = false; SetBlocker(false);
    }

    void Styles()
    {
        if (label != null) return;
        panelTexture = new Texture2D(1, 1);
        panelTexture.SetPixel(0, 0, new Color(.09f, .105f, .13f, 1)); panelTexture.Apply();
        label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = false };
        label.normal.textColor = new Color(.91f, .93f, .96f);
        heading = new GUIStyle(label) { fontSize = 22, fontStyle = FontStyle.Bold };
        button = new GUIStyle(GUI.skin.button) { fontSize = 15, fixedHeight = 34, richText = false };
        toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 16, fixedHeight = 29, richText = false };
        toggle.normal.textColor = Color.white;
        toggle.onNormal.textColor = new Color(.58f, 1f, .74f);
        box = new GUIStyle(GUI.skin.window) { padding = new RectOffset(20, 20, 18, 16) };
        box.normal.background = panelTexture; box.onNormal.background = panelTexture;
        field = new GUIStyle(GUI.skin.textField) { fontSize = 16, fixedHeight = 32 };
        deckNameStyle = new GUIStyle(label) { fontSize = 16, clipping = TextClipping.Clip, wordWrap = false };
        badgeStyle = new GUIStyle(label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        detailStyle = new GUIStyle(label) { fontSize = 12 };
    }

    void OnGUI()
    {
        if (!onDeckScreen && !visible) return;
        var previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * UiScale);
        try { DrawGui(); } finally { GUI.matrix = previousMatrix; }
    }

    void DrawGui()
    {
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F8 && !busy)
        {
            if (visible) ClosePanel(); else OpenPanel();
            Event.current.Use();
        }
        Styles();
        if (!visible)
        {
            if (GUI.Button(new Rect(Screen.width / UiScale - 225, 175, 200, 38), "Deck cleanup  [F8]", button)) OpenPanel();
            return;
        }
        window = GUI.Window(920174, window, DrawWindow, "", box);
    }

    void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(browsingBackups ? "Recover a backed-up deck" : confirming ? "Confirm deck cleanup" : "Choose decks to keep", heading);
        GUI.enabled = !busy;
        if (GUILayout.Button("Close", button, GUILayout.Width(80))) ClosePanel();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.Space(8);
        GUILayout.Label(status, label, GUILayout.Height(48));
        if (browsingBackups) DrawBackups(); else if (confirming) DrawConfirmation(); else DrawSelection();
        GUI.DragWindow(new Rect(0, 0, window.width - 110, 45));
    }

    void DrawSelection()
    {
        GUI.enabled = !busy;
        GUILayout.BeginHorizontal();
        GUILayout.Label("Search", label, GUILayout.Width(60));
        query = GUILayout.TextField(query, field);
        if (GUILayout.Button("Refresh", button, GUILayout.Width(95))) { SavePreferences(); StartTask(LoadRows()); }
        GUILayout.EndHorizontal();
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Keep all", button, GUILayout.Width(120))) keep = new HashSet<Guid>(rows.Select(d => d.Id));
        if (GUILayout.Button("Keep none", button, GUILayout.Width(120))) keep.Clear();
        if (GUILayout.Button("Backups", button, GUILayout.Width(110))) OpenBackups();
        GUILayout.FlexibleSpace();
        GUILayout.Label(keep.Count + " kept   /   " + (rows.Count - keep.Count) + " to delete", label);
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
        GUILayout.Label("Checked decks stay. Unchecked decks will be deleted.", label);
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        foreach (var row in rows.Where(d => string.IsNullOrWhiteSpace(query) || DisplayName(d.Name).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            bool wasKept = keep.Contains(row.Id);
            bool isKept = DrawDeckRow(row, wasKept);
            if (wasKept != isKept) { if (isKept) keep.Add(row.Id); else keep.Remove(row.Id); }
        }
        GUILayout.EndScrollView();
        GUILayout.Label("New decks start checked. Your choices are remembered. Each cleanup saves a backup.", label, GUILayout.Height(42));
        GUI.enabled = !busy && !uncertain && rows.Count > keep.Count;
        if (GUILayout.Button("Review deletion of " + (rows.Count - keep.Count) + " decks", button))
        {
            confirmedInventory = rows.Select(Clone).ToList();
            confirmedTargets = MassDeletePolicy.Targets(confirmedInventory, keep);
            SavePreferences(); confirming = true; scroll = Vector2.zero;
        }
        GUI.enabled = true;
    }

    static void Fill(Rect r, Color color)
    {
        var previous = GUI.color; GUI.color = color;
        GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = previous;
    }

    bool DrawDeckRow(ManagedDeck deck, bool selected)
    {
        Rect r = GUILayoutUtility.GetRect(0, 58, GUILayout.ExpandWidth(true));
        bool next = GUI.Toggle(r, selected, GUIContent.none, GUIStyle.none);
        Color accent = selected ? new Color(.35f, .9f, .65f) : new Color(1f, .61f, .54f);
        Fill(new Rect(r.x, r.y + 2, r.width, 54), selected ? new Color(.12f, .22f, .20f) : new Color(.23f, .15f, .16f));
        Fill(new Rect(r.x, r.y + 2, 4, 54), accent);
        Rect check = new Rect(r.x + 14, r.y + 14, 30, 30);
        Fill(check, accent);
        Fill(new Rect(check.x + 3, check.y + 3, 24, 24), selected ? accent : new Color(.09f, .105f, .13f));
        if (selected)
        {
            // Draw the check mark as geometry, independent of installed font glyphs.
            Color ink = new Color(.04f, .12f, .09f);
            for (int i = 0; i < 7; i++) Fill(new Rect(check.x + 5 + i, check.y + 13 + i, 4, 4), ink);
            for (int i = 0; i < 13; i++) Fill(new Rect(check.x + 11 + i, check.y + 19 - i, 4, 4), ink);
        }
        GUI.Label(new Rect(r.x + 57, r.y + 7, r.width - 153, 23), DisplayName(deck.Name), deckNameStyle);
        GUI.Label(new Rect(r.x + 57, r.y + 31, r.width - 153, 20), deck.Format ?? "", detailStyle);
        badgeStyle.normal.textColor = accent;
        GUI.Label(new Rect(r.xMax - 88, r.y + 12, 78, 32), selected ? "KEEP" : "DELETE", badgeStyle);
        return next;
    }

    void OpenBackups()
    {
        SavePreferences(); browsingBackups = true; scroll = Vector2.zero;
        savedDecks.Clear(); backupFiles.Clear(); backupIndex = 0; backupQuery = "";
        try
        {
            string directory = Path.Combine(Root, "backups");
            if (Directory.Exists(directory)) backupFiles.AddRange(Directory.GetFiles(directory, "decks.json", SearchOption.AllDirectories).OrderByDescending(p => p));
            string original = Path.Combine(Root, "legacy-backup.json");
            if (File.Exists(original)) backupFiles.Add(original);
            LoadBackup();
        }
        catch (Exception ex) { status = "Could not read backups: " + ex.Message; }
    }

    void LoadBackup()
    {
        savedDecks.Clear(); scroll = Vector2.zero;
        if (backupFiles.Count == 0) { status = "No saved decks yet. Backups are made before deletion."; return; }
        try
        {
            string path = backupFiles[backupIndex];
            {
                var entries = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(path));
                foreach (var entry in entries)
                {
                    var summary = entry["Summary"].ToObject<Client_DeckSummary>();
                    var contents = entry["Contents"].ToObject<Client_DeckContents>();
                    if (summary == null || contents == null || contents.Piles == null) continue;
                    savedDecks.Add(new SavedDeck { Source = path, Summary = summary, Contents = contents });
                }
            }
            status = "Copy a saved deck, close this panel, then click Import in Arena.";
        }
        catch (Exception ex) { status = "Could not read backups: " + ex.Message; }
    }

    void DrawBackups()
    {
        if (GUILayout.Button("Back to keep selection", button)) { browsingBackups = false; scroll = Vector2.zero; status = rows.Count + " decks loaded. Check the decks you want to keep."; }
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        GUI.enabled = backupIndex > 0;
        if (GUILayout.Button("Newer", button, GUILayout.Width(95))) { backupIndex--; LoadBackup(); }
        GUI.enabled = true;
        string date = "No backups";
        if (backupFiles.Count > 0)
        {
            date = File.GetLastWriteTime(backupFiles[backupIndex]).ToString("dd MMM yyyy, HH:mm");
            date += "   (" + (backupIndex + 1) + " / " + backupFiles.Count + ")";
        }
        GUILayout.Label(date, label);
        GUI.enabled = backupIndex + 1 < backupFiles.Count;
        if (GUILayout.Button("Older", button, GUILayout.Width(95))) { backupIndex++; LoadBackup(); }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Find a deck", label, GUILayout.Width(100));
        backupQuery = GUILayout.TextField(backupQuery, field);
        GUILayout.EndHorizontal();
        GUILayout.Label("Search this backup. Older backups stay saved; only this one is loaded.", detailStyle);
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        foreach (var saved in savedDecks.Where(d => string.IsNullOrWhiteSpace(backupQuery) || DisplayName(d.Summary.Name).IndexOf(backupQuery, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label(DisplayName(saved.Summary.Name), deckNameStyle);
            GUILayout.Label(saved.Summary.Format, detailStyle);
            GUILayout.EndVertical();
            if (GUILayout.Button("Copy for Import", button, GUILayout.Width(150))) CopyBackup(saved);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }
        GUILayout.EndScrollView();
        GUILayout.Label("Import creates a new deck. Cards are not crafted automatically. The original JSON backup stays intact.", label, GUILayout.Height(42));
    }

    static Type GameType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }
            var type = types.FirstOrDefault(t => t != null && (t.FullName == name || t.Name == name));
            if (type != null) return type;
        }
        throw new Exception("Arena type unavailable: " + name);
    }

    void CopyBackup(SavedDeck saved)
    {
        try
        {
            var deck = new Client_Deck(saved.Summary, saved.Contents);
            var convert = GameType("DeckServiceWrapperHelpers").GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "ToAzureModel" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Client_Deck));
            var model = convert.Invoke(null, new object[] { deck });
            var languages = GameType("Wotc.Mtga.Loc.Languages");
            var loc = languages.GetField("ActiveLocProvider", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var database = typeof(DeckManagerController).GetField("_cardDatabase", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(deckScreen);
            var export = GameType("WrapperDeckUtilities").GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "ToExportString" && m.GetParameters().Length == 3);
            var text = (string)export.Invoke(null, new object[] { model, loc, database });
            if (string.IsNullOrWhiteSpace(text)) throw new Exception("Arena returned an empty deck export.");
            string exports = Path.Combine(Root, "exports"); Directory.CreateDirectory(exports);
            File.WriteAllText(Path.Combine(exports, saved.Summary.DeckId + ".txt"), text, new UTF8Encoding(false));
            GUIUtility.systemCopyBuffer = text;
            status = "Copied " + DisplayName(saved.Summary.Name) + ". Close this panel and click Import. A text copy is also saved in ArenaDeckCleanup/exports.";
        }
        catch (Exception ex) { Logger.LogError(ex); status = "Export failed: " + (ex.InnerException ?? ex).Message; }
    }

    void DrawConfirmation()
    {
        int n = confirmedTargets == null ? 0 : confirmedTargets.Count;
        GUILayout.Label("Delete these " + n + " decks and keep " + (confirmedInventory.Count - n) + "?", heading);
        GUILayout.Label("Only the decks listed below will be removed. There is no automatic retry if the server response is uncertain.", label);
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        foreach (var row in confirmedTargets) GUILayout.Label("Delete: " + DisplayName(row.Name), label);
        GUILayout.EndScrollView();
        GUI.enabled = !busy;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Back to selection", button)) { confirming = false; scroll = Vector2.zero; }
        GUI.enabled = !busy && !uncertain && n > 0;
        if (GUILayout.Button("Delete " + n + " decks", button)) StartTask(DeleteSelected());
        GUILayout.EndHorizontal();
        GUI.enabled = true;
    }

    static ManagedDeck Clone(ManagedDeck d) { return new ManagedDeck { Id = d.Id, Name = d.Name, Format = d.Format, Updated = d.Updated }; }
    static string DisplayName(string name) { return Regex.Replace(name ?? "", "<[^>]*>", ""); }

    void SavePreferences()
    {
        foreach (var row in rows) preferences[row.Id] = keep.Contains(row.Id);
        try { WriteJson(Path.Combine(Root, "preferences.json"), preferences); }
        catch (Exception ex) { Logger.LogWarning("Preference save failed: " + ex.Message); }
    }

    void WriteJson(string path, object value)
    {
        string json = JsonConvert.SerializeObject(value, Formatting.Indented);
        File.WriteAllText(path + ".tmp", json, new UTF8Encoding(false));
        if (File.Exists(path)) File.Delete(path);
        File.Move(path + ".tmp", path);
    }

    void SetStatus(string value) { status = value; Logger.LogInfo(value); }
    void StartTask(IEnumerator task)
    {
        if (busy) return;
        busy = true; StartCoroutine(Guarded(task));
    }

    IEnumerator Guarded(IEnumerator task)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(task);
        while (stack.Count > 0)
        {
            object next = null;
            try
            {
                var current = stack.Peek();
                if (!current.MoveNext()) { stack.Pop(); continue; }
                next = current.Current;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                status = ex.Message + (uncertain ? " Restart Arena before another deletion." : " Refresh and review the selection again.");
                if (runPath != null)
                {
                    try { WriteJson(Path.Combine(runPath, "stopped.json"), new { Error = ex.ToString(), Deleted = succeeded, OutcomeUncertain = uncertain }); } catch { }
                }
                confirming = false; busy = false; yield break;
            }
            if (next is IEnumerator) stack.Push((IEnumerator)next); else yield return next;
        }
        busy = false;
    }

    IEnumerator Await<T>(Promise<T> promise, string operation)
    {
        if (promise == null) throw new Exception(operation + ": missing response.");
        float deadline = Time.realtimeSinceStartup + 45;
        while (!promise.IsDone)
        {
            if (Time.realtimeSinceStartup > deadline) throw new Exception(operation + ": request timed out.");
            yield return null;
        }
        if (!promise.Successful) throw new Exception(operation + ": server request failed.");
    }

    List<ManagedDeck> Snapshot()
    {
        if (provider == null || !object.ReferenceEquals(provider, Pantry.GetWithoutCreate<DeckDataProvider>(true)))
            throw new Exception("Arena session changed.");
        var cached = provider.GetCachedDecks();
        if (cached == null || cached.Any(d => d == null || d.Summary == null)) throw new Exception("Deck data is incomplete.");
        var list = cached.Where(d => !d.Summary.IsNetDeck).Select(d => new ManagedDeck {
            Id = d.Id, Name = d.Summary.Name, Format = d.Summary.Format, Updated = d.Summary.LastUpdated
        }).OrderBy(d => DisplayName(d.Name), StringComparer.OrdinalIgnoreCase).ToList();
        MassDeletePolicy.Validate(list); return list;
    }

    IEnumerator RefreshServer()
    {
        var request = provider.ForceRefreshCachedDecks();
        yield return Await(request, "Refresh decks");
    }

    IEnumerator LoadRows()
    {
        runPath = null;
        SetStatus("Loading your personal decks...");
        provider = Pantry.GetWithoutCreate<DeckDataProvider>(true);
        if (provider == null || !provider.Initialized) throw new Exception("Arena deck data is not ready yet.");
        yield return RefreshServer();
        rows = Snapshot(); keep = MassDeletePolicy.InitialKeep(rows, preferences);
        SetStatus(uncertain ? "Previous outcome was uncertain. Restart Arena before deleting again." : rows.Count + " decks loaded. Check the decks you want to keep.");
    }

    IEnumerator DeleteSelected()
    {
        var initial = confirmedInventory.Select(Clone).ToList();
        var targets = confirmedTargets.Select(Clone).ToList();
        if (targets.Count == 0 || uncertain) throw new Exception("No valid deletion request.");
        var protect = new HashSet<Guid>(initial.Where(d => !targets.Any(t => t.Id == d.Id)).Select(d => d.Id));
        MassDeletePolicy.Targets(initial, protect);
        yield return RefreshServer();
        if (!MassDeletePolicy.Same(initial, Snapshot())) throw new Exception("The deck list changed since confirmation. Nothing was deleted.");
        runPath = Path.Combine(Root, "backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(runPath); succeeded = new List<ManagedDeck>();
        WriteJson(Path.Combine(runPath, "plan.json"), new { Inventory = initial, Keep = protect, Delete = targets });
        var service = (IDeckServiceWrapper)typeof(DeckDataProvider).GetField("_deckServiceWrapper", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(provider);
        var backups = new List<object>();
        for (int offset = 0; offset < targets.Count; offset += BatchSize)
        {
            var batch = targets.Skip(offset).Take(BatchSize).ToList();
            var requests = batch.Select(d => service.LoadDeckContents(d.Id)).ToList();
            for (int i = 0; i < batch.Count; i++)
            {
                SetStatus("Backing up " + (offset + i + 1) + " / " + targets.Count + "...");
                yield return Await(requests[i], "Back up " + batch[i].Name);
                var deck = provider.GetDeckForId(batch[i].Id);
                var contents = requests[i].Result;
                if (deck == null || contents == null || deck.Summary.Name != batch[i].Name || deck.Summary.LastUpdated != batch[i].Updated)
                    throw new Exception("A deck changed while preparing its backup. Nothing was deleted.");
                var json = JsonConvert.SerializeObject(contents);
                var roundtrip = JsonConvert.DeserializeObject<Client_DeckContents>(json);
                if (roundtrip == null || roundtrip.Piles == null || JsonConvert.SerializeObject(roundtrip.Piles) != JsonConvert.SerializeObject(contents.Piles))
                    throw new Exception("Backup validation failed. Nothing was deleted.");
                backups.Add(new { Id = deck.Id, Name = deck.Summary.Name, Summary = deck.Summary, Contents = contents });
            }
        }
        WriteJson(Path.Combine(runPath, "decks.json"), backups);
        // Revalidate after all backups, before any mutation.
        yield return RefreshServer();
        if (!MassDeletePolicy.Same(initial, Snapshot())) throw new Exception("The deck list changed during backup. Nothing was deleted.");
        var expected = initial.Select(Clone).ToList();
        for (int offset = 0; offset < targets.Count; offset += BatchSize)
        {
            if (!MassDeletePolicy.Same(expected, Snapshot())) throw new Exception("The deck list changed during deletion; stopped.");
            var batch = targets.Skip(offset).Take(BatchSize).ToList();
            var pending = new List<KeyValuePair<ManagedDeck, Promise<bool>>>();
            SetStatus("Deleting " + (offset + 1) + "-" + (offset + batch.Count) + " / " + targets.Count + "...");
            // Sticky until this complete batch is accounted for. Exceptions cannot silently retry it.
            uncertain = true;
            foreach (var deck in batch)
            {
                if (protect.Contains(deck.Id)) throw new Exception("Protected deck appeared in deletion batch.");
                pending.Add(new KeyValuePair<ManagedDeck, Promise<bool>>(deck, provider.DeleteDeck(deck.Id)));
            }
            float deadline = Time.realtimeSinceStartup + 45;
            while (pending.Any(p => p.Value == null || !p.Value.IsDone) && Time.realtimeSinceStartup < deadline) yield return null;
            bool allKnown = true, allSucceeded = true;
            foreach (var p in pending)
            {
                if (p.Value == null || !p.Value.IsDone) { allKnown = false; allSucceeded = false; continue; }
                if (p.Value.Successful && p.Value.Result)
                { succeeded.Add(p.Key); expected.RemoveAll(d => d.Id == p.Key.Id); }
                else { allSucceeded = false; if (!p.Value.Successful) allKnown = false; }
            }
            WriteJson(Path.Combine(runPath, "deleted.json"), succeeded);
            uncertain = !allKnown;
            if (!allSucceeded) throw new Exception(allKnown ? "Some deletions were rejected. Confirmed deletions: " + succeeded.Count : "Deletion response timed out. Confirmed deletions: " + succeeded.Count);
        }
        yield return RefreshServer();
        var final = Snapshot();
        if (!MassDeletePolicy.Same(expected, final)) throw new Exception("Final verification differs from the expected kept decks.");
        WriteJson(Path.Combine(runPath, "completed.json"), new { Time = DateTime.UtcNow, Deleted = succeeded, Kept = final });
        foreach (var d in succeeded) preferences.Remove(d.Id);
        rows = final; keep = new HashSet<Guid>(rows.Select(d => d.Id)); SavePreferences();
        confirming = false; scroll = Vector2.zero;
        SetStatus("Done. Deleted " + succeeded.Count + " decks; kept " + final.Count + ". Backup saved in your local ArenaDeckCleanup folder.");
        // Refresh Arena's visible grid as well as its server-side cache.
        if (deckScreen != null)
        {
            var reload = typeof(DeckManagerController).GetMethod("Coroutine_LoadDecks", BindingFlags.Instance | BindingFlags.NonPublic);
            var bucket = typeof(DeckManagerController).GetField("_selectedBucket", BindingFlags.Instance | BindingFlags.NonPublic);
            if (reload != null && bucket != null)
                deckScreen.StartCoroutine((IEnumerator)reload.Invoke(deckScreen, new object[] { bucket.GetValue(deckScreen), null }));
        }
    }
}



