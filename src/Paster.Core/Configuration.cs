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
        [DataMember(Name="captureShortcut")] public string CaptureShortcut = "Ctrl+Alt+C";
        [DataMember(Name="pasteShortcut")] public string PasteShortcut = "Ctrl+Alt+V";
        [DataMember(Name="cancelShortcut")] public string CancelShortcut = "Ctrl+Alt+X";
        [DataMember(Name="startDelayMilliseconds")] public int StartDelayMilliseconds = 250;
        [DataMember(Name="characterDelayMilliseconds")] public int CharacterDelayMilliseconds = 8;
        [DataMember(Name="clipboardTimeoutMilliseconds")] public int ClipboardTimeoutMilliseconds = 2000;
        [DataMember(Name="maximumTextCharacters")] public int MaximumTextCharacters = 1024 * 1024;

        public void Validate()
        {
            Shortcut.Parse(CaptureShortcut); Shortcut.Parse(PasteShortcut); Shortcut.Parse(CancelShortcut);
            if (StartDelayMilliseconds < 0 || StartDelayMilliseconds > 60000) throw new ArgumentOutOfRangeException("StartDelayMilliseconds");
            if (CharacterDelayMilliseconds < 0 || CharacterDelayMilliseconds > 60000) throw new ArgumentOutOfRangeException("CharacterDelayMilliseconds");
            if (ClipboardTimeoutMilliseconds < 100 || ClipboardTimeoutMilliseconds > 120000) throw new ArgumentOutOfRangeException("ClipboardTimeoutMilliseconds");
            if (MaximumTextCharacters < 1 || MaximumTextCharacters > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException("MaximumTextCharacters");
        }

        public static PasterConfig Load(string path)
        {
            if (!File.Exists(path)) return new PasterConfig();
            try { using (FileStream s = File.OpenRead(path)) { PasterConfig c = (PasterConfig)new DataContractJsonSerializer(typeof(PasterConfig)).ReadObject(s); c.Validate(); return c; } }
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
            for (int i = 0; i < parts.Length; i++) { string p = parts[i].Trim(); if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase)) mods |= 2; else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= 1; else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= 4; else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) || p.Equals("Windows", StringComparison.OrdinalIgnoreCase)) mods |= 8; else if (p.Length == 1) { key = Char.ToUpperInvariant(p[0]); hasKey = true; } else if (p.StartsWith("F", StringComparison.OrdinalIgnoreCase) && p.Length <= 3) { int n; if (Int32.TryParse(p.Substring(1), out n) && n >= 1 && n <= 24) { key = (uint)(0x70 + n - 1); hasKey = true; } } else if (p.Equals("Escape", StringComparison.OrdinalIgnoreCase)) { key = 0x1B; hasKey = true; } else throw new FormatException("Unknown shortcut key."); }
            if (!hasKey || mods == 0) throw new FormatException("A shortcut needs modifiers and one key."); return new Shortcut(mods, key);
        }
    }
}
