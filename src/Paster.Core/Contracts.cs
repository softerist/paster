using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Paster.Core
{
    public interface ITextCapturePlatform
    {
        IntPtr ForegroundWindow { get; }
        uint ClipboardSequence { get; }
        bool SendCopyShortcut();
        bool TryReadClipboard(out string text);
        bool SendText(string text, bool codeMode);
        bool IsInputAvailable { get; }
        bool IsUserInterruptionRequested { get; }
        bool IsRemoteWindow(IntPtr window);
        // Asks the user before a long or resumable transfer. A non-cancel answer must return focus to prompt.TargetWindow.
        TransferChoice ConfirmTransfer(TransferPrompt prompt);
    }
    public interface IClock { void Sleep(int milliseconds, CancellationToken token); }
    public sealed class TransferStatus
    {
        public string LastError; public bool HasCapture; public bool IsTransferring; public bool IsInputAvailable; public IntPtr TargetWindow; public int ResumeOffset; public int ResumeTotal;
    }
    public enum TransferChoice { Cancel, Start, Resume }
    public enum KeyAction { Character, Enter, Tab, Escape, Home, Delete }
    public struct TypedKey { public KeyAction Action; public char Character; public TypedKey(KeyAction action, char character) { Action = action; Character = character; } }
    // Code mode counters editor automation without knowing which editor is focused (the remote app is invisible locally):
    // Escape closes suggestion popups before Enter/Tab can accept them, Home after Enter moves before auto-inserted indentation so the
    // line's own indentation is typed exactly, and Delete after an opener removes an auto-closed bracket or quote. Delete assumes typing
    // at the end of a document, where nothing but auto-inserted text follows the cursor.
    [Flags] public enum CodeModeKeys { None = 0, EscapeBeforeEnter = 1, HomeAfterEnter = 2, DeleteAutoClosed = 4, All = 7 }
    public static class CodeTyping
    {
        public const string Openers = "([{\"'`";
        public static List<TypedKey> Expand(string text, bool codeMode) { return Expand(text, codeMode ? CodeModeKeys.All : CodeModeKeys.None); }
        public static List<TypedKey> Expand(string text, CodeModeKeys extra)
        {
            bool escape = (extra & CodeModeKeys.EscapeBeforeEnter) != 0, home = (extra & CodeModeKeys.HomeAfterEnter) != 0, delete = (extra & CodeModeKeys.DeleteAutoClosed) != 0;
            List<TypedKey> keys = new List<TypedKey>(text.Length * (extra != CodeModeKeys.None ? 2 : 1));
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\n') { if (escape) keys.Add(new TypedKey(KeyAction.Escape, '\0')); keys.Add(new TypedKey(KeyAction.Enter, '\0')); if (home) keys.Add(new TypedKey(KeyAction.Home, '\0')); }
                else if (ch == '\t') { if (escape) keys.Add(new TypedKey(KeyAction.Escape, '\0')); keys.Add(new TypedKey(KeyAction.Tab, '\0')); }
                else { keys.Add(new TypedKey(KeyAction.Character, ch)); if (delete && Openers.IndexOf(ch) >= 0) keys.Add(new TypedKey(KeyAction.Delete, '\0')); }
            }
            return keys;
        }
    }
    public sealed class TransferPrompt
    {
        public IntPtr TargetWindow; public int ResumeOffset; public string Message; public CancellationToken Cancellation;
        public bool CanResume { get { return ResumeOffset > 0; } }
        public static string Describe(TimeSpan duration)
        {
            double seconds = Math.Max(1, Math.Ceiling(duration.TotalSeconds));
            if (seconds < 60) return "about " + seconds + (seconds == 1 ? " second" : " seconds");
            double minutes = Math.Ceiling(seconds / 60);
            if (minutes < 60) return "about " + minutes + (minutes == 1 ? " minute" : " minutes");
            int hours = (int)(minutes / 60), rest = (int)(minutes % 60);
            return "about " + hours + (hours == 1 ? " hour" : " hours") + (rest > 0 ? " " + rest + (rest == 1 ? " minute" : " minutes") : "");
        }
    }
}
