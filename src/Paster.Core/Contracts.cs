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
        // remote: the destination is a remote-desktop client (a listed app); every character is then sent as a Unicode character, not a key.
        bool SendText(string text, int start, int length, CodeModeKeys codeMode, bool remote);
        bool IsInputAvailable { get; }
        bool IsUserInterruptionRequested { get; }
        bool IsRemoteWindow(IntPtr window);
        // True when Windows would silently discard simulated input to the window (it runs at a higher integrity level, e.g. as administrator).
        bool IsInputBlocked(IntPtr window);
        // True when the user physically pressed a key or mouse button within the last second, counting only input after typing was set up.
        bool HasRecentUserInput { get; }
        // A short, user-facing name of the program that owns the window (such as "Microsoft Teams"), or null when it cannot be determined.
        string DescribeWindow(IntPtr window);
        // Asks the user before a long or resumable transfer. A non-cancel answer must return focus to prompt.TargetWindow.
        TransferChoice ConfirmTransfer(TransferPrompt prompt);
    }
    public interface IClock { void Sleep(int milliseconds, CancellationToken token); }
    public sealed class TransferStatus
    {
        public string LastError; public bool HasCapture; public bool IsTransferring; public bool IsInputAvailable; public IntPtr TargetWindow; public int ResumeOffset; public int ResumeTotal;
    }
    public enum TransferChoice { Cancel, Start, Resume }
    public enum KeyAction { Character, Enter, Tab, Escape, Home, Delete, Backspace, End }
    // Index is the position (relative to the expanded range) of the character that produced the key (helper keys share their character's index).
    public struct TypedKey { public KeyAction Action; public char Character; public int Index; public TypedKey(KeyAction action, char character, int index) { Action = action; Character = character; Index = index; } }
    // Thrown by SendText when Windows inserted only part of a batch; CharactersSent counts the leading characters whose key reached the target.
    public sealed class InputRejectedException : Exception { public readonly int CharactersSent; public InputRejectedException(string message, int charactersSent) : base(message) { CharactersSent = charactersSent; } }
    // Code mode counters editor automation without knowing which editor is focused (the remote app is invisible locally):
    // - DismissSuggestions types Space then Backspace before Enter/Tab when the previous character is not whitespace: the space ends the word,
    //   so suggestion popups (Notepad++ word completion, VS Code IntelliSense) close instead of Enter/Tab accepting them, and Backspace removes
    //   it. It is a no-op in terminals and documents, and it is skipped after whitespace, where Backspace could unindent or clear a cell.
    // - EscapeBeforeEnter closes popups too, but also clears terminal input lines and cancels spreadsheet cell edits.
    // - HomeAfterEnter moves before auto-inserted indentation so the line's own indentation is typed exactly. That indentation stays after the
    //   caret, and Enter would carry it to the next line, where a smart Home (Notepad++, VS Code) stops after it instead of at the line start:
    //   every second line gained indentation. So End comes first and Enter always splits at the end of the line.
    // - DeleteAutoClosed removes an auto-closed bracket or quote; it assumes typing at the end of a document.
    [Flags] public enum CodeModeKeys { None = 0, EscapeBeforeEnter = 1, HomeAfterEnter = 2, DeleteAutoClosed = 4, DismissSuggestions = 8, All = 15 }
    public static class CodeTyping
    {
        public const string Openers = "([{\"'`";
        // True when Expand turns the character into keys rather than (only) a character: line breaks and tabs always, openers when
        // DeleteAutoClosed adds Delete. Remote windows receive characters as Unicode and these as keys, which travel separately.
        public static bool ProducesKeys(char ch, CodeModeKeys extra) { return ch == '\n' || ch == '\t' || (extra & CodeModeKeys.DeleteAutoClosed) != 0 && Openers.IndexOf(ch) >= 0; }
        // How many times typing text[start, start+length) switches from keys back to characters, i.e. how many remote key pauses it needs.
        public static int CountKeyPauses(string text, int start, int length, CodeModeKeys extra) { int pauses = 0; for (int i = start; i < start + length - 1; i++) if (ProducesKeys(text[i], extra) && !ProducesKeys(text[i + 1], extra)) pauses++; return pauses; }
        // Keystrokes Expand produces for text[start, start+length), without allocating; remote pacing counts keys, not characters.
        public static int CountKeys(string text, int start, int length, CodeModeKeys extra)
        {
            int escape = (extra & CodeModeKeys.EscapeBeforeEnter) != 0 ? 1 : 0, home = (extra & CodeModeKeys.HomeAfterEnter) != 0 ? 1 : 0, delete = (extra & CodeModeKeys.DeleteAutoClosed) != 0 ? 1 : 0, keys = length;
            bool dismiss = (extra & CodeModeKeys.DismissSuggestions) != 0;
            if (extra == CodeModeKeys.None) return keys;
            for (int i = start; i < start + length; i++) { char ch = text[i]; if (ch == '\n' || ch == '\t') keys += (dismiss && Dismisses(text, i) ? 2 : 0) + escape + (ch == '\n' ? 2 * home : 0); else if (Openers.IndexOf(ch) >= 0) keys += delete; }
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
                    // After the popup is closed: in an open completion list End would select the last entry and Enter would accept it.
                    if (ch == '\n' && home) keys.Add(new TypedKey(KeyAction.End, '\0', index));
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

    // Whether a normal paste reached the session behind a window that pastes normally first.
    public enum PasteCheck { Unknown, Works, Blocked }
    // Text: plain text only, which Paster can republish without losing anything, to watch the paste. Other: content with further formats
    // (HTML, rich text, images, Office or editor data), which republishing the text alone would drop, so Paster leaves it untouched.
    // Private: the source asked clipboard tools to keep no copy (password managers), so it is never republished or pasted through clipboard
    // sharing; it is typed.
    public enum ClipboardKind { Text, Other, Private }
    public enum PasteAction { Type, TryNormalPaste, PasteNormally }
    // Remembers per remote window whether a normal paste reached its session, and decides what Ctrl+V does there. Blocked is final for the
    // window: typing always works, and another attempt could paste the session's old clipboard again. Works is checked again with every
    // paste of text, because the focus inside a remote session can move to a nested session that blocks clipboard sharing.
    public sealed class PasteFirstMemory
    {
        private readonly Dictionary<IntPtr, PasteCheck> checks = new Dictionary<IntPtr, PasteCheck>(); private readonly object gate = new object();
        public static PasteAction Decide(PasteCheck check, ClipboardKind kind)
        {
            if (kind == ClipboardKind.Private || check == PasteCheck.Blocked) return PasteAction.Type;
            if (kind == ClipboardKind.Text) return PasteAction.TryNormalPaste;
            // Content that cannot be watched is pasted normally only where a normal paste has already been seen to work.
            return check == PasteCheck.Works ? PasteAction.PasteNormally : PasteAction.Type;
        }
        // The session's own clipboard may already have been pasted when Paster notices, and only the user can see whether it was.
        public static string BlockedMessage(string program) { return "Normal paste didn't reach " + (program ?? "this window") + ": clipboard sharing is blocked there, so Ctrl+V types into it from now on. If something else was pasted, undo it (Ctrl+Z) and press Ctrl+V again."; }
        // isAlive drops windows that were closed, so a window handle reused by a new window starts unchecked.
        public PasteCheck Get(IntPtr window, Func<IntPtr, bool> isAlive) { lock (gate) { PasteCheck check; if (!checks.TryGetValue(window, out check)) return PasteCheck.Unknown; if (isAlive(window)) return check; checks.Remove(window); return PasteCheck.Unknown; } }
        public void Set(IntPtr window, PasteCheck check, Func<IntPtr, bool> isAlive) { lock (gate) { List<IntPtr> closed = new List<IntPtr>(); foreach (IntPtr known in checks.Keys) if (!isAlive(known)) closed.Add(known); foreach (IntPtr gone in closed) checks.Remove(gone); checks[window] = check; } }
        public int Count(PasteCheck check) { lock (gate) { int count = 0; foreach (PasteCheck known in checks.Values) if (known == check) count++; return count; } }
        public void Clear() { lock (gate) { checks.Clear(); } }
    }
}
