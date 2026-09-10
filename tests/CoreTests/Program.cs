using System;
using System.Collections.Generic;
using System.Threading;
using Paster.Core;

namespace Paster.CoreTests
{
    internal sealed class FakePlatform : ITextCapturePlatform
    {
        public IntPtr ForegroundWindow = new IntPtr(1); public bool CopyWorks = true; public string Clipboard; public bool InputWorks = true; public bool ChangeAfterFirst; public uint Sequence; public List<string> Units = new List<string>();
        IntPtr ITextCapturePlatform.ForegroundWindow { get { return (ChangeAfterFirst && Units.Count > 0) ? new IntPtr(2) : ForegroundWindow; } }
        public bool IsInputAvailable { get { return true; } }
        public uint ClipboardSequence { get { return Sequence; } }
        public bool SendCopyShortcut() { if (CopyWorks) Sequence++; return CopyWorks; }
        public bool TryReadClipboard(out string text) { text = Clipboard; return text != null; }
        public bool SendTextUnit(string textUnit) { if (!InputWorks) return false; Units.Add(textUnit); return true; }
    }
    internal sealed class FakeClock : IClock { public void Sleep(int milliseconds, CancellationToken token) { token.ThrowIfCancellationRequested(); } }
    internal static class Program
    {
        private static int passed;
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS "+name); }
        public static int Main()
        {
            Test("capture and structured transfer", delegate { FakePlatform p=new FakePlatform(); p.Clipboard="{\"x\": 1}\nitems:\n\t- café\n\npunctuation: !?"; TransferCoordinator c=new TransferCoordinator(p,new FakeClock(),new PasterConfig()); Assert(c.Capture(),"capture"); Assert(c.Transfer(),"transfer"); Assert(String.Join("",p.Units.ToArray()).Contains("café"),"unicode"); });
            Test("capture failure clears stale text", delegate { FakePlatform p=new FakePlatform(); p.Clipboard="old"; TransferCoordinator c=new TransferCoordinator(p,new FakeClock(),new PasterConfig()); Assert(c.Capture(),"first"); p.CopyWorks=false; Assert(!c.Capture(),"failure"); Assert(!c.Status.HasCapture,"stale capture"); });
            Test("size limit", delegate { FakePlatform p=new FakePlatform(); p.Clipboard="12345"; PasterConfig cfg=new PasterConfig(); cfg.MaximumTextCharacters=3; TransferCoordinator c=new TransferCoordinator(p,new FakeClock(),cfg); Assert(!c.Capture(),"limit"); });
            Test("unsafe character delay rejected", delegate { PasterConfig cfg=new PasterConfig(); cfg.CharacterDelayMilliseconds=1; bool rejected=false; try { cfg.Validate(); } catch(ArgumentOutOfRangeException) { rejected=true; } Assert(rejected,"unsafe delay"); });
            Test("clipboard fallback transfer", delegate { FakePlatform p=new FakePlatform(); p.Clipboard="copied normally"; TransferCoordinator c=new TransferCoordinator(p,new FakeClock(),new PasterConfig()); Assert(c.Transfer(),"transfer"); Assert(String.Join("",p.Units.ToArray())==p.Clipboard,"clipboard fallback"); });
            Test("focus change and cancellation", delegate { FakePlatform p=new FakePlatform(); p.Clipboard="abc"; TransferCoordinator c=new TransferCoordinator(p,new FakeClock(),new PasterConfig()); Assert(c.Capture(),"capture"); p.ChangeAfterFirst=true; Assert(!c.Transfer(),"focus stop"); p.ChangeAfterFirst=false; Assert(c.Capture(),"recapture"); c.Cancel(); Assert(!c.Status.IsTransferring,"cancel state"); });
            Test("overlap prevention", delegate { FakePlatform p=new FakePlatform(); p.Clipboard="abc"; TransferCoordinator c=new TransferCoordinator(p,new FakeClock(),new PasterConfig()); Assert(c.Capture(),"capture"); Assert(c.Transfer(),"transfer"); Assert(!c.Status.IsTransferring,"finished"); });
            Console.WriteLine("{0} tests passed", passed); return 0;
        }
    }
}
