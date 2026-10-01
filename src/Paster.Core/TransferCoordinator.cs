using System;
using System.Threading;

namespace Paster.Core
{
    public sealed class TransferCoordinator
    {
        private readonly ITextCapturePlatform platform; private readonly IClock clock; private readonly PasterConfig config;
        private readonly object gate = new object(); private string captured; private CancellationTokenSource cancellation; private string lastError; private bool captureInProgress; private IntPtr transferTarget; private uint capturedSequence;
        public TransferCoordinator(ITextCapturePlatform p, IClock c, PasterConfig cfg) { platform = p; clock = c; config = cfg; config.Validate(); }
        public TransferStatus Status { get { lock (gate) { return new TransferStatus { HasCapture = captured != null, IsTransferring = cancellation != null, IsInputAvailable = platform.IsInputAvailable, LastError = lastError, TargetWindow = transferTarget }; } } }
        public void Clear() { lock (gate) { captured = null; } }
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
            try { clock.Sleep(config.StartDelayMilliseconds, token); for (int i = 0; i < text.Length; ) { token.ThrowIfCancellationRequested(); if (platform.IsUserInterruptionRequested) throw new OperationCanceledException("Transfer interrupted by keyboard or mouse input."); if (platform.ForegroundWindow != target) throw new InvalidOperationException("Foreground target changed."); int length = Math.Min(config.CharactersPerBatch, text.Length - i); if (i + length < text.Length && Char.IsHighSurrogate(text[i + length - 1])) length++; if (!platform.SendText(text.Substring(i, length))) throw new InvalidOperationException("Windows input rejected a text batch."); i += length; clock.Sleep(config.CharacterDelayMilliseconds, token); } return true; }
            catch (OperationCanceledException) { Fail("Transfer cancelled.", false); return false; } catch (Exception ex) { Fail(ex.Message, false); return false; } finally { lock (gate) { if (cancellation != null) { cancellation.Dispose(); cancellation = null; } transferTarget = IntPtr.Zero; } }
        }
        public void Cancel() { lock (gate) { if (cancellation != null) cancellation.Cancel(); } }
        public void ReportError(string error) { lock (gate) { lastError = error; } }
        private bool Fail(string error) { return Fail(error, true); }
        private bool Fail(string error, bool clearCapture) { lock (gate) { if (clearCapture) captured = null; lastError = error; } return false; }
    }
    public sealed class SystemClock : IClock { public void Sleep(int milliseconds, CancellationToken token) { if (milliseconds > 0) token.WaitHandle.WaitOne(milliseconds); token.ThrowIfCancellationRequested(); } }
}
