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
        bool SendText(string text, int start, int length, CodeModeKeys codeMode);
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
    public enum KeyAction { Character, Enter, Tab, Escape, Home, Delete, Backspace }
    // Index is the position (relative to the expanded range) of the character that produced the key (helper keys share their character's index).
    public struct TypedKey { public KeyAction Action; public char Character; public int Index; public TypedKey(KeyAction action, char character, int index) { Action = action; Character = character; Index = index; } }
    // Thrown by SendText when Windows inserted only part of a batch; CharactersSent counts the leading characters whose key reached the target.
    public sealed class InputRejectedException : Exception { public readonly int CharactersSent; public InputRejectedException(string message, int charactersSent) : base(message) { CharactersSent = charactersSent; } }
    // Code mode counters editor automation without knowing which editor is focused (the remote app is invisible locally):
    // - DismissSuggestions types Space then Backspace before Enter/Tab when the previous character is not whitespace: the space ends the word,
    //   so suggestion popups (Notepad++ word completion, VS Code IntelliSense) close instead of Enter/Tab accepting them, and Backspace removes
    //   it. It is a no-op in terminals and documents, and it is skipped after whitespace, where Backspace could unindent or clear a cell.
    // - EscapeBeforeEnter closes popups too, but also clears terminal input lines and cancels spreadsheet cell edits.
    // - HomeAfterEnter moves before auto-inserted indentation so the line's own indentation is typed exactly.
    // - DeleteAutoClosed removes an auto-closed bracket or quote; it assumes typing at the end of a document.
    [Flags] public enum CodeModeKeys { None = 0, EscapeBeforeEnter = 1, HomeAfterEnter = 2, DeleteAutoClosed = 4, DismissSuggestions = 8, All = 15 }
    public static class CodeTyping
    {
        public const string Openers = "([{\"'`";
        // Keystrokes Expand produces for text[start, start+length), without allocating; remote pacing counts keys, not characters.
        public static int CountKeys(string text, int start, int length, CodeModeKeys extra)
        {
            int escape = (extra & CodeModeKeys.EscapeBeforeEnter) != 0 ? 1 : 0, home = (extra & CodeModeKeys.HomeAfterEnter) != 0 ? 1 : 0, delete = (extra & CodeModeKeys.DeleteAutoClosed) != 0 ? 1 : 0, keys = length;
            bool dismiss = (extra & CodeModeKeys.DismissSuggestions) != 0;
            if (extra == CodeModeKeys.None) return keys;
            for (int i = start; i < start + length; i++) { char ch = text[i]; if (ch == '\n' || ch == '\t') keys += (dismiss && Dismisses(text, i) ? 2 : 0) + escape + (ch == '\n' ? home : 0); else if (Openers.IndexOf(ch) >= 0) keys += delete; }
            return keys;
        }
        // The character before position i is in the full text, so batch boundaries see the same context as one long batch.
        private static bool Dismisses(string text, int i) { return i > 0 && !Char.IsWhiteSpace(text[i - 1]); }
        public static List<TypedKey> Expand(string text, CodeModeKeys extra) { return Expand(text, 0, text.Length, extra); }
        public static List<TypedKey> Expand(string text, int start, int length, CodeModeKeys extra)
        {
            bool escape = (extra & CodeModeKeys.EscapeBeforeEnter) != 0, home = (extra & CodeModeKeys.HomeAfterEnter) != 0, delete = (extra & CodeModeKeys.DeleteAutoClosed) != 0, dismiss = (extra & CodeModeKeys.DismissSuggestions) != 0;
            List<TypedKey> keys = new List<TypedKey>(length * (extra != CodeModeKeys.None ? 2 : 1));
            for (int i = start; i < start + length; i++)
            {
                char ch = text[i]; int index = i - start;
                if (ch == '\n' || ch == '\t')
                {
                    if (dismiss && Dismisses(text, i)) { keys.Add(new TypedKey(KeyAction.Character, ' ', index)); keys.Add(new TypedKey(KeyAction.Backspace, '\0', index)); }
                    if (escape) keys.Add(new TypedKey(KeyAction.Escape, '\0', index));
                    keys.Add(new TypedKey(ch == '\n' ? KeyAction.Enter : KeyAction.Tab, '\0', index));
                    if (ch == '\n' && home) keys.Add(new TypedKey(KeyAction.Home, '\0', index));
                }
                else { keys.Add(new TypedKey(KeyAction.Character, ch, index)); if (delete && Openers.IndexOf(ch) >= 0) keys.Add(new TypedKey(KeyAction.Delete, '\0', index)); }
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
