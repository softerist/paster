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
        public bool Transfer()
        {
            string text;
            // The clipboard is read outside the lock: a clipboard owned by a remote session can take seconds to render, and Status callers must not wait on it.
            uint sequence = platform.ClipboardSequence; bool useClipboard; lock (gate) { useClipboard = captured == null || sequence != capturedSequence; }
            string clipboard = null; if (useClipboard) platform.TryReadClipboard(out clipboard);
            lock (gate)
            {
                if (captureInProgress) return Fail("A capture is still in progress.", false);
                if (cancellation != null) return Fail("A transfer is already in progress.", false);
                if (!platform.IsInputAvailable) return Fail("Windows input is unavailable.", false);
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
            int offset = 0, sent = 0;
            try
            {
                offset = ChooseStart(text, target, remote, rate, batchSize, token); if (offset < 0) { Fail("Transfer cancelled.", false); return false; } sent = offset;
                clock.Sleep(config.StartDelayMilliseconds, token); paced.Restart(); for (int i = offset; i < text.Length; ) { token.ThrowIfCancellationRequested(); if (platform.IsUserInterruptionRequested) throw new OperationCanceledException("Transfer interrupted by keyboard or mouse input."); if (platform.ForegroundWindow != target) throw new InvalidOperationException("Foreground target changed."); int length = Math.Min(batchSize, text.Length - i); if (i + length < text.Length && Char.IsHighSurrogate(text[i + length - 1])) length++; if (!platform.SendText(text.Substring(i, length))) throw new InvalidOperationException("Windows input rejected a text batch."); i += length; sent = i; int wait = config.CharacterDelayMilliseconds; if (rate > 0) wait = (int)Math.Max(wait, (long)(i - offset) * 1000 / rate - paced.ElapsedMilliseconds); clock.Sleep(wait, token); }
                lock (gate) { resumeText = null; resumeOffset = 0; } return true;
            }
            catch (OperationCanceledException) { SaveResumePoint(text, sent); Fail("Transfer cancelled.", false); return false; } catch (Exception ex) { SaveResumePoint(text, sent); Fail(ex.Message, false); return false; } finally { lock (gate) { if (cancellation != null) { cancellation.Dispose(); cancellation = null; } transferTarget = IntPtr.Zero; } }
        }
        // Returns the offset to start typing from, or -1 when the user declines. Long transfers and transfers of the text that was
        // interrupted last ask first; the platform returns focus to the target before typing begins.
        private int ChooseStart(string text, IntPtr target, bool remote, int rate, int batchSize, CancellationToken token)
        {
            // Pasting different text discards the resume point. The comparison (up to the full text) runs outside the lock so Status never waits on it.
            string previous; int previousOffset; lock (gate) { previous = resumeText; previousOffset = resumeOffset; }
            int resumeAt = previous != null && previousOffset < text.Length && String.Equals(previous, text) ? previousOffset : 0;
            if (resumeAt == 0 && previous != null) lock (gate) { if (Object.ReferenceEquals(resumeText, previous)) { resumeText = null; resumeOffset = 0; } }
            // Throughput is capped both by the remote rate and by the batch delay floor.
            double perSecond = batchSize * 1000.0 / Math.Max(1, config.CharacterDelayMilliseconds); if (rate > 0) perSecond = Math.Min(perSecond, rate);
            TimeSpan full = TimeSpan.FromSeconds(text.Length / perSecond), rest = TimeSpan.FromSeconds((text.Length - resumeAt) / perSecond);
            bool warn = config.ConfirmAboveSeconds > 0 && full.TotalSeconds > config.ConfirmAboveSeconds;
            if (resumeAt == 0 && !warn) return 0;
            string where = remote ? "the remote session" : "this window", nl = Environment.NewLine;
            string message = resumeAt > 0
                ? String.Format("Typing into {0} stopped after {1:N0} of {2:N0} characters ({3}%).{7}{7}Resume types the remaining {4:N0} characters ({5}). Start over types all of them again ({6}).", where, resumeAt, text.Length, (long)resumeAt * 100 / text.Length, text.Length - resumeAt, TransferPrompt.Describe(rest), TransferPrompt.Describe(full), nl)
                : String.Format("Type {0:N0} characters into {1}?{4}{4}This takes {2}{3}. Press any key or click to stop; you can resume later.", text.Length, where, TransferPrompt.Describe(full), rate > 0 ? String.Format(" at {0:N0} characters per second", rate) : "", nl);
            TransferChoice choice = platform.ConfirmTransfer(new TransferPrompt { TargetWindow = target, ResumeOffset = resumeAt, Message = message, Cancellation = token });
            token.ThrowIfCancellationRequested();
            if (choice == TransferChoice.Cancel) return -1;
            if (platform.ForegroundWindow != target) throw new InvalidOperationException("Could not return to the target window after the prompt.");
            if (choice == TransferChoice.Resume && resumeAt > 0) return resumeAt;
            lock (gate) { resumeText = null; resumeOffset = 0; } return 0;
        }
        private void SaveResumePoint(string text, int sent) { lock (gate) { if (sent > 0 && sent < text.Length) { resumeText = text; resumeOffset = sent; } } }
        public void Cancel() { lock (gate) { if (cancellation != null) cancellation.Cancel(); } }
        public void ReportError(string error) { lock (gate) { lastError = error; } }
        private bool Fail(string error) { return Fail(error, true); }
        private bool Fail(string error, bool clearCapture) { lock (gate) { if (clearCapture) captured = null; lastError = error; } return false; }
    }
    public sealed class SystemClock : IClock { public void Sleep(int milliseconds, CancellationToken token) { if (milliseconds > 0) token.WaitHandle.WaitOne(milliseconds); token.ThrowIfCancellationRequested(); } }
}
