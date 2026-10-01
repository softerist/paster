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
        // Order groups the settings file by topic; every option has a default, so a hand-edited file may omit any of them.
        [DataMember(Name="captureShortcut", Order=1)] public string CaptureShortcut = "Ctrl+Shift+C";
        [DataMember(Name="pasteShortcut", Order=2)] public string PasteShortcut = "Ctrl+Shift+V";
        [DataMember(Name="cancelShortcut", Order=3)] public string CancelShortcut = "Ctrl+Shift+X";
        [DataMember(Name="anyOrderShortcuts", Order=4)] public bool AnyOrderShortcuts = true;
        [DataMember(Name="startDelayMilliseconds", Order=5)] public int StartDelayMilliseconds = 0;
        [DataMember(Name="characterDelayMilliseconds", Order=6)] public int CharacterDelayMilliseconds = 2;
        [DataMember(Name="charactersPerBatch", Order=7)] public int CharactersPerBatch = 32;
        [DataMember(Name="typeShiftedAsUnicode", Order=8)] public bool TypeShiftedAsUnicode = true;
        [DataMember(Name="stopOnUserInput", Order=9)] public bool StopOnUserInput = true;
        [DataMember(Name="clipboardTimeoutMilliseconds", Order=10)] public int ClipboardTimeoutMilliseconds = 2000;
        [DataMember(Name="maximumTextCharacters", Order=11)] public int MaximumTextCharacters = 16 * 1024 * 1024;
        [DataMember(Name="confirmAboveLines", Order=12)] public int ConfirmAboveLines = 2000;
        [DataMember(Name="promptTimeoutSeconds", Order=13)] public int PromptTimeoutSeconds = 120;
        [DataMember(Name="typeOnCtrlV", Order=14)] public bool TypeOnCtrlV = true;
        [DataMember(Name="typeOnPasteApps", Order=15)] public string TypeOnPasteApps = "mstsc.exe, msrdc.exe, msedge.exe:Windows Cloud, chrome.exe:Windows Cloud";
        [DataMember(Name="typeRemoteCopies", Order=16)] public bool TypeRemoteCopies = true;
        [DataMember(Name="codeModeForRemotePaste", Order=17)] public bool CodeModeForRemotePaste = true;
        [DataMember(Name="codeModeEscapeBeforeEnter", Order=18)] public bool CodeModeEscapeBeforeEnter = true;
        [DataMember(Name="codeModeHomeAfterEnter", Order=19)] public bool CodeModeHomeAfterEnter = true;
        [DataMember(Name="codeModeDeleteAutoClosed", Order=20)] public bool CodeModeDeleteAutoClosed = true;
        [DataMember(Name="remoteCharactersPerSecond", Order=21)] public int RemoteCharactersPerSecond = 500;

        public const int MaximumTextLimit = 256 * 1024 * 1024;

        public void Validate()
        {
            Shortcut capture = Shortcut.Parse(CaptureShortcut); Shortcut paste = Shortcut.Parse(PasteShortcut); Shortcut cancel = Shortcut.Parse(CancelShortcut);
            if (capture.Modifiers == paste.Modifiers && capture.Key == paste.Key || capture.Modifiers == cancel.Modifiers && capture.Key == cancel.Key || paste.Modifiers == cancel.Modifiers && paste.Key == cancel.Key)
                throw new FormatException("Capture, paste, and cancel shortcuts must be different.");
            if (StartDelayMilliseconds < 0 || StartDelayMilliseconds > 60000) throw new ArgumentOutOfRangeException("StartDelayMilliseconds");
            if (CharacterDelayMilliseconds < 0 || CharacterDelayMilliseconds > 60000) throw new ArgumentOutOfRangeException("CharacterDelayMilliseconds", "Batch delay must be between 0 and 60000 milliseconds.");
            if (CharactersPerBatch < 1 || CharactersPerBatch > 4096) throw new ArgumentOutOfRangeException("CharactersPerBatch", "Characters per batch must be between 1 and 4096.");
            if (ClipboardTimeoutMilliseconds < 100 || ClipboardTimeoutMilliseconds > 120000) throw new ArgumentOutOfRangeException("ClipboardTimeoutMilliseconds");
            if (RemoteCharactersPerSecond < 0 || RemoteCharactersPerSecond > 100000) throw new ArgumentOutOfRangeException("RemoteCharactersPerSecond", "Remote speed must be between 0 (unlimited) and 100000 characters per second.");
            if (MaximumTextCharacters < 1 || MaximumTextCharacters > MaximumTextLimit) throw new ArgumentOutOfRangeException("MaximumTextCharacters", "Maximum characters must be between 1 and " + MaximumTextLimit.ToString("N0") + ".");
            if (PromptTimeoutSeconds < 10 || PromptTimeoutSeconds > 3600) throw new ArgumentOutOfRangeException("PromptTimeoutSeconds", "Prompt timeout must be between 10 and 3600 seconds.");
            if (ConfirmAboveLines < 0 || ConfirmAboveLines > 100000000) throw new ArgumentOutOfRangeException("ConfirmAboveLines", "Confirmation threshold must be between 0 (never) and 100000000 lines.");
        }

        // Every field is a value type or string, so a shallow copy is complete and new settings can never be forgotten here.
        public CodeModeKeys CodeModeKeys { get { return (CodeModeEscapeBeforeEnter ? CodeModeKeys.EscapeBeforeEnter : 0) | (CodeModeHomeAfterEnter ? CodeModeKeys.HomeAfterEnter : 0) | (CodeModeDeleteAutoClosed ? CodeModeKeys.DeleteAutoClosed : 0); } }
        public PasterConfig Clone() { return (PasterConfig)MemberwiseClone(); }
        public void CopyFrom(PasterConfig other) { foreach (FieldInfo field in typeof(PasterConfig).GetFields(BindingFlags.Public | BindingFlags.Instance)) field.SetValue(this, field.GetValue(other)); }

        // Entries are process names (".exe" optional), optionally followed by ":" and text the window title must contain,
        // so a browser-based Remote Desktop client can be matched without capturing Ctrl+V in every browser tab.
        public bool IsTypeOnPasteApp(string processPath, string windowTitle)
        {
            if (String.IsNullOrEmpty(processPath) || String.IsNullOrEmpty(TypeOnPasteApps)) return false;
            string name = StripExe(Path.GetFileName(processPath));
            string[] entries = TypeOnPasteApps.Split(',', ';');
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

        public static PasterConfig Load(string path) { return File.Exists(path) ? TryRead(path) ?? new PasterConfig() : new PasterConfig(); }

        // Leaves a complete settings file behind so every option is visible and editable: a missing file gets the defaults and a readable
        // one gains options added since it was written. An unreadable or invalid file is left untouched, so a typo never wipes the settings.
        public static PasterConfig LoadOrCreate(string path)
        {
            PasterConfig c = File.Exists(path) ? TryRead(path) : new PasterConfig();
            if (c == null) return new PasterConfig();
            try { c.Save(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return c;
        }

        private static PasterConfig TryRead(string path)
        {
            try { using (FileStream s = File.OpenRead(path)) { PasterConfig c = (PasterConfig)new DataContractJsonSerializer(typeof(PasterConfig)).ReadObject(s); if (c.StartDelayMilliseconds == 250) c.StartDelayMilliseconds = 0; c.Validate(); return c; } }
            catch { return null; }
        }

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
