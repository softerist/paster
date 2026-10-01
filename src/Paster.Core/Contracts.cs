using System;
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
        bool SendText(string text);
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
