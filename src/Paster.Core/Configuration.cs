using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Paster.Core
{
    [DataContract]
    public sealed class PasterConfig
    {
        [DataMember(Name="captureShortcut")] public string CaptureShortcut = "Ctrl+Shift+C";
        [DataMember(Name="pasteShortcut")] public string PasteShortcut = "Ctrl+Shift+V";
        [DataMember(Name="cancelShortcut")] public string CancelShortcut = "Ctrl+Alt+X";
        [DataMember(Name="startDelayMilliseconds")] public int StartDelayMilliseconds = 0;
        [DataMember(Name="characterDelayMilliseconds")] public int CharacterDelayMilliseconds = 8;
        [DataMember(Name="clipboardTimeoutMilliseconds")] public int ClipboardTimeoutMilliseconds = 2000;
        [DataMember(Name="maximumTextCharacters")] public int MaximumTextCharacters = 1024 * 1024;

        public void Validate()
        {
            Shortcut capture = Shortcut.Parse(CaptureShortcut); Shortcut paste = Shortcut.Parse(PasteShortcut); Shortcut cancel = Shortcut.Parse(CancelShortcut);
            if (capture.Modifiers == paste.Modifiers && capture.Key == paste.Key || capture.Modifiers == cancel.Modifiers && capture.Key == cancel.Key || paste.Modifiers == cancel.Modifiers && paste.Key == cancel.Key)
                throw new FormatException("Capture, paste, and cancel shortcuts must be different.");
            if (StartDelayMilliseconds < 0 || StartDelayMilliseconds > 60000) throw new ArgumentOutOfRangeException("StartDelayMilliseconds");
            if (CharacterDelayMilliseconds < 8 || CharacterDelayMilliseconds > 60000) throw new ArgumentOutOfRangeException("CharacterDelayMilliseconds", "Character delay must be between 8 and 60000 milliseconds to prevent dropped input.");
            if (ClipboardTimeoutMilliseconds < 100 || ClipboardTimeoutMilliseconds > 120000) throw new ArgumentOutOfRangeException("ClipboardTimeoutMilliseconds");
            if (MaximumTextCharacters < 1 || MaximumTextCharacters > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException("MaximumTextCharacters");
        }

        public PasterConfig Clone() { return new PasterConfig { CaptureShortcut = CaptureShortcut, PasteShortcut = PasteShortcut, CancelShortcut = CancelShortcut, StartDelayMilliseconds = StartDelayMilliseconds, CharacterDelayMilliseconds = CharacterDelayMilliseconds, ClipboardTimeoutMilliseconds = ClipboardTimeoutMilliseconds, MaximumTextCharacters = MaximumTextCharacters }; }
        public void CopyFrom(PasterConfig other) { CaptureShortcut = other.CaptureShortcut; PasteShortcut = other.PasteShortcut; CancelShortcut = other.CancelShortcut; StartDelayMilliseconds = other.StartDelayMilliseconds; CharacterDelayMilliseconds = other.CharacterDelayMilliseconds; ClipboardTimeoutMilliseconds = other.ClipboardTimeoutMilliseconds; MaximumTextCharacters = other.MaximumTextCharacters; }

        public static PasterConfig Load(string path)
        {
            if (!File.Exists(path)) return new PasterConfig();
            try { using (FileStream s = File.OpenRead(path)) { PasterConfig c = (PasterConfig)new DataContractJsonSerializer(typeof(PasterConfig)).ReadObject(s); if (c.StartDelayMilliseconds == 250) c.StartDelayMilliseconds = 0; c.Validate(); return c; } }
            catch { return new PasterConfig(); }
        }

        public void Save(string path)
        {
            Validate(); string dir = Path.GetDirectoryName(path); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string temp = path + ".tmp"; using (FileStream s = File.Create(temp)) { new DataContractJsonSerializer(typeof(PasterConfig)).WriteObject(s, this); }
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
