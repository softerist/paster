using System;
using System.Diagnostics;
using System.Threading;

namespace Paster.Core
{
    public sealed class TransferCoordinator
    {
        private readonly ITextCapturePlatform platform; private readonly IClock clock; private readonly PasterConfig config;
        private readonly object gate = new object(); private string captured; private CancellationTokenSource cancellation; private string lastError; private bool captureInProgress; private IntPtr transferTarget; private uint capturedSequence; private string resumeText; private int resumeOffset;
        public TransferCoordinator(ITextCapturePlatform p, IClock c, PasterConfig cfg) { platform = p; clock = c; config = cfg; config.Validate(); }
        public TransferStatus Status { get { lock (gate) { return new TransferStatus { HasCapture = captured != null, IsTransferring = cancellation != null, IsInputAvailable = platform.IsInputAvailable, LastError = lastError, TargetWindow = transferTarget, ResumeOffset = resumeOffset, ResumeTotal = resumeText == null ? 0 : resumeText.Length }; } } }
        public void Clear() { lock (gate) { captured = null; resumeText = null; resumeOffset = 0; } }
        public bool Capture()
        {
            lock (gate) { if (captureInProgress) { lastError = "A capture is already in progress."; return false; } if (cancellation != null) { lastError = "A transfer is already in progress."; return false; } captureInProgress = true; lastError = null; }
            try
            {
                uint sequence = platform.ClipboardSequence;
                if (!platform.SendCopyShortcut()) return Fail("Copy shortcut could not be sent.");
                DateTime end = DateTime.UtcNow.AddMilliseconds(config.ClipboardTimeoutMilliseconds); string text;
                do { uint current = platform.ClipboardSequence; if (current != sequence && platform.TryReadClipboard(out text) && text != null) { if (text.Length > config.MaximumTextCharacters) return Fail("Clipboard text exceeds the configured size limit."); lock (gate) { captured = text; capturedSequence = current; lastError = null; } return true; } clock.Sleep(20, CancellationToken.None); } while (DateTime.UtcNow < end);
                return Fail("Clipboard did not provide fresh Unicode text before the timeout.");
            }
            finally { lock (gate) { captureInProgress = false; } }
        }
        public bool Transfer() { return Transfer(CodeModeKeys.None); }
        public bool Transfer(CodeModeKeys codeMode)
        {
            string text;
            // The clipboard is read outside the lock: a clipboard owned by a remote session can take seconds to render, and Status callers must not wait on it.
            uint sequence = platform.ClipboardSequence; bool useClipboard; lock (gate) { useClipboard = captured == null || sequence != capturedSequence; }
            string clipboard = null; if (useClipboard) platform.TryReadClipboard(out clipboard);
            lock (gate)
            {
                if (captureInProgress) return Fail("A capture is still in progress.", false);
                if (cancellation != null) return Fail("A transfer is already in progress.", false);
                // No gate on an earlier input rejection (UAC prompt, lock screen): each transfer tries again, so Paster recovers by itself.
                // Whichever is newer wins: text copied normally after a capture replaces it, so plain Ctrl+C works as the copy step.
                text = captured;
                if (useClipboard || text == null)
                {
                    if (clipboard != null) { if (clipboard.Length > config.MaximumTextCharacters) return Fail("Clipboard text exceeds the configured size limit.", false); text = clipboard; }
                    else if (text == null) return Fail("No captured text or clipboard text is available.", false);
                }
                cancellation = new CancellationTokenSource(); transferTarget = platform.ForegroundWindow; lastError = null;
            }
            IntPtr target; CancellationToken token; lock (gate) { target = transferTarget; token = cancellation.Token; }
            if (text.IndexOf('\r') >= 0) text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            // Remote clients consume input far slower than SendInput accepts it; anything sent ahead of them queues up where cancellation
            // can no longer reach it. Pacing remote targets in ~10 ms batches keeps that backlog small so interruption stays immediate.
            bool remote = platform.IsRemoteWindow(target); int rate = remote ? config.RemoteCharactersPerSecond : 0; int batchSize = rate > 0 ? Math.Max(1, Math.Min(config.CharactersPerBatch, rate / 100)) : config.CharactersPerBatch; Stopwatch paced = Stopwatch.StartNew();
            // Only large texts (more lines than the threshold) are confirmed and can be resumed; shorter ones always type from the start.
            int lines = CountLines(text); bool large = config.ConfirmAboveLines > 0 && lines > config.ConfirmAboveLines;
            int offset = 0, sent = 0; long keysSent = 0;
            try
            {
                offset = ChooseStart(text, lines, large, target, remote, rate, batchSize, codeMode, token); if (offset < 0) { Fail("Transfer cancelled.", false); return false; } sent = offset;
                clock.Sleep(config.StartDelayMilliseconds, token); paced.Restart(); for (int i = offset; i < text.Length; ) { token.ThrowIfCancellationRequested(); if (platform.IsUserInterruptionRequested) throw new OperationCanceledException("Transfer interrupted by keyboard or mouse input."); if (platform.ForegroundWindow != target) throw new InvalidOperationException("Foreground target changed."); int length = Math.Min(batchSize, text.Length - i); if (i + length < text.Length && Char.IsHighSurrogate(text[i + length - 1])) length++; try { if (!platform.SendText(text, i, length, codeMode)) throw new InvalidOperationException("Windows input rejected a text batch."); } catch (InputRejectedException rejected) { sent = i + rejected.CharactersSent; if (sent > 0 && sent < text.Length && Char.IsLowSurrogate(text[sent])) sent--; throw; } keysSent += CodeTyping.CountKeys(text, i, length, codeMode); i += length; sent = i; int wait = config.CharacterDelayMilliseconds; if (rate > 0) wait = (int)Math.Max(wait, keysSent * 1000 / rate - paced.ElapsedMilliseconds); clock.Sleep(wait, token); }
                lock (gate) { resumeText = null; resumeOffset = 0; } return true;
            }
            catch (OperationCanceledException) { if (large) SaveResumePoint(text, sent); Fail("Transfer cancelled.", false); return false; } catch (Exception ex) { if (large) SaveResumePoint(text, sent); Fail(ex.Message, false); return false; } finally { lock (gate) { if (cancellation != null) { cancellation.Dispose(); cancellation = null; } transferTarget = IntPtr.Zero; } }
        }
        // Returns the offset to start typing from, or -1 when the user declines. Large texts ask first, offering Resume when the same text
        // was interrupted earlier; the platform returns focus to the target before typing begins.
        private int ChooseStart(string text, int lines, bool large, IntPtr target, bool remote, int rate, int batchSize, CodeModeKeys codeMode, CancellationToken token)
        {
            // Pasting different text discards the resume point. The comparison (up to the full text) runs outside the lock so Status never waits on it.
            string previous; int previousOffset; lock (gate) { previous = resumeText; previousOffset = resumeOffset; }
            int resumeAt = large && previous != null && previousOffset < text.Length && String.Equals(previous, text) ? previousOffset : 0;
            if (resumeAt == 0 && previous != null) lock (gate) { if (Object.ReferenceEquals(resumeText, previous)) { resumeText = null; resumeOffset = 0; } }
            if (!large) return 0;
            // Duration is bounded by the batch delay floor (characters per batch) and, for remote targets, by the rate in keystrokes.
            double charactersPerSecond = batchSize * 1000.0 / Math.Max(1, config.CharacterDelayMilliseconds);
            TimeSpan full = TimeSpan.FromSeconds(Math.Max(text.Length / charactersPerSecond, rate > 0 ? CodeTyping.CountKeys(text, 0, text.Length, codeMode) / (double)rate : 0));
            TimeSpan rest = TimeSpan.FromSeconds(Math.Max((text.Length - resumeAt) / charactersPerSecond, rate > 0 ? CodeTyping.CountKeys(text, resumeAt, text.Length - resumeAt, codeMode) / (double)rate : 0));
            string where = remote ? "the remote session" : "this window", nl = Environment.NewLine;
            string message = resumeAt > 0
                ? String.Format("Typing into {0} stopped after {1:N0} of {2:N0} characters ({3}%).{7}{7}Resume types the remaining {4:N0} characters ({5}). Start over types all of them again ({6}).", where, resumeAt, text.Length, (long)resumeAt * 100 / text.Length, text.Length - resumeAt, TransferPrompt.Describe(rest), TransferPrompt.Describe(full), nl)
                : String.Format("Type {5:N0} lines ({0:N0} characters) into {1}?{4}{4}This takes {2}{3}. {6}; you can resume later.", text.Length, where, TransferPrompt.Describe(full), rate > 0 ? String.Format(" at {0:N0} keystrokes per second", rate) : "", nl, lines, config.StopOnUserInput ? "Press any key or click to stop" : "Press the cancel shortcut to stop");
            TransferChoice choice = platform.ConfirmTransfer(new TransferPrompt { TargetWindow = target, ResumeOffset = resumeAt, Message = message, Cancellation = token });
            token.ThrowIfCancellationRequested();
            if (choice == TransferChoice.Cancel) return -1;
            if (platform.ForegroundWindow != target) throw new InvalidOperationException("Could not return to the target window after the prompt.");
            if (choice == TransferChoice.Resume && resumeAt > 0) return resumeAt;
            lock (gate) { resumeText = null; resumeOffset = 0; } return 0;
        }
        private static int CountLines(string text) { int lines = 1; for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lines++; return lines; }
        private void SaveResumePoint(string text, int sent) { lock (gate) { if (sent > 0 && sent < text.Length) { resumeText = text; resumeOffset = sent; } } }
        public void Cancel() { lock (gate) { if (cancellation != null) cancellation.Cancel(); } }
        public void ReportError(string error) { lock (gate) { lastError = error; } }
        private bool Fail(string error) { return Fail(error, true); }
        private bool Fail(string error, bool clearCapture) { lock (gate) { if (clearCapture) captured = null; lastError = error; } return false; }
    }
    public sealed class SystemClock : IClock { public void Sleep(int milliseconds, CancellationToken token) { if (milliseconds > 0) token.WaitHandle.WaitOne(milliseconds); token.ThrowIfCancellationRequested(); } }
}
