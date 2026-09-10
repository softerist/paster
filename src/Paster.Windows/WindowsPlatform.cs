using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Paster.Core;

namespace Paster.Windows
{
    internal sealed class WindowsPlatform : NativeWindow, ITextCapturePlatform, IDisposable
    {
        private const uint MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8, WM_HOTKEY=0x0312, KEYEVENTF_UNICODE=4, KEYEVENTF_KEYUP=2;
        private const int HOT_CAPTURE=1, HOT_PASTE=2, HOT_CANCEL=3; private readonly PasterConfig config;
        public event Action CaptureRequested; public event Action PasteRequested; public event Action CancelRequested;
        public WindowsPlatform(PasterConfig c) { config = c; CreateHandle(new CreateParams()); Register(config.CaptureShortcut, HOT_CAPTURE); Register(config.PasteShortcut, HOT_PASTE); Register(config.CancelShortcut, HOT_CANCEL); }
        public IntPtr ForegroundWindow { get { return GetForegroundWindow(); } }
        public uint ClipboardSequence { get { return GetClipboardSequenceNumber(); } }
        public bool IsInputAvailable { get { return true; } }
        public void WaitForKeysReleased() { ReleaseModifiers(); }
        public bool SendCopyShortcut() { ReleaseModifiers(); SendKey(0x11, false, false, true); SendKey(0x43, false, false, true); SendKey(0x11, false, false, false); return true; }
        public bool TryReadClipboard(out string text) { text = null; try { if (Clipboard.ContainsText(TextDataFormat.UnicodeText)) { text = Clipboard.GetText(TextDataFormat.UnicodeText); return true; } } catch { } return false; }
        public bool SendTextUnit(string unit) { if (unit == "\n") { SendKey(0x0D, false, true); return true; } if (unit == "\t") { SendKey(0x09, false, true); return true; } if (unit.Length == 0) return true; for (int i=0; i<unit.Length; i++) { INPUT input = new INPUT(); input.type=1; input.ki.wVk=0; input.ki.wScan=unit[i]; input.ki.dwFlags=KEYEVENTF_UNICODE; if (SendInput(1, ref input, Marshal.SizeOf(typeof(INPUT))) != 1) return false; input.ki.dwFlags=KEYEVENTF_UNICODE|KEYEVENTF_KEYUP; if (SendInput(1, ref input, Marshal.SizeOf(typeof(INPUT))) != 1) return false; } return true; }
        private void SendKey(ushort key, bool unicode, bool enter) { SendKey(key, unicode, enter, true); }
        private void SendKey(ushort key, bool unicode, bool enter, bool release) { INPUT i=new INPUT(); i.type=1; i.ki.wVk=key; i.ki.dwFlags=0; SendInput(1,ref i,Marshal.SizeOf(typeof(INPUT))); if(release) { i.ki.dwFlags=KEYEVENTF_KEYUP; SendInput(1,ref i,Marshal.SizeOf(typeof(INPUT))); } }
        private void ReleaseModifiers() { while ((GetAsyncKeyState(0x11)&0x8000)!=0 || (GetAsyncKeyState(0x12)&0x8000)!=0 || (GetAsyncKeyState(0x10)&0x8000)!=0) Thread.Sleep(10); }
        private void Register(string value, int id) { Paster.Core.Shortcut s=Paster.Core.Shortcut.Parse(value); if (!RegisterHotKey(Handle,id,s.Modifiers,s.Key)) throw new InvalidOperationException("Shortcut registration failed for "+value+"."); }
        protected override void WndProc(ref Message m) { if (m.Msg==WM_HOTKEY) { int id=m.WParam.ToInt32(); if(id==HOT_CAPTURE && CaptureRequested!=null) CaptureRequested(); else if(id==HOT_PASTE && PasteRequested!=null) PasteRequested(); else if(id==HOT_CANCEL && CancelRequested!=null) CancelRequested(); } base.WndProc(ref m); }
        public void Dispose() { UnregisterHotKey(Handle,HOT_CAPTURE); UnregisterHotKey(Handle,HOT_PASTE); UnregisterHotKey(Handle,HOT_CANCEL); DestroyHandle(); }
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public KEYBDINPUT ki; } [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk,wScan; public uint dwFlags,time; public UIntPtr dwExtraInfo; }
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd,int id,uint fsModifiers,uint vk); [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd,int id); [DllImport("user32.dll")] private static extern uint SendInput(uint n,ref INPUT i,int size); [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey); [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    }

    internal sealed class PasterApplicationContext : ApplicationContext
    {
        private readonly TransferCoordinator coordinator; private readonly PasterConfig config; private readonly string configPath; private readonly WindowsPlatform platform; private readonly ManagementPipe pipe;
        public PasterApplicationContext(WindowsPlatform p, TransferCoordinator c, PasterConfig cfg, string path) { platform=p; coordinator=c; config=cfg; configPath=path; platform.CaptureRequested += delegate { Thread t=new Thread((ThreadStart)delegate { coordinator.Capture(); }); t.IsBackground=true; t.SetApartmentState(ApartmentState.STA); t.Start(); }; platform.PasteRequested += delegate { platform.WaitForKeysReleased(); Thread t=new Thread((ThreadStart)delegate { coordinator.Transfer(); }); t.IsBackground=true; t.Start(); }; platform.CancelRequested += delegate { coordinator.Cancel(); }; pipe=new ManagementPipe(this); }
        public void Command(string command) { if (command=="settings") Management.ShowSettings(config,configPath); else if(command=="status") Management.ShowStatus(coordinator); else if(command=="clear") coordinator.Clear(); else if(command=="exit") ExitThread(); else if(command=="enable-autostart") Management.SetAutostart(true); else if(command=="disable-autostart") Management.SetAutostart(false); }
        protected override void ExitThreadCore() { pipe.Dispose(); platform.Dispose(); base.ExitThreadCore(); }
    }
}
