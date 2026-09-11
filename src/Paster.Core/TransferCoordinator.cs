using System;
using System.Threading;

namespace Paster.Core
{
    public sealed class TransferCoordinator
    {
        private readonly ITextCapturePlatform platform; private readonly IClock clock; private readonly PasterConfig config;
        private readonly object gate = new object(); private string captured; private CancellationTokenSource cancellation; private string lastError; private bool captureInProgress; private IntPtr transferTarget;
        public TransferCoordinator(ITextCapturePlatform p, IClock c, PasterConfig cfg) { platform = p; clock = c; config = cfg; config.Validate(); }
        public TransferStatus Status { get { lock (gate) { return new TransferStatus { HasCapture = captured != null, IsTransferring = cancellation != null, IsInputAvailable = platform.IsInputAvailable, LastError = lastError, TargetWindow = transferTarget }; } } }
        public void Clear() { lock (gate) { captured = null; } }
        public bool Capture()
        {
            lock (gate) { if (captureInProgress) { lastError = "A capture is already in progress."; return false; } captureInProgress = true; lastError = null; }
            try
            {
                uint sequence = platform.ClipboardSequence;
                if (!platform.SendCopyShortcut()) return Fail("Copy shortcut could not be sent.");
                DateTime end = DateTime.UtcNow.AddMilliseconds(config.ClipboardTimeoutMilliseconds); string text;
                do { if (platform.ClipboardSequence != sequence && platform.TryReadClipboard(out text) && text != null) { if (text.Length > config.MaximumTextCharacters) return Fail("Clipboard text exceeds the configured size limit."); lock (gate) { captured = text; lastError = null; } return true; } clock.Sleep(20, CancellationToken.None); } while (DateTime.UtcNow < end);
                return Fail("Clipboard did not provide fresh Unicode text before the timeout.");
            }
            finally { lock (gate) { captureInProgress = false; } }
        }
        public bool Transfer()
        {
            string text;
            lock (gate)
            {
                if (captureInProgress) return Fail("A capture is still in progress.");
                if (cancellation != null) return Fail("A transfer is already in progress.");
                if (!platform.IsInputAvailable) return Fail("Windows input is unavailable.");
                text = captured;
                if (text == null)
                {
                    if (!platform.TryReadClipboard(out text) || text == null) return Fail("No captured text or clipboard text is available.");
                    if (text.Length > config.MaximumTextCharacters) return Fail("Clipboard text exceeds the configured size limit.");
                }
                cancellation = new CancellationTokenSource(); transferTarget = platform.ForegroundWindow; lastError = null;
            }
            IntPtr target; CancellationToken token; lock (gate) { target = transferTarget; token = cancellation.Token; }
            try { clock.Sleep(config.StartDelayMilliseconds, token); for (int i = 0; i < text.Length; i++) { token.ThrowIfCancellationRequested(); if (platform.ForegroundWindow != target) throw new InvalidOperationException("Foreground target changed."); string unit = text[i].ToString(); if (unit == "\r") { if (i + 1 < text.Length && text[i + 1] == '\n') i++; unit = "\n"; } if (!platform.SendTextUnit(unit)) throw new InvalidOperationException("Windows input rejected a text unit."); clock.Sleep(config.CharacterDelayMilliseconds, token); } return true; }
            catch (OperationCanceledException) { Fail("Transfer cancelled."); return false; } catch (Exception ex) { Fail(ex.Message); return false; } finally { lock (gate) { if (cancellation != null) { cancellation.Dispose(); cancellation = null; } transferTarget = IntPtr.Zero; } }
        }
        public void Cancel() { lock (gate) { if (cancellation != null) cancellation.Cancel(); } }
        public void ReportError(string error) { lock (gate) { lastError = error; } }
        private bool Fail(string error) { lock (gate) { captured = null; lastError = error; } return false; }
    }
    public sealed class SystemClock : IClock { public void Sleep(int milliseconds, CancellationToken token) { if (milliseconds > 0) token.WaitHandle.WaitOne(milliseconds); token.ThrowIfCancellationRequested(); } }
}
