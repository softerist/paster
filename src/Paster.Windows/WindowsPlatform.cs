using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Paster.Core;

namespace Paster.Windows
{
    internal sealed class WindowsPlatform : NativeWindow, ITextCapturePlatform, IDisposable
    {
        private const uint MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8, MOD_NOREPEAT=0x4000, WM_HOTKEY=0x0312, KEYEVENTF_UNICODE=4, KEYEVENTF_KEYUP=2;
        private const int HOT_CAPTURE=1, HOT_PASTE=2, HOT_CANCEL=3; private readonly PasterConfig config;
        public event Action CaptureRequested; public event Action PasteRequested; public event Action CancelRequested;
        public WindowsPlatform(PasterConfig c) { config = c; CreateHandle(new CreateParams()); Register(config.CaptureShortcut, HOT_CAPTURE); Register(config.PasteShortcut, HOT_PASTE); Register(config.CancelShortcut, HOT_CANCEL); }
        public IntPtr ForegroundWindow { get { return GetForegroundWindow(); } }
        public uint ClipboardSequence { get { return GetClipboardSequenceNumber(); } }
        public bool IsInputAvailable { get { return true; } }
        public void ReleasePasteShortcut() { Paster.Core.Shortcut shortcut=Paster.Core.Shortcut.Parse(config.PasteShortcut); INPUT[] inputs=new INPUT[5]; int count=0; inputs[count++]=CreateInput((ushort)shortcut.Key,0,KEYEVENTF_KEYUP); if ((shortcut.Modifiers & MOD_WIN)!=0) inputs[count++]=CreateInput(0x5B,0,KEYEVENTF_KEYUP); if ((shortcut.Modifiers & MOD_ALT)!=0) inputs[count++]=CreateInput(0x12,0,KEYEVENTF_KEYUP); if ((shortcut.Modifiers & MOD_CONTROL)!=0) inputs[count++]=CreateInput(0x11,0,KEYEVENTF_KEYUP); if ((shortcut.Modifiers & MOD_SHIFT)!=0) inputs[count++]=CreateInput(0x10,0,KEYEVENTF_KEYUP); SendBatch(inputs,count,"paste shortcut release"); }
        public bool SendCopyShortcut() { if (!ReleaseModifiers()) return false; if (!SendKey(0x11, false)) return false; if (!SendKey(0x43, false)) { SendKey(0x11, true); return false; } if (!SendKey(0x43, true)) { SendKey(0x11, true); return false; } return SendKey(0x11, true); }
        public bool TryReadClipboard(out string text) { text = null; try { if (Clipboard.ContainsText(TextDataFormat.UnicodeText)) { text = Clipboard.GetText(TextDataFormat.UnicodeText); return true; } } catch { } return false; }
        public bool SendTextUnit(string unit) { if (unit == "\n") return SendVirtualKeyPair(0x0D); if (unit == "\t") return SendVirtualKeyPair(0x09); if (unit.Length == 0) return true; for (int i=0; i<unit.Length; i++) { short mapped=VkKeyScan(unit[i]); if (mapped != -1) { SendMappedKey(mapped); continue; } INPUT[] unicode=new INPUT[2]; unicode[0]=CreateInput(0,unit[i],KEYEVENTF_UNICODE); unicode[1]=CreateInput(0,unit[i],KEYEVENTF_UNICODE|KEYEVENTF_KEYUP); SendBatch(unicode,2,"Unicode input"); } return true; }
        private bool SendVirtualKeyPair(ushort key) { INPUT[] inputs=new INPUT[2]; inputs[0]=CreateInput(key,0,0); inputs[1]=CreateInput(key,0,KEYEVENTF_KEYUP); SendBatch(inputs,2,"virtual-key input"); return true; }
        private void SendMappedKey(short mapped) { ushort key=(ushort)(mapped & 0xff); int modifiers=(mapped >> 8) & 0xff; bool shift=(modifiers & 1)!=0, control=(modifiers & 2)!=0, alt=(modifiers & 4)!=0; INPUT[] inputs=new INPUT[8]; int count=0; if (shift) inputs[count++]=CreateInput(0x10,0,0); if (control) inputs[count++]=CreateInput(0x11,0,0); if (alt) inputs[count++]=CreateInput(0x12,0,0); inputs[count++]=CreateInput(key,0,0); inputs[count++]=CreateInput(key,0,KEYEVENTF_KEYUP); if (alt) inputs[count++]=CreateInput(0x12,0,KEYEVENTF_KEYUP); if (control) inputs[count++]=CreateInput(0x11,0,KEYEVENTF_KEYUP); if (shift) inputs[count++]=CreateInput(0x10,0,KEYEVENTF_KEYUP); SendBatch(inputs,count,"mapped keyboard input"); }
        private static INPUT CreateInput(ushort key, ushort scan, uint flags) { INPUT input=new INPUT(); input.type=1; input.ki.wVk=key; input.ki.wScan=scan; input.ki.dwFlags=flags; return input; }
        private static void SendBatch(INPUT[] inputs, int count, string kind) { if (SendInputBatch((uint)count,inputs,Marshal.SizeOf(typeof(INPUT))) != (uint)count) throw InputError(kind); }
        private static Win32Exception InputError(string kind) { int code=Marshal.GetLastWin32Error(); return new Win32Exception(code, "Windows rejected "+kind+" (error "+code+")"); }
        private bool SendKey(ushort key, bool release) { INPUT i=new INPUT(); i.type=1; i.ki.wVk=key; i.ki.dwFlags=release ? KEYEVENTF_KEYUP : 0; return SendInput(1,ref i,Marshal.SizeOf(typeof(INPUT))) == 1; }
        private bool ReleaseModifiers() { Stopwatch elapsed=Stopwatch.StartNew(); while (elapsed.ElapsedMilliseconds < 5000 && ModifiersDown()) Thread.Sleep(5); return !ModifiersDown(); }
        private static bool ModifiersDown() { return (GetAsyncKeyState(0x11)&0x8000)!=0 || (GetAsyncKeyState(0x12)&0x8000)!=0 || (GetAsyncKeyState(0x10)&0x8000)!=0; }
        private void Register(string value, int id) { Paster.Core.Shortcut s=Paster.Core.Shortcut.Parse(value); if (!RegisterHotKey(Handle,id,s.Modifiers|MOD_NOREPEAT,s.Key)) throw new InvalidOperationException("Shortcut registration failed for "+value+"."); }
        protected override void WndProc(ref Message m) { if (m.Msg==WM_HOTKEY) { int id=m.WParam.ToInt32(); if(id==HOT_CAPTURE && CaptureRequested!=null) CaptureRequested(); else if(id==HOT_PASTE && PasteRequested!=null) PasteRequested(); else if(id==HOT_CANCEL && CancelRequested!=null) CancelRequested(); } base.WndProc(ref m); }
        public void Dispose() { UnregisterHotKey(Handle,HOT_CAPTURE); UnregisterHotKey(Handle,HOT_PASTE); UnregisterHotKey(Handle,HOT_CANCEL); DestroyHandle(); }
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public KEYBDINPUT ki; private uint padding1; private uint padding2; } [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk,wScan; public uint dwFlags,time; public UIntPtr dwExtraInfo; }
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd,int id,uint fsModifiers,uint vk); [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd,int id); [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint n,ref INPUT i,int size); [DllImport("user32.dll", EntryPoint="SendInput", SetLastError=true)] private static extern uint SendInputBatch(uint n,[In] INPUT[] inputs,int size); [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey); [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber(); [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern short VkKeyScan(char ch);
    }

    internal sealed class PasterApplicationContext : ApplicationContext
    {
        private readonly TransferCoordinator coordinator; private readonly PasterConfig config; private readonly string configPath; private readonly WindowsPlatform platform; private readonly ManagementPipe pipe;
        public PasterApplicationContext(WindowsPlatform p, TransferCoordinator c, PasterConfig cfg, string path) { platform=p; coordinator=c; config=cfg; configPath=path; platform.CaptureRequested += delegate { Thread t=new Thread((ThreadStart)delegate { coordinator.Capture(); }); t.IsBackground=true; t.SetApartmentState(ApartmentState.STA); t.Start(); }; platform.PasteRequested += delegate { Thread t=new Thread((ThreadStart)delegate { try { platform.ReleasePasteShortcut(); coordinator.Transfer(); } catch(Exception ex) { coordinator.ReportError(ex.Message); } }); t.IsBackground=true; t.SetApartmentState(ApartmentState.STA); t.Start(); }; platform.CancelRequested += delegate { coordinator.Cancel(); }; pipe=new ManagementPipe(this); }
        public void Command(string command) { if (command=="settings") Management.ShowSettings(config,configPath); else if(command=="status") Management.ShowStatus(coordinator); else if(command=="clear") coordinator.Clear(); else if(command=="exit") ExitThread(); else if(command=="enable-autostart") Management.SetAutostart(true); else if(command=="disable-autostart") Management.SetAutostart(false); }
        protected override void ExitThreadCore() { pipe.Dispose(); platform.Dispose(); base.ExitThreadCore(); }
    }
}
