using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace Paster.Core
{
    [DataContract]
    public sealed class PasterConfig
    {
        // Code mode defaults to Home and the Space+Backspace popup dismissal: neither removes existing text. Escape (clears terminal lines, cancels cell edits) and Delete
        // (eats text unless typing at the end of a document) are opt-in.
        // Order groups the settings file by topic; every option has a default, so a hand-edited file may omit any of them.
        [DataMember(Name="captureShortcut", Order=1)] public string CaptureShortcut = "Ctrl+Shift+C";
        [DataMember(Name="pasteShortcut", Order=2)] public string PasteShortcut = "Ctrl+Shift+V";
        [DataMember(Name="cancelShortcut", Order=3)] public string CancelShortcut = "Ctrl+Shift+X";
        [DataMember(Name="anyOrderShortcuts", Order=4)] public bool AnyOrderShortcuts = true;
        [DataMember(Name="startDelayMilliseconds", Order=5)] public int StartDelayMilliseconds = 0;
        [DataMember(Name="characterDelayMilliseconds", Order=6)] public int CharacterDelayMilliseconds = 2;
        [DataMember(Name="charactersPerBatch", Order=7)] public int CharactersPerBatch = 32;
        [DataMember(Name="typeShiftedAsUnicode", Order=8)] public bool TypeShiftedAsUnicode = true;
        [DataMember(Name="typeAllAsUnicode", Order=9)] public bool TypeAllAsUnicode = false;
        [DataMember(Name="stopOnUserInput", Order=10)] public bool StopOnUserInput = true;
        [DataMember(Name="clipboardTimeoutMilliseconds", Order=11)] public int ClipboardTimeoutMilliseconds = 2000;
        [DataMember(Name="maximumTextCharacters", Order=12)] public int MaximumTextCharacters = 16 * 1024 * 1024;
        [DataMember(Name="confirmAboveLines", Order=13)] public int ConfirmAboveLines = 2000;
        [DataMember(Name="promptTimeoutSeconds", Order=14)] public int PromptTimeoutSeconds = 120;
        [DataMember(Name="typeOnCtrlV", Order=15)] public bool TypeOnCtrlV = true;
        // Remote desktop programs: Microsoft's clients, connection managers (RDCMan, mRemoteNG, Royal TS, Devolutions RDM), Hyper-V, Citrix
        // (session window and desktop viewer), VMware Horizon (its session window process), TeamViewer, AnyDesk, VNC viewers, and the
        // Windows 365 / Azure Virtual Desktop web client, recognized by its window title.
        [DataMember(Name="typeOnPasteApps", Order=16)] public string TypeOnPasteApps = DefaultRemoteApps;
        public const string DefaultRemoteApps = "mstsc.exe, msrdc.exe, RDCMan.exe, mRemoteNG.exe, RoyalTS.exe, RemoteDesktopManager.exe, RemoteDesktopManager64.exe, vmconnect.exe, wfica32.exe, CDViewer.exe, vmware-remotemks.exe, TeamViewer.exe, AnyDesk.exe, vncviewer.exe, tvnviewer.exe, msedge.exe:Windows App, chrome.exe:Windows App, msedge.exe:Windows Cloud, chrome.exe:Windows Cloud";
        [DataMember(Name="typeRemoteCopies", Order=17)] public bool TypeRemoteCopies = true;
        [DataMember(Name="codeModeForRemotePaste", Order=18)] public bool CodeModeForRemotePaste = true;
        [DataMember(Name="codeModeDismissSuggestions", Order=19)] public bool CodeModeDismissSuggestions = true;
        [DataMember(Name="codeModeEscapeBeforeEnter", Order=20)] public bool CodeModeEscapeBeforeEnter = false;
        [DataMember(Name="codeModeHomeAfterEnter", Order=21)] public bool CodeModeHomeAfterEnter = true;
        [DataMember(Name="codeModeDeleteAutoClosed", Order=22)] public bool CodeModeDeleteAutoClosed = false;
        [DataMember(Name="remoteCharactersPerSecond", Order=23)] public int RemoteCharactersPerSecond = 1000;
        [DataMember(Name="showTrayIcon", Order=24)] public bool ShowTrayIcon = false;
        // Independent of the tray icon: without a permanent icon, Paster shows its icon only while a notification is on screen.
        [DataMember(Name="showNotifications", Order=25)] public bool ShowNotifications = true;
        // Remote windows get characters as Unicode but Enter, Tab, and code mode's keys as keys; a remote client inside a remote session
        // delivered those keys up to about 60 ms after the characters that followed them. Characters after keys wait this long.
        [DataMember(Name="remoteKeyPauseMilliseconds", Order=26)] public int RemoteKeyPauseMilliseconds = 150;
        // Ctrl+V in a listed remote desktop program tries the session's own paste first and types only where clipboard sharing is blocked.
        [DataMember(Name="pasteNormallyFirst", Order=27)] public bool PasteNormallyFirst = true;

        public const int MaximumTextLimit = 256 * 1024 * 1024;

        public void Validate()
        {
            Shortcut capture = Shortcut.Parse(CaptureShortcut); Shortcut paste = Shortcut.Parse(PasteShortcut); Shortcut cancel = Shortcut.Parse(CancelShortcut);
            if (capture.Modifiers == paste.Modifiers && capture.Key == paste.Key || capture.Modifiers == cancel.Modifiers && capture.Key == cancel.Key || paste.Modifiers == cancel.Modifiers && paste.Key == cancel.Key)
                throw new FormatException("Capture, paste, and cancel shortcuts must be different.");
            if (StartDelayMilliseconds < 0 || StartDelayMilliseconds > 60000) throw new ArgumentOutOfRangeException("StartDelayMilliseconds", "Start delay must be between 0 and 60000 milliseconds.");
            if (CharacterDelayMilliseconds < 0 || CharacterDelayMilliseconds > 60000) throw new ArgumentOutOfRangeException("CharacterDelayMilliseconds", "Batch delay must be between 0 and 60000 milliseconds.");
            if (CharactersPerBatch < 1 || CharactersPerBatch > 4096) throw new ArgumentOutOfRangeException("CharactersPerBatch", "Characters per batch must be between 1 and 4096.");
            if (ClipboardTimeoutMilliseconds < 100 || ClipboardTimeoutMilliseconds > 120000) throw new ArgumentOutOfRangeException("ClipboardTimeoutMilliseconds", "Clipboard timeout must be between 100 and 120000 milliseconds.");
            if (RemoteKeyPauseMilliseconds < 0 || RemoteKeyPauseMilliseconds > 1000) throw new ArgumentOutOfRangeException("RemoteKeyPauseMilliseconds", "The pause after Enter and Tab must be between 0 and 1000 milliseconds.");
            if (RemoteCharactersPerSecond < 0 || RemoteCharactersPerSecond > 100000) throw new ArgumentOutOfRangeException("RemoteCharactersPerSecond", "Remote speed must be between 0 (unlimited) and 100000 characters per second.");
            if (MaximumTextCharacters < 1 || MaximumTextCharacters > MaximumTextLimit) throw new ArgumentOutOfRangeException("MaximumTextCharacters", "Maximum characters must be between 1 and " + MaximumTextLimit.ToString("N0") + ".");
            if (PromptTimeoutSeconds < 10 || PromptTimeoutSeconds > 3600) throw new ArgumentOutOfRangeException("PromptTimeoutSeconds", "Prompt timeout must be between 10 and 3600 seconds.");
            if (ConfirmAboveLines < 0 || ConfirmAboveLines > 100000000) throw new ArgumentOutOfRangeException("ConfirmAboveLines", "Confirmation threshold must be between 0 (never) and 100000000 lines.");
        }

        // Every field is a value type or string, so a shallow copy is complete and new settings can never be forgotten here.
        public CodeModeKeys CodeModeKeys { get { return (CodeModeDismissSuggestions ? CodeModeKeys.DismissSuggestions : 0) | (CodeModeEscapeBeforeEnter ? CodeModeKeys.EscapeBeforeEnter : 0) | (CodeModeHomeAfterEnter ? CodeModeKeys.HomeAfterEnter : 0) | (CodeModeDeleteAutoClosed ? CodeModeKeys.DeleteAutoClosed : 0); } }
        public PasterConfig Clone() { return (PasterConfig)MemberwiseClone(); }
        public void CopyFrom(PasterConfig other) { foreach (FieldInfo field in typeof(PasterConfig).GetFields(BindingFlags.Public | BindingFlags.Instance)) field.SetValue(this, field.GetValue(other)); }

        // Entries are process names (".exe" optional), optionally followed by ":" and text the window title must contain,
        // so a browser-based Remote Desktop client can be matched without capturing Ctrl+V in every browser tab.
        public bool IsTypeOnPasteApp(string processPath, string windowTitle) { return MatchesApp(TypeOnPasteApps, processPath, windowTitle); }
        // Browser-based clients read the clipboard as soon as Ctrl+V is pressed, whether or not the session (or one nested in it) accepts
        // the text, so a read proves nothing there: they always type.
        public bool IsPasteFirstApp(string processPath, string windowTitle) { return PasteNormallyFirst && !IsBrowser(processPath) && MatchesApp(TypeOnPasteApps, processPath, windowTitle); }
        private static readonly string[] Browsers = { "msedge", "chrome", "firefox", "brave", "opera", "vivaldi", "iexplore" };
        private static bool IsBrowser(string processPath) { if (String.IsNullOrEmpty(processPath)) return false; string name = StripExe(Path.GetFileName(processPath)); for (int i = 0; i < Browsers.Length; i++) if (Browsers[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return true; return false; }
        private static bool MatchesApp(string list, string processPath, string windowTitle)
        {
            if (String.IsNullOrEmpty(processPath) || String.IsNullOrEmpty(list)) return false;
            string name = StripExe(Path.GetFileName(processPath));
            string[] entries = list.Split(',', ';');
            for (int i = 0; i < entries.Length; i++)
            {
                string entry = entries[i].Trim(), title = null; int colon = entry.IndexOf(':'); if (colon >= 0) { title = entry.Substring(colon + 1).Trim(); entry = entry.Substring(0, colon).Trim(); }
                if (entry.Length == 0 || !StripExe(entry).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                if (String.IsNullOrEmpty(title) || windowTitle != null && windowTitle.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
        private static string StripExe(string name) { return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name; }

        // The serializer skips field initializers, so settings missing from older files would otherwise load as zero.
        [OnDeserializing] private void OnDeserializing(StreamingContext context) { CopyFrom(new PasterConfig()); }
        // Lists that were once the default: a file still holding one exactly was never edited, so it gains the programs added since. An edited
        // list is kept as it is.
        private static readonly string[] PreviousDefaultRemoteApps = {
            "mstsc.exe, msrdc.exe, msedge.exe:Windows Cloud, chrome.exe:Windows Cloud",
            "mstsc.exe, msrdc.exe, RDCMan.exe, msedge.exe:Windows Cloud, chrome.exe:Windows Cloud",
            "mstsc.exe, msrdc.exe, RDCMan.exe, msedge.exe:Windows App, chrome.exe:Windows App, msedge.exe:Windows Cloud, chrome.exe:Windows Cloud" };
        [OnDeserialized] private void OnDeserialized(StreamingContext context) { if (Array.IndexOf(PreviousDefaultRemoteApps, TypeOnPasteApps) >= 0) TypeOnPasteApps = DefaultRemoteApps; }

        public static PasterConfig Load(string path) { return File.Exists(path) ? TryLoad(path) ?? new PasterConfig() : new PasterConfig(); }

        // Leaves a complete settings file behind so every option is visible and editable: a missing file gets the defaults and a readable
        // one gains options added since it was written. An unreadable or invalid file is left untouched, so a typo never wipes the settings.
        public static PasterConfig LoadOrCreate(string path) { string ignored; return LoadOrCreate(path, out ignored); }
        // error explains why an existing file was not used (null when it was), so the running process can tell the user it fell back to defaults.
        public static PasterConfig LoadOrCreate(string path, out string error)
        {
            error = null; PasterConfig c = File.Exists(path) ? TryLoad(path, out error) : new PasterConfig();
            if (c == null) { error = "The settings file could not be used (" + error + "). Paster is running with the default settings until the file is fixed or saved from the settings dialog."; return new PasterConfig(); }
            try { c.Save(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return c;
        }

        // Null when the file is missing, unreadable, or holds invalid values.
        public static PasterConfig TryLoad(string path) { string ignored; return TryLoad(path, out ignored); }
        public static PasterConfig TryLoad(string path, out string error)
        {
            error = null;
            try
            {
                // Windows PowerShell 5.1 and older Notepad save UTF-8 with a byte-order mark (and Notepad's "Unicode" is UTF-16), which the JSON
                // reader rejects as an unexpected character. The text is decoded by its byte-order mark first and read as plain UTF-8.
                string json; using (StreamReader reader = new StreamReader(path, new UTF8Encoding(false), true)) json = reader.ReadToEnd();
                using (MemoryStream s = new MemoryStream(new UTF8Encoding(false).GetBytes(json))) { PasterConfig c = (PasterConfig)new DataContractJsonSerializer(typeof(PasterConfig)).ReadObject(s); c.Validate(); return c; }
            }
            catch (Exception ex) { error = FirstLine(ex.Message); return null; }
        }
        // Argument exceptions append "Parameter name: ..." on a second line.
        public static string FirstLine(string message) { int end = message.IndexOfAny(new char[] { '\r', '\n' }); return end >= 0 ? message.Substring(0, end) : message; }

        public void Save(string path)
        {
            Validate(); string dir = Path.GetDirectoryName(path); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string temp = path + ".tmp"; using (FileStream s = File.Create(temp)) using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(s, new UTF8Encoding(false), false, true, "  ")) { new DataContractJsonSerializer(typeof(PasterConfig)).WriteObject(writer, this); writer.Flush(); }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
    }

    public sealed class Shortcut
    {
        public uint Modifiers; public uint Key;
        private Shortcut(uint modifiers, uint key) { Modifiers = modifiers; Key = key; }
        public static Shortcut Parse(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new FormatException("Shortcut is empty.");
            uint mods = 0; uint key = 0; bool hasKey = false;
            string[] parts = value.Split('+');
            for (int i = 0; i < parts.Length; i++) { string p = parts[i].Trim(); uint modifier = 0; if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase)) modifier = 2; else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifier = 1; else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifier = 4; else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) || p.Equals("Windows", StringComparison.OrdinalIgnoreCase)) modifier = 8; if (modifier != 0) { if ((mods & modifier) != 0) throw new FormatException("Shortcut contains a duplicate modifier."); mods |= modifier; continue; } uint parsedKey = 0; bool parsed = false; if (p.Length == 1 && (Char.IsLetterOrDigit(p[0]) && p[0] <= 0x7F)) { parsedKey = Char.ToUpperInvariant(p[0]); parsed = true; } else if (p.StartsWith("F", StringComparison.OrdinalIgnoreCase) && p.Length <= 3) { int n; if (Int32.TryParse(p.Substring(1), out n) && n >= 1 && n <= 24) { parsedKey = (uint)(0x70 + n - 1); parsed = true; } } else if (p.Equals("Escape", StringComparison.OrdinalIgnoreCase)) { parsedKey = 0x1B; parsed = true; } if (!parsed) throw new FormatException("Unknown or unsupported shortcut key."); if (hasKey) throw new FormatException("A shortcut can contain only one key."); key = parsedKey; hasKey = true; }
            if (!hasKey || mods == 0) throw new FormatException("A shortcut needs modifiers and one key."); return new Shortcut(mods, key);
        }
    }
}
