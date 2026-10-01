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
    }
    public interface IClock { void Sleep(int milliseconds, CancellationToken token); }
    public sealed class TransferStatus
    {
        public string LastError; public bool HasCapture; public bool IsTransferring; public bool IsInputAvailable; public IntPtr TargetWindow;
    }
}
