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
        bool SendTextUnit(string textUnit);
        bool IsInputAvailable { get; }
    }
    public interface IClock { void Sleep(int milliseconds, CancellationToken token); }
    public sealed class TransferStatus
    {
        public string LastError; public bool HasCapture; public bool IsTransferring; public bool IsInputAvailable; public IntPtr TargetWindow;
    }
}
