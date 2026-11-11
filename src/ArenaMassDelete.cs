using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Wizards.Arena.Promises;
using Wizards.Mtga;
using Wizards.Mtga.Decks;
using Wotc.Mtga.Network.ServiceWrappers;

[BepInPlugin("local.maga.arena.massdelete", "Arena Deck Cleanup", "1.0.0")]
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
    class SavedDeck { public string Source; public Client_DeckSummary Summary; public Client_DeckContents Contents; public DateTime Date; public string Colors; }
    List<SavedDeck> savedDecks = new List<SavedDeck>();
    List<string> backupFiles = new List<string>();
    RectTransform trashAnchor;
    readonly Filter mainFilter = new Filter(), backupFilter = new Filter();
    readonly Dictionary<Guid, string> colors = new Dictionary<Guid, string>();
    int backupPage;
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
    readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
    Filter calendarFilter;
    bool calendarEnd;
    DateTime calendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    bool hasError, loadingPage;
    float suppressEscapeUntil = -1;
    static ArenaMassDelete instance;
    Harmony inputPatch;
    readonly HashSet<SavedDeck> restoreSelection = new HashSet<SavedDeck>();
    readonly HashSet<SavedDeck> restoredThisSession = new HashSet<SavedDeck>();
    bool restoreUncertain;
    static bool EscapePrefix(InputAction.CallbackContext context) {
        var plugin = instance;
        if (plugin == null || (!plugin.visible && Time.realtimeSinceStartup > plugin.suppressEscapeUntil)) return true;
        plugin.suppressEscapeUntil = Time.realtimeSinceStartup + .35f;
        if (context.performed && plugin.visible) plugin.ClosePanel();
        return false;
    }
    static bool SettingsBackPrefix(Core.Code.Input.ActionContext __0) {
        var plugin = instance;
        if (plugin == null || (!plugin.visible && Time.realtimeSinceStartup > plugin.suppressEscapeUntil)) return true;
        __0.Used = true;
        plugin.suppressEscapeUntil = Time.realtimeSinceStartup + .35f;
        if (plugin.visible) plugin.ClosePanel();
        plugin.Logger.LogInfo("Consumed Arena Back for the cleanup overlay.");
        return false;
    }
    static bool BlockSettingsOpen() {
        var plugin = instance;
        if (plugin == null || (!plugin.visible && Time.realtimeSinceStartup > plugin.suppressEscapeUntil)) return false;
        if (plugin.visible) {
            plugin.suppressEscapeUntil = Time.realtimeSinceStartup + .35f;
            plugin.ClosePanel();
        }
        return true;
    }
    static bool SettingsOpenPrefix(ref System.Threading.Tasks.Task __result) {
        if (!BlockSettingsOpen()) return true;
        __result = System.Threading.Tasks.Task.FromResult(0);
        return false;
    }
    static bool SettingsMenuOpenPrefix() { return !BlockSettingsOpen(); }
    Texture2D Icon(string name) {
        Texture2D value;
        if (icons.TryGetValue(name, out value)) return value;
        using (var stream = GetType().Assembly.GetManifestResourceStream("ArenaDeckCleanup." + name + ".png")) {
            if (stream == null) return Texture2D.whiteTexture;
            var bytes = new byte[stream.Length]; stream.Read(bytes, 0, bytes.Length);
            value = new Texture2D(2, 2); ImageConversion.LoadImage(value, bytes);
            icons[name] = value; return value;
        }
    }
    void Mana(Rect rect, string value) {
        int index = 0;
        foreach (char c in "WUBRGC") if ((value ?? "").Contains(c.ToString())) {
            GUI.DrawTexture(new Rect(rect.x + index * 24, rect.y, 21, 21), Icon(c.ToString()), ScaleMode.ScaleToFit);
            index++;
        }
    }

    bool navigationReady, storageReady;
    void Awake() { instance = this; SceneManager.sceneLoaded += OnSceneLoaded; }
    void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        if (scene.name == "MainNavigation") {
            navigationReady = true;
            if (inputPatch == null) {
                inputPatch = new Harmony("local.maga.arena.massdelete.input");
                inputPatch.Patch(AccessTools.Method(GameType("Core.Code.Input.NewInputHandler"), "OnEscape"),
                    prefix: new HarmonyMethod(typeof(ArenaMassDelete), "EscapePrefix"));
                inputPatch.Patch(AccessTools.Method(GameType("SettingsMenuHost"), "OnBack"),
                    prefix: new HarmonyMethod(typeof(ArenaMassDelete), "SettingsBackPrefix"));
                inputPatch.Patch(AccessTools.Method(GameType("SettingsMenuHost"), "Open"),
                    prefix: new HarmonyMethod(typeof(ArenaMassDelete), "SettingsOpenPrefix"));
                inputPatch.Patch(AccessTools.Method(GameType("SettingsMenu"), "Open"),
                    prefix: new HarmonyMethod(typeof(ArenaMassDelete), "SettingsMenuOpenPrefix"));
            }
        }
        if (scene.name == "Bootstrap") navigationReady = false;
    }
    void InitializeStorage()
    {
        if (storageReady) return;
        storageReady = true;
        Directory.CreateDirectory(Root);

        try
        {
            var path = Path.Combine(Root, "preferences.json");
            if (File.Exists(path)) preferences = JsonConvert.DeserializeObject<Dictionary<Guid, bool>>(File.ReadAllText(path)) ?? preferences;
        }
        catch (Exception ex) { Logger.LogWarning("Could not read keep preferences: " + ex.Message); }
        Logger.LogInfo("Ready. Open Decks, then Bulk delete. No automatic deletion.");
    }

    void Update()
    {
        if (!navigationReady) return;
        if (Time.realtimeSinceStartup >= nextProbe)
        {
            nextProbe = Time.realtimeSinceStartup + 1;
            deckScreen = UnityEngine.Object.FindAnyObjectByType<DeckManagerController>();
            onDeckScreen = deckScreen != null && deckScreen.gameObject.activeInHierarchy;
            if (!onDeckScreen && visible && !busy) ClosePanel();
            trashAnchor = null;
            if (onDeckScreen) {
                var member = typeof(DeckManagerController).GetField("_deleteDeckButton", BindingFlags.Instance | BindingFlags.NonPublic);
                var component = member == null ? null : member.GetValue(deckScreen) as Component;
                if (component != null) trashAnchor = component.transform as RectTransform;
            }
        }

    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (inputPatch != null) inputPatch.UnpatchSelf();
        if (instance == this) instance = null;
        if (blocker != null) Destroy(blocker);
        if (panelTexture != null) Destroy(panelTexture);
        foreach (var icon in icons.Values) Destroy(icon);
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
        if (busy) { visible = true; SetBlocker(true); return; }
        hasError = false; calendarFilter = null; loadingPage = true; scroll = Vector2.zero;
        status = "Loading...";
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
        calendarFilter = null;
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
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape && visible)
        {
            suppressEscapeUntil = Time.realtimeSinceStartup + .35f; ClosePanel();
            Event.current.Use();
        }
        Styles();
        if (!visible)
        {
            if (trashAnchor != null) {
                var corners = new Vector3[4]; trashAnchor.GetWorldCorners(corners);
                var canvas = trashAnchor.GetComponentInParent<Canvas>();
                var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                var right = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                var bottom = RectTransformUtility.WorldToScreenPoint(camera, corners[3]);
                float x = Mathf.Min(right.x / UiScale + 12, Screen.width / UiScale - 152);
                float y = (Screen.height - (right.y + bottom.y) / 2) / UiScale - 34;
                if (GUI.Button(new Rect(x, y, 68, 68), new GUIContent(Icon("bulk-delete"), "Bulk delete"), GUIStyle.none)) OpenPanel();
                if (GUI.Button(new Rect(x + 76, y, 68, 68), new GUIContent(Icon("archive"), "Backups"), GUIStyle.none)) StartTask(OpenArchive());
            }
            return;
        }
        window = GUI.Window(920174, window, DrawWindow, "", box);
    }

    void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(browsingBackups ? "Recover a backed-up deck" : confirming ? "Confirm deck cleanup" : "Choose decks to keep", heading);
        GUI.enabled = true;
        if (GUILayout.Button("Close", button, GUILayout.Width(80))) ClosePanel();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.Space(8);
        if (hasError || busy) GUILayout.Label(status, label);
        if (loadingPage) { GUILayout.FlexibleSpace(); return; }
        if (browsingBackups) DrawBackups(); else if (confirming) DrawConfirmation(); else DrawSelection();
        GUI.DragWindow(new Rect(0, 0, window.width - 110, 45));
    }

    void DrawSelection()
    {
        GUI.enabled = !busy;
        GUILayout.BeginHorizontal();
        GUILayout.Label("Name", label, GUILayout.Width(60));
        query = GUILayout.TextField(query, field);
        if (GUILayout.Button("Refresh", button, GUILayout.Width(95))) { SavePreferences(); StartTask(LoadRows(true)); }
        GUILayout.EndHorizontal();
        DrawFilters(mainFilter, "Updated");
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Keep all", button, GUILayout.Width(120))) keep = new HashSet<Guid>(rows.Select(d => d.Id));
        if (GUILayout.Button("Keep none", button, GUILayout.Width(120))) keep.Clear();
        if (GUILayout.Button("Backups", button, GUILayout.Width(110))) OpenBackups();
        GUILayout.FlexibleSpace();
        GUILayout.Label(keep.Count + " kept   /   " + (rows.Count - keep.Count) + " to delete", label);
        GUILayout.EndHorizontal();
        GUILayout.Space(6);

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        foreach (var row in rows.Where(d => Matches(d.Name, d.Updated, colors.ContainsKey(d.Id) ? colors[d.Id] : "?", query, mainFilter)))
        {
            bool wasKept = keep.Contains(row.Id);
            bool isKept = DrawDeckRow(row, wasKept);
            if (wasKept != isKept) { if (isKept) keep.Add(row.Id); else keep.Remove(row.Id); }
        }
        GUILayout.EndScrollView();

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
        string identity = colors.ContainsKey(deck.Id) ? colors[deck.Id] : "?";
        float iconWidth = identity == "?" ? 0 : identity.Length * 24;
        var nameRect = new Rect(r.x + 57, r.y + 7, r.width - 153 - iconWidth, 23);
        GUI.Label(nameRect, DisplayName(deck.Name), deckNameStyle);
        float textWidth = Mathf.Min(nameRect.width, deckNameStyle.CalcSize(new GUIContent(DisplayName(deck.Name))).x);
        Mana(new Rect(nameRect.x + textWidth + 8, nameRect.y, iconWidth, 21), identity);
        GUI.Label(new Rect(r.x + 57, r.y + 31, r.width - 153, 20), deck.Format ?? "", detailStyle);
        badgeStyle.normal.textColor = accent;
        GUI.Label(new Rect(r.xMax - 88, r.y + 12, 78, 32), selected ? "KEEP" : "DELETE", badgeStyle);
        return next;
    }

    IEnumerator OpenArchive()
    {
        if (visible) yield break;
        visible = true; confirming = false; browsingBackups = true; hasError = false; calendarFilter = null; loadingPage = true; status = "Loading...";
        window.width = Mathf.Min(860, Screen.width / UiScale - 30);
        window.height = Mathf.Min(710, Screen.height / UiScale - 40);
        window.x = (Screen.width / UiScale - window.width) / 2;
        window.y = (Screen.height / UiScale - window.height) / 2;
        SetBlocker(true);
        InitializeStorage();
        provider = Pantry.GetWithoutCreate<DeckDataProvider>(true);
        yield return null;
        OpenBackups();
        loadingPage = false;
    }

    void OpenBackups()
    {
        calendarFilter = null; hasError = false; SavePreferences(); browsingBackups = true; scroll = Vector2.zero;
        restoreSelection.Clear(); restoredThisSession.Clear(); savedDecks.Clear(); backupFiles.Clear(); backupPage = 0; backupQuery = "";
        try
        {

            string directory = Path.Combine(Root, "backups");
            if (Directory.Exists(directory)) backupFiles.AddRange(Directory.GetFiles(directory, "decks.json", SearchOption.AllDirectories).OrderByDescending(p => p));
            string original = Path.Combine(Root, "legacy-backup.json");
            if (File.Exists(original)) backupFiles.Add(original);
            LoadBackup();
        }
        catch (Exception ex) { hasError = true; status = "Could not read backups: " + ex.Message; }
    }

    void LoadBackup()
    {
        savedDecks.Clear(); scroll = Vector2.zero;
        if (backupFiles.Count == 0) { status = "No saved decks yet. Backups are made before deletion."; return; }
        try
        {
            foreach (string path in backupFiles)
            {
                var entries = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(path));
                foreach (var entry in entries)
                {
                    var summary = entry["Summary"].ToObject<Client_DeckSummary>();
                    var contents = entry["Contents"].ToObject<Client_DeckContents>();
                    if (summary == null || contents == null || contents.Piles == null) continue;
                    savedDecks.Add(new SavedDeck { Source = path, Summary = summary, Contents = contents,
                        Date = entry["BackedUpAtUtc"] == null ? File.GetLastWriteTime(path) : entry["BackedUpAtUtc"].ToObject<DateTime>().ToLocalTime(), Colors = DeckColors(contents) });
                }
            }
            status = "Copy a saved deck, close this panel, then click Import in Arena.";
        }
        catch (Exception ex) { hasError = true; status = "Could not read backups: " + ex.Message; }
    }

    void DrawBackups()
    {
        GUI.enabled = !busy;
        if (GUILayout.Button("Back to keep selection", button)) { calendarFilter = null; browsingBackups = false; scroll = Vector2.zero; loadingPage = true; status = "Loading..."; StartTask(LoadRows()); }
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Name", label, GUILayout.Width(60));
        backupQuery = GUILayout.TextField(backupQuery, field);
        GUILayout.EndHorizontal();
        DrawFilters(backupFilter, "Backed up");
        var matches = savedDecks.Where(d => Matches(d.Summary.Name, d.Date, d.Colors, backupQuery, backupFilter)).OrderByDescending(d => d.Date).ToList();
        int pages = Math.Max(1, (matches.Count + 29) / 30);
        backupPage = Math.Min(backupPage, pages - 1);
        GUILayout.BeginHorizontal();
        GUI.enabled = backupPage > 0;
        if (GUILayout.Button("Previous", button, GUILayout.Width(95))) { backupPage--; scroll = Vector2.zero; }
        GUI.enabled = true;
        GUILayout.Label(matches.Count + " saved  /  page " + (backupPage + 1) + " of " + pages, label);
        GUI.enabled = backupPage + 1 < pages;
        if (GUILayout.Button("Next", button, GUILayout.Width(95))) { backupPage++; scroll = Vector2.zero; }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUI.enabled = !busy && !restoreUncertain;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Select shown", button)) foreach (var d in matches) if (!restoredThisSession.Contains(d)) restoreSelection.Add(d);
        if (GUILayout.Button("Clear selection", button)) restoreSelection.Clear();
        GUILayout.EndHorizontal();
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        foreach (var saved in matches.Skip(backupPage * 30).Take(30)) {
            bool done = restoredThisSession.Contains(saved);
            GUI.enabled = !busy && !restoreUncertain && !done;
            Rect r = GUILayoutUtility.GetRect(0, 58, GUILayout.ExpandWidth(true));
            bool selected = restoreSelection.Contains(saved);
            bool next = GUI.Toggle(r, selected, GUIContent.none, GUIStyle.none);
            if (next) restoreSelection.Add(saved); else restoreSelection.Remove(saved);
            Color accent = new Color(.35f, .9f, .65f);
            Fill(new Rect(r.x, r.y + 2, r.width, 54), selected ? new Color(.12f, .22f, .20f) : new Color(.12f, .13f, .16f));
            Rect check = new Rect(r.x + 14, r.y + 14, 30, 30);
            Fill(check, selected ? accent : new Color(.45f,.47f,.51f));
            Fill(new Rect(check.x+3,check.y+3,24,24), selected ? accent : new Color(.09f,.105f,.13f));
            if (selected) {
                for(int i=0;i<7;i++) Fill(new Rect(check.x+5+i,check.y+13+i,4,4),Color.black);
                for(int i=0;i<13;i++) Fill(new Rect(check.x+11+i,check.y+19-i,4,4),Color.black);
            }
            float iconWidth = (saved.Colors ?? "C").Length * 24;
            Rect nr = new Rect(r.x+57,r.y+7,r.width-155-iconWidth,23);
            GUI.Label(nr,DisplayName(saved.Summary.Name),deckNameStyle);
            float tw = Mathf.Min(nr.width,deckNameStyle.CalcSize(new GUIContent(DisplayName(saved.Summary.Name))).x);
            Mana(new Rect(nr.x+tw+8,nr.y,iconWidth,21),saved.Colors);
            GUI.Label(new Rect(nr.x,r.y+31,r.width-155,20),saved.Date.ToString("dd MMM yyyy")+"  /  "+saved.Summary.Format,detailStyle);
            if(done) GUI.Label(new Rect(r.xMax-85,r.y+15,80,25),"Restored",detailStyle);
        }
        GUILayout.EndScrollView();
        GUI.enabled = !busy && !restoreUncertain && restoreSelection.Count > 0;
        if (GUILayout.Button("Restore selected (" + restoreSelection.Count + ")", button))
            StartTask(RestoreSelected(restoreSelection.ToList()));
        GUI.enabled = true;
    }

    IEnumerator RestoreSelected(List<SavedDeck> selected) {
        runPath = null;
        if (restoreUncertain || selected.Count == 0) yield break;
        provider = Pantry.GetWithoutCreate<DeckDataProvider>(true);
        if (provider == null || !provider.Initialized) throw new Exception("Arena deck data is not ready.");
        yield return RefreshServer();
        var manager = (DecksManager)typeof(DeckManagerController).GetField("_decksManager", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(deckScreen);
        int limit = manager.GetDeckLimit();
        if (Snapshot().Count + selected.Count > limit)
            throw new Exception("Not enough room. Select at most " + Math.Max(0,limit-Snapshot().Count) + " decks.");
        int count = 0;
        foreach (var saved in selected) {
            SetStatus("Restoring " + (count+1) + " / " + selected.Count + "...");
            var summary = JsonConvert.DeserializeObject<Client_DeckSummary>(JsonConvert.SerializeObject(saved.Summary));
            var contents = JsonConvert.DeserializeObject<Client_DeckContents>(JsonConvert.SerializeObject(saved.Contents));
            summary.DeckId = Guid.NewGuid(); summary.IsNetDeck = false; summary.LastUpdated = DateTime.UtcNow;
            // A fresh ID restores a copy without changing the saved backup or an existing deck.
            restoreUncertain = true;
            var request = provider.CreateDeck(new Client_Deck(summary,contents), "Import");
            yield return Await(request,"Restore " + DisplayName(summary.Name));
            restoreUncertain = false;
            restoredThisSession.Add(saved); restoreSelection.Remove(saved);
            preferences[summary.DeckId] = true;
            count++;
        }
        yield return RefreshServer();
        rows = Snapshot(); keep = MassDeletePolicy.InitialKeep(rows, preferences);
        colors.Clear(); foreach(var row in rows) colors[row.Id] = DeckColors(provider.GetDeckForId(row.Id).Contents);
        SavePreferences();
        SetStatus("Restored " + count + " decks.");
        if(deckScreen != null) {
            var reload = typeof(DeckManagerController).GetMethod("Coroutine_LoadDecks",BindingFlags.Instance|BindingFlags.NonPublic);
            var bucket = typeof(DeckManagerController).GetField("_selectedBucket",BindingFlags.Instance|BindingFlags.NonPublic);
            if(reload != null && bucket != null) deckScreen.StartCoroutine((IEnumerator)reload.Invoke(deckScreen,new object[]{bucket.GetValue(deckScreen),null}));
        }
    }

    sealed class Filter { public string From = "", To = "", Colors = ""; }
    void DrawFilters(Filter filter, string dateLabel)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(dateLabel, detailStyle, GUILayout.Width(70));
        if (GUILayout.Button(DateLabel(filter.From, "From"), button, GUILayout.Width(125))) OpenCalendar(filter, false);
        if (GUILayout.Button(DateLabel(filter.To, "Until"), button, GUILayout.Width(125))) OpenCalendar(filter, true);
        GUILayout.FlexibleSpace();
        foreach (char c in "WUBRGC") {
            bool selected = filter.Colors.Contains(c.ToString());
            Rect r = GUILayoutUtility.GetRect(36, 36, GUILayout.Width(36));
            if (selected) Fill(new Rect(r.x, r.y, 36, 36), new Color(.78f, .66f, .38f));
            Fill(new Rect(r.x + 2, r.y + 2, 32, 32), new Color(.09f,.105f,.13f));
            var tint = GUI.color; GUI.color = new Color(1,1,1,selected ? 1 : .65f);
            if (GUI.Button(new Rect(r.x+4,r.y+4,28,28), new GUIContent(Icon(c.ToString())), GUIStyle.none)) {
                if (c == 'C') filter.Colors = selected ? "" : "C";
                else filter.Colors = selected ? filter.Colors.Replace(c.ToString(), "") : filter.Colors.Replace("C", "") + c;
                backupPage = 0;
            }
            GUI.color = tint;
        }
        if (GUILayout.Button("Clear", button, GUILayout.Width(58))) { filter.From = filter.To = filter.Colors = ""; calendarFilter = null; backupPage = 0; }
        GUILayout.EndHorizontal();
        if (calendarFilter == filter) DrawCalendar();
    }
    static string DateLabel(string value, string empty) { DateTime d; return ParseDate(value, out d) ? d.ToString("dd MMM yyyy") : empty; }
    void OpenCalendar(Filter filter, bool end) {
        if (calendarFilter == filter && calendarEnd == end) { calendarFilter = null; return; }
        calendarFilter = filter; calendarEnd = end;
        DateTime d; if (!ParseDate(end ? filter.To : filter.From, out d)) d = DateTime.Today;
        calendarMonth = new DateTime(d.Year, d.Month, 1);
    }
    void DrawCalendar() {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("<<", button, GUILayout.Width(42))) calendarMonth = calendarMonth.AddYears(-1);
        if (GUILayout.Button("<", button, GUILayout.Width(42))) calendarMonth = calendarMonth.AddMonths(-1);
        GUILayout.Label(calendarMonth.ToString("MMMM yyyy"), heading);
        if (GUILayout.Button(">", button, GUILayout.Width(42))) calendarMonth = calendarMonth.AddMonths(1);
        if (GUILayout.Button(">>", button, GUILayout.Width(42))) calendarMonth = calendarMonth.AddYears(1);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        foreach (string day in new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" }) GUILayout.Label(day, detailStyle, GUILayout.Width((window.width - 65) / 7));
        GUILayout.EndHorizontal();
        int offset = ((int)calendarMonth.DayOfWeek + 6) % 7;
        int days = DateTime.DaysInMonth(calendarMonth.Year, calendarMonth.Month);
        DateTime? picked = null;
        for (int week = 0; week < (offset + days + 6) / 7; week++) {
            GUILayout.BeginHorizontal();
            for (int col = 0; col < 7; col++) {
                int day = week * 7 + col - offset + 1;
                if (day < 1 || day > days) GUILayout.Space((window.width - 65) / 7 + 4);
                else {
                    var d = calendarMonth.AddDays(day - 1);
                    var old = GUI.backgroundColor;
                    if (d.ToString("yyyy-MM-dd") == (calendarEnd ? calendarFilter.To : calendarFilter.From)) GUI.backgroundColor = new Color(.8f,.7f,.4f);
                    if (GUILayout.Button(day.ToString(), button, GUILayout.Width((window.width - 65) / 7))) picked = d;
                    GUI.backgroundColor = old;
                }
            }
            GUILayout.EndHorizontal();
        }
        if (picked.HasValue) {
            string date = picked.Value.ToString("yyyy-MM-dd");
            if (calendarEnd) { calendarFilter.To = date; if (string.CompareOrdinal(calendarFilter.From,date)>0) calendarFilter.From=date; }
            else { calendarFilter.From = date; if (calendarFilter.To.Length>0 && string.CompareOrdinal(calendarFilter.To,date)<0) calendarFilter.To=date; }
            calendarFilter = null; backupPage = 0; scroll = Vector2.zero;
        }
        GUILayout.EndVertical();
    }
    static bool ParseDate(string text, out DateTime date) { return DateTime.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date); }
    static bool Matches(string name, DateTime date, string colors, string nameQuery, Filter filter)
    {
        if (!string.IsNullOrWhiteSpace(nameQuery) && DisplayName(name).IndexOf(nameQuery.Trim(), StringComparison.OrdinalIgnoreCase) < 0) return false;
        DateTime from, to;
        if (filter.From.Length > 0 && (!ParseDate(filter.From, out from) || date.ToLocalTime().Date < from)) return false;
        if (filter.To.Length > 0 && (!ParseDate(filter.To, out to) || date.ToLocalTime().Date > to)) return false;
        return filter.Colors.All(c => colors.Contains(c.ToString()));
    }
    string DeckColors(Client_DeckContents contents)
    {
        if (contents == null || contents.Piles == null) return "?";
        try {
            var db = typeof(DeckManagerController).GetField("_cardDatabase", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(deckScreen);
            if (db == null) return "?";
            var data = db.GetType().GetProperty("CardDataProvider").GetValue(db, null);
            var lookup = data.GetType().GetMethod("GetCardPrintingById", new[] { typeof(uint), typeof(string) });
            if (lookup == null) return "?";
            string result = "";
            foreach (var card in contents.Piles.Values.SelectMany(p => p)) {
                var printing = lookup.Invoke(data, new object[] { card.Id, null });
                if (printing == null) return "?";
                var flags = printing.GetType().GetProperty("ColorIdentityFlags").GetValue(printing, null).ToString();
                var names = new[] { "White", "Blue", "Black", "Red", "Green" };
                for (int i = 0; i < names.Length; i++) if (flags.Contains(names[i]) && !result.Contains("WUBRG"[i].ToString())) result += "WUBRG"[i];
            }
            return result.Length == 0 ? "C" : result;
        } catch (Exception ex) { Logger.LogWarning("Color unavailable: " + ex.Message); return "?"; }
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
            string originalName = (saved.Summary.Name ?? "").Replace("\r", " ").Replace("\n", " ");
            text = "About\nName " + originalName + "\n\n" + Regex.Replace(text, @"(?ms)\AAbout\r?\n.*?(?:\r?\n){2}", "");
            string exports = Path.Combine(Root, "exports"); Directory.CreateDirectory(exports);
            File.WriteAllText(Path.Combine(exports, saved.Summary.DeckId + ".txt"), text, new UTF8Encoding(false));
            GUIUtility.systemCopyBuffer = text;
            status = "Copied";
        }
        catch (Exception ex) { hasError = true; Logger.LogError(ex); status = "Export failed: " + (ex.InnerException ?? ex).Message; }
    }

    void DrawConfirmation()
    {
        int n = confirmedTargets == null ? 0 : confirmedTargets.Count;
        GUILayout.Label("Delete these " + n + " decks and keep " + (confirmedInventory.Count - n) + "?", heading);

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
        hasError = false; busy = true; StartCoroutine(Guarded(task));
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
                hasError = true; Logger.LogError(ex);
                status = ex.Message + (uncertain ? " Restart Arena before another deletion." : " Refresh and review the selection again.");
                if (runPath != null)
                {
                    try { WriteJson(Path.Combine(runPath, "stopped.json"), new { Error = ex.ToString(), Deleted = succeeded, OutcomeUncertain = uncertain }); } catch { }
                }
                loadingPage = false; confirming = false; busy = false;
                if (restoreUncertain) status = "Restore stopped. Check your Arena decks before restarting to try again. " + ex.Message;
                yield break;
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

    IEnumerator LoadRows(bool refresh = false)
    {
        InitializeStorage();
        SavePreferences(); rows.Clear(); colors.Clear(); loadingPage = true;
        runPath = null;
        SetStatus("Loading your personal decks...");
        provider = Pantry.GetWithoutCreate<DeckDataProvider>(true);
        if (provider == null || !provider.Initialized) throw new Exception("Arena deck data is not ready yet.");
        // Opening uses the same live cache as Arena's deck grid; explicit refresh stays available.
        if (refresh) yield return RefreshServer();
        rows = Snapshot(); keep = MassDeletePolicy.InitialKeep(rows, preferences);
        colors.Clear();
        foreach (var row in rows) colors[row.Id] = DeckColors(provider.GetDeckForId(row.Id).Contents);
        loadingPage = false;
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
                backups.Add(new { Id = deck.Id, Name = deck.Summary.Name, BackedUpAtUtc = DateTime.UtcNow, Summary = deck.Summary, Contents = contents });
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



