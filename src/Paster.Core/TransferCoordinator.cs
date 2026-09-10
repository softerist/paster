using System;
using System.Threading;

namespace Paster.Core
{
    public sealed class TransferCoordinator
    {
        private readonly ITextCapturePlatform platform; private readonly IClock clock; private readonly PasterConfig config;
        private readonly object gate = new object(); private string captured; private CancellationTokenSource cancellation; private string lastError;
        public TransferCoordinator(ITextCapturePlatform p, IClock c, PasterConfig cfg) { platform = p; clock = c; config = cfg; config.Validate(); }
        public TransferStatus Status { get { lock (gate) { return new TransferStatus { HasCapture = captured != null, IsTransferring = cancellation != null, LastError = lastError, TargetWindow = platform.ForegroundWindow }; } } }
        public void Clear() { lock (gate) { captured = null; } }
        public bool Capture()
        {
            lock (gate) { lastError = null; }
            uint sequence = platform.ClipboardSequence;
            if (!platform.SendCopyShortcut()) return Fail("Copy shortcut could not be sent.");
            DateTime end = DateTime.UtcNow.AddMilliseconds(config.ClipboardTimeoutMilliseconds); string text;
            do { if (platform.ClipboardSequence != sequence && platform.TryReadClipboard(out text) && text != null) { if (text.Length > config.MaximumTextCharacters) return Fail("Clipboard text exceeds the configured size limit."); lock (gate) { captured = text; } return true; } clock.Sleep(20, CancellationToken.None); } while (DateTime.UtcNow < end);
            return Fail("Clipboard did not provide fresh Unicode text before the timeout.");
        }
        public bool Transfer()
        {
            lock (gate) { if (captured == null) return Fail("No captured text is available."); if (cancellation != null) return Fail("A transfer is already in progress."); cancellation = new CancellationTokenSource(); lastError = null; }
            string text; IntPtr target = platform.ForegroundWindow; CancellationToken token; lock (gate) { text = captured; token = cancellation.Token; }
            try { clock.Sleep(config.StartDelayMilliseconds, token); for (int i = 0; i < text.Length; i++) { token.ThrowIfCancellationRequested(); if (platform.ForegroundWindow != target) throw new InvalidOperationException("Foreground target changed."); string unit = text[i].ToString(); if (unit == "\r") { if (i + 1 < text.Length && text[i + 1] == '\n') i++; unit = "\n"; } if (!platform.SendTextUnit(unit)) throw new InvalidOperationException("Windows input rejected a text unit."); clock.Sleep(config.CharacterDelayMilliseconds, token); } return true; }
            catch (OperationCanceledException) { Fail("Transfer cancelled."); return false; } catch (Exception ex) { Fail(ex.Message); return false; } finally { lock (gate) { if (cancellation != null) { cancellation.Dispose(); cancellation = null; } } }
        }
        public void Cancel() { lock (gate) { if (cancellation != null) cancellation.Cancel(); } }
        private bool Fail(string error) { lock (gate) { captured = null; lastError = error; } return false; }
    }
    public sealed class SystemClock : IClock { public void Sleep(int milliseconds, CancellationToken token) { if (milliseconds > 0) token.WaitHandle.WaitOne(milliseconds); token.ThrowIfCancellationRequested(); } }
}
