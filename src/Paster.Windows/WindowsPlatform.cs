using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Paster.Core;

namespace Paster.Windows
{
    internal sealed class WindowsPlatform : NativeWindow, ITextCapturePlatform, IDisposable
    {
        private const uint MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8, MOD_NOREPEAT=0x4000, WM_HOTKEY=0x0312, KEYEVENTF_UNICODE=4, KEYEVENTF_KEYUP=2, KEYEVENTF_EXTENDEDKEY=1;
        private const int HOT_CAPTURE=1, HOT_PASTE=2, HOT_CANCEL=3, HOT_WINDOWS_PASTE=4; private readonly PasterConfig config; private string hotkeyError, hookError; private volatile bool inputAvailable=true;
        private const int WM_TIMER=0x113, WM_POWERBROADCAST=0x218, WM_WTSSESSION_CHANGE=0x2B1, PBT_APMRESUMEAUTOMATIC=0x12, WTS_CONSOLE_CONNECT=1, WTS_REMOTE_CONNECT=3, WTS_SESSION_UNLOCK=8, HOOK_TIMER=1, HookRefreshMilliseconds=60000, WM_CAPTURE_HOTKEY=0x8001, WM_WATCH_MOUSE=0x8002, WH_MOUSE_LL=14;
        private const int LLMHF_INJECTED=1; private readonly LowLevelKeyboardProc mouseProc; private IntPtr mouseHook; private volatile int physicalClicks; private int transferClickBaseline; private long lastPhysicalInput, inputBaseline;
        private const int WH_KEYBOARD_LL=13, WM_KEYDOWN=0x100, WM_KEYUP=0x101, WM_SYSKEYDOWN=0x104, WM_SYSKEYUP=0x105; private const uint LLKHF_INJECTED=0x10, WM_CLIPBOARDUPDATE=0x031D, CF_UNICODETEXT=13;
        private readonly Paster.Core.Shortcut[] shortcuts=new Paster.Core.Shortcut[HOT_CANCEL+1]; private readonly bool[] latched=new bool[HOT_CANCEL+1]; private readonly LowLevelKeyboardProc hookProc; private IntPtr hook; private volatile int physicalKeyPresses; private int transferKeyBaseline; private volatile bool windowsPasteHeld; private bool pasteKeyDown; private volatile bool typeClipboardOnPaste;
        public event Action CaptureRequested; public event Action CancelRequested;
        // true when Ctrl+V in a listed app started the paste (it may paste normally first); false for the paste shortcut, which always types.
        public event Action<bool> PasteRequested;
        public WindowsPlatform(PasterConfig c) { config = c; DisablePowerThrottling(); CreateHandle(new CreateParams()); ReloadShortcuts(); typeClipboardOnPaste=IsClipboardFormatAvailable(CF_UNICODETEXT); AddClipboardFormatListener(Handle); hookProc=KeyboardHook; mouseProc=MouseHook; InstallHook(); WTSRegisterSessionNotification(Handle,0); SetTimer(Handle,(UIntPtr)HOOK_TIMER,HookRefreshMilliseconds,IntPtr.Zero); }
        public string RegistrationError { get { return String.IsNullOrEmpty(hotkeyError) ? hookError : String.IsNullOrEmpty(hookError) ? hotkeyError : hotkeyError+" "+hookError; } }
        // Applies the configured shortcuts while running; every old registration is released first, so two shortcuts can swap keys.
        // Returns the registration problems (null when there are none). Must run on the thread that created the window.
        public string ReloadShortcuts() { for (int id=HOT_CAPTURE; id<=HOT_CANCEL; id++) { UnregisterHotKey(Handle,id); shortcuts[id]=null; latched[id]=false; } hotkeyError=null; Register(config.CaptureShortcut,HOT_CAPTURE); Register(config.PasteShortcut,HOT_PASTE); Register(config.CancelShortcut,HOT_CANCEL); return RegistrationError; }
        // Windows 7 and later silently remove a low-level hook whose callback once exceeds the system timeout (heavy load, resume from sleep),
        // which would stop Ctrl+V typing and key interruption without any sign. No API reports whether a hook is still installed, so it is
        // replaced every minute and after resume, unlock, and session reconnect. Hook callbacks run on this thread's message loop, so none can
        // arrive between installing the new hook and removing the old one, and no key is seen twice or missed.
        private void InstallHook() { if (mouseHook!=IntPtr.Zero) SetMouseHook(true); IntPtr fresh=SetWindowsHookEx(WH_KEYBOARD_LL,hookProc,GetModuleHandle(null),0); if (fresh==IntPtr.Zero) { if (hook==IntPtr.Zero) hookError="Keyboard hook installation failed (error "+Marshal.GetLastWin32Error()+"); shortcuts require the key to be pressed last."; return; } IntPtr old=hook; hook=fresh; hookError=null; if (old!=IntPtr.Zero) UnhookWindowsHookEx(old); }
        public IntPtr ForegroundWindow { get { return GetForegroundWindow(); } }
        public uint ClipboardSequence { get { return GetClipboardSequenceNumber(); } }
        public bool IsInputAvailable { get { return inputAvailable; } }
        // The hook counts only physical key presses, so our own injected typing can never look like a user interruption.
        // Clicks are counted by the mouse hook while a paste runs, so a quick tap between two checks still stops typing; the button state is
        // the fallback when that hook is not installed.
        public bool IsUserInterruptionRequested { get { return config.StopOnUserInput && ((hook!=IntPtr.Zero ? physicalKeyPresses!=transferKeyBaseline : UserKeyDown()) || mouseHook!=IntPtr.Zero && physicalClicks!=transferClickBaseline || MouseButtonDown()); } }
        // Input before the baseline (the paste shortcut itself, the answer to a prompt) does not count, so only what the user did while
        // typing ran can explain a focus change. Mouse movement is not counted: it never changes the focus by itself.
        public bool HasRecentUserInput { get { long at=Interlocked.Read(ref lastPhysicalInput); return at>Interlocked.Read(ref inputBaseline) && Stopwatch.GetTimestamp()-at<Stopwatch.Frequency || MouseButtonDown(); } }
        private void SetInputBaseline() { transferKeyBaseline=physicalKeyPresses; transferClickBaseline=physicalClicks; Interlocked.Exchange(ref inputBaseline,Stopwatch.GetTimestamp()); }
        private void MarkInput() { Interlocked.Exchange(ref lastPhysicalInput,Stopwatch.GetTimestamp()); }
        // Clicks are watched only while a paste runs: a low-level mouse hook makes every mouse movement in the system wait for this thread,
        // so it is not kept installed while Paster is idle. The worker asks the window thread, which owns the hooks.
        public void WatchMouse(bool enabled) { IntPtr result; SendMessageTimeout(Handle,WM_WATCH_MOUSE,(IntPtr)(enabled?1:0),IntPtr.Zero,0,500,out result); }
        private void SetMouseHook(bool enabled) { if (!enabled) { if (mouseHook!=IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook=IntPtr.Zero; } return; } IntPtr fresh=SetWindowsHookEx(WH_MOUSE_LL,mouseProc,GetModuleHandle(null),0); if (fresh==IntPtr.Zero) return; IntPtr old=mouseHook; mouseHook=fresh; if (old!=IntPtr.Zero) UnhookWindowsHookEx(old); }
        // MSLLHOOKSTRUCT.flags is at offset 12 (after the POINT and mouseData). Button-down messages: left, right, middle, X.
        private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam) { if (code>=0 && (Marshal.ReadInt32(lParam,12) & LLMHF_INJECTED)==0) { int msg=wParam.ToInt32(); if (msg==0x201 || msg==0x204 || msg==0x207 || msg==0x20B) physicalClicks++; if (msg!=0x200) MarkInput(); } return CallNextHookEx(mouseHook,code,wParam,lParam); }
        // Typing while the shortcut is still physically held lets the held (and auto-repeating) modifiers turn typed text into application shortcuts.
        public bool WaitForPasteShortcutRelease() { Paster.Core.Shortcut paste=Paster.Core.Shortcut.Parse(config.PasteShortcut); WaitWhile(delegate { return ModifiersDown() || windowsPasteHeld || (GetAsyncKeyState((int)paste.Key)&0x8000)!=0; },5000); SetInputBaseline(); return !ModifiersDown() && !windowsPasteHeld && (GetAsyncKeyState((int)paste.Key)&0x8000)==0; }
        // A Ctrl+C capture shortcut is released while the copy is sent, or the injected Ctrl+C would trigger Paster instead of reaching the
        // application; if it cannot be released, nothing is sent.
        public bool SendCopyShortcut() { Paster.Core.Shortcut capture=Paster.Core.Shortcut.Parse(config.CaptureShortcut); bool captureUsesCopy=capture.Modifiers==MOD_CONTROL && capture.Key==0x43; if(captureUsesCopy && !SetCaptureHotkey(false)) return false; try { if (!ReleaseModifiers()) return false; if (!SendKey(0x11, false)) return false; if (!SendKey(0x43, false)) { SendKey(0x11, true); return false; } if (!SendKey(0x43, true)) { SendKey(0x11, true); return false; } return SendKey(0x11, true); } finally { if(captureUsesCopy) SetCaptureHotkey(true); } }
        // Hotkeys belong to the thread that created the window: RegisterHotKey and UnregisterHotKey fail from the capture worker (errors 1408
        // and 1419), so the worker asks the window thread to do it. The timeout keeps a busy window thread from blocking the worker forever.
        private bool SetCaptureHotkey(bool registered) { IntPtr result; return SendMessageTimeout(Handle,WM_CAPTURE_HOTKEY,(IntPtr)(registered?1:0),IntPtr.Zero,0,2000,out result)!=IntPtr.Zero && result!=IntPtr.Zero; }
        public bool TryReadClipboard(out string text) { text = null; try { if (Clipboard.ContainsText(TextDataFormat.UnicodeText)) { text = Clipboard.GetText(TextDataFormat.UnicodeText); return true; } } catch { } return false; }
        // Builds the whole batch as one SendInput call: Windows inserts it atomically, and the per-call cost is paid once per batch instead of once per character.
        // Characters that would need Shift, Ctrl, or Alt are sent as Unicode instead, so typing never presses a modifier: rapid Shift taps
        // trigger the Sticky Keys prompt (also on RDP hosts), and synthesized modifiers can combine with keys into application shortcuts.
        // Home, End, and Delete are extended keys: without the flag and scan code, RDP clients map them to numpad keys when NumLock is on.
        // In remote windows every character is sent as Unicode. A key travels as a scan code that the remote turns back into a character with
        // its own layout, and a remote client inside a remote session (mRemoteNG inside an RDCMan session) delivered such keys twice: "Out-Null"
        // arrived as "Out-Nulult-ull", where only the two Unicode characters (O and N) arrived once.
        // A successful batch marks input as available again, so one rejection (UAC prompt, lock screen) never blocks later transfers. When
        // Windows inserts only part of a batch, the keys it left pressed are released and the exception reports exactly how many characters
        // reached the target, so Resume neither skips nor repeats any.
        public bool SendText(string text, int start, int length, CodeModeKeys codeMode, bool remote)
        {
            if (length == 0) return true;
            List<TypedKey> keys=CodeTyping.Expand(text,start,length,codeMode); bool allUnicode=config.TypeAllAsUnicode || remote, shiftedUnicode=allUnicode || config.TypeShiftedAsUnicode; IntPtr layout=TargetLayout();
            INPUT[] inputs=new INPUT[keys.Count*(shiftedUnicode?2:8)]; int[] characterDown=new int[length]; int count=0;
            for (int i=0; i<keys.Count; i++)
            {
                TypedKey key=keys[i];
                switch (key.Action)
                {
                    case KeyAction.Enter: characterDown[key.Index]=count; AddKeyPair(inputs,ref count,0x0D,0x1C,0); break;
                    case KeyAction.Tab: characterDown[key.Index]=count; AddKeyPair(inputs,ref count,0x09,0x0F,0); break;
                    case KeyAction.Escape: AddKeyPair(inputs,ref count,0x1B,0x01,0); break;
                    case KeyAction.Home: AddKeyPair(inputs,ref count,0x24,0x47,KEYEVENTF_EXTENDEDKEY); break;
                    case KeyAction.End: AddKeyPair(inputs,ref count,0x23,0x4F,KEYEVENTF_EXTENDEDKEY); break;
                    case KeyAction.Delete: AddKeyPair(inputs,ref count,0x2E,0x53,KEYEVENTF_EXTENDEDKEY); break;
                    case KeyAction.Backspace: AddKeyPair(inputs,ref count,0x08,0x0E,0); break;
                    default:
                        char ch=key.Character; short mapped=allUnicode ? (short)-1 : VkKeyScanEx(ch,layout);
                        // Code mode's dismissal space shares its line break's index; only the line break itself counts as the character's key.
                        bool own=key.Character==text[start+key.Index];
                        if (mapped != -1 && (mapped & 0xff00) == 0) { if (own) characterDown[key.Index]=count; AddVirtualKeyPair(inputs,ref count,(ushort)(mapped & 0xff),layout); }
                        else if (mapped != -1 && !shiftedUnicode && (mapped & 0xf800) == 0 && ch >= ' ' && ch != '\u007f') { int down=AddMappedKey(inputs,ref count,mapped,layout); if (own) characterDown[key.Index]=down; }
                        else { if (own) characterDown[key.Index]=count; AddKeyPair(inputs,ref count,0,ch,KEYEVENTF_UNICODE); }
                        break;
                }
            }
            int inserted=(int)SendInputBatch((uint)count,inputs,Marshal.SizeOf(typeof(INPUT)));
            if (inserted==count) { inputAvailable=true; return true; }
            Win32Exception error=InputError("keyboard input");
            if (inserted>0 && (inputs[inserted-1].ki.dwFlags & KEYEVENTF_KEYUP)==0) { INPUT up=inputs[inserted-1]; up.ki.dwFlags|=KEYEVENTF_KEYUP; SendInput(1,ref up,Marshal.SizeOf(typeof(INPUT))); }
            if (!shiftedUnicode) { SendKey(0x10,true); SendKey(0x11,true); SendKey(0x12,true); }
            inputAvailable=false; int sent=0; while (sent<length && characterDown[sent]<inserted) sent++;
            throw new InputRejectedException(error.Message,sent);
        }
        // Shift/Ctrl/Alt chords for characters the layout maps to modified keys; used only when typeShiftedAsUnicode is off (rapid Shift taps
        // can trigger the Sticky Keys prompt and synthesized modifiers can combine into application shortcuts).
        private static int AddMappedKey(INPUT[] inputs, ref int count, short mapped, IntPtr layout) { ushort key=(ushort)(mapped & 0xff); int held=(mapped >> 8) & 0x07; ushort[] modifiers={0x10,0x11,0x12}; uint flags; for (int b=0; b<3; b++) if ((held & (1<<b))!=0) inputs[count++]=CreateInput(modifiers[b],ScanCode(modifiers[b],layout,out flags),flags); int down=count; AddVirtualKeyPair(inputs,ref count,key,layout); for (int b=2; b>=0; b--) if ((held & (1<<b))!=0) inputs[count++]=CreateInput(modifiers[b],ScanCode(modifiers[b],layout,out flags),flags|KEYEVENTF_KEYUP); return down; }
        // Simulated keys carry the layout's scan code, as physical keys do: programs that read scan codes (remote clients, virtual machine
        // consoles, games) otherwise receive key 0 and must guess. Keys whose scan code has an E0/E1 prefix are flagged as extended.
        private static ushort ScanCode(ushort vk, IntPtr layout, out uint flags) { uint scan=MapVirtualKeyEx(vk,4,layout); flags=(scan & 0xFF00)!=0 ? KEYEVENTF_EXTENDEDKEY : 0; return (ushort)(scan & 0xFF); }
        private static void AddVirtualKeyPair(INPUT[] inputs, ref int count, ushort vk, IntPtr layout) { uint flags; ushort scan=ScanCode(vk,layout,out flags); AddKeyPair(inputs,ref count,vk,scan,flags); }
        // Characters are mapped with the destination's layout, not Paster's: the destination turns the virtual keys back into characters with
        // its own layout, and with per-app layouts the two can differ (the US ';' key types 'ö' on a German layout).
        private static IntPtr TargetLayout() { uint pid; IntPtr layout=GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(),out pid)); return layout!=IntPtr.Zero ? layout : GetKeyboardLayout(0); }
        private static void AddKeyPair(INPUT[] inputs, ref int count, ushort key, ushort scan, uint flags) { inputs[count++]=CreateInput(key,scan,flags); inputs[count++]=CreateInput(key,scan,flags|KEYEVENTF_KEYUP); }
        // Windows 11 ignores timer-resolution requests from processes without visible windows unless they opt out of power throttling.
        private static void DisablePowerThrottling() { PROCESS_POWER_THROTTLING_STATE state=new PROCESS_POWER_THROTTLING_STATE(); state.Version=1; state.ControlMask=0x1|0x4; state.StateMask=0; try { SetProcessInformation(GetCurrentProcess(),4,ref state,Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE))); } catch (EntryPointNotFoundException) { } }
        // The default ~15.6 ms timer tick would stretch every batch delay; request 1 ms only while a transfer runs.
        public static void SetHighResolutionTimer(bool enabled) { if (enabled) timeBeginPeriod(1); else timeEndPeriod(1); }
        private static INPUT CreateInput(ushort key, ushort scan, uint flags) { INPUT input=new INPUT(); input.type=1; input.ki.wVk=key; input.ki.wScan=scan; input.ki.dwFlags=flags; return input; }
        private static Win32Exception InputError(string kind) { int code=Marshal.GetLastWin32Error(); return new Win32Exception(code, "Windows rejected "+kind+" (error "+code+")"); }
        private bool SendKey(ushort key, bool release) { uint flags; INPUT i=new INPUT(); i.type=1; i.ki.wVk=key; i.ki.wScan=ScanCode(key,TargetLayout(),out flags); i.ki.dwFlags=flags|(release ? KEYEVENTF_KEYUP : 0); bool sent=SendInput(1,ref i,Marshal.SizeOf(typeof(INPUT))) == 1; inputAvailable=sent; return sent; }
        private bool ReleaseModifiers() { WaitWhile(ModifiersDown,5000); return !ModifiersDown(); }
        private static void WaitWhile(Func<bool> condition, int timeoutMilliseconds) { Stopwatch elapsed=Stopwatch.StartNew(); while (elapsed.ElapsedMilliseconds < timeoutMilliseconds && condition()) Thread.Sleep(5); }
        private static bool ModifiersDown() { return (GetAsyncKeyState(0x11)&0x8000)!=0 || (GetAsyncKeyState(0x12)&0x8000)!=0 || (GetAsyncKeyState(0x10)&0x8000)!=0 || (GetAsyncKeyState(0x5B)&0x8000)!=0 || (GetAsyncKeyState(0x5C)&0x8000)!=0; }
        private bool UserKeyDown() { Paster.Core.Shortcut paste=Paster.Core.Shortcut.Parse(config.PasteShortcut); for (int key = 1; key <= 0xFE; key++) { if (IsMouseButton(key) || key == (int)paste.Key || (key == 0x10 || key == 0xA0 || key == 0xA1) && (paste.Modifiers & MOD_SHIFT) != 0 || (key == 0x11 || key == 0xA2 || key == 0xA3) && (paste.Modifiers & MOD_CONTROL) != 0 || (key == 0x12 || key == 0xA4 || key == 0xA5) && (paste.Modifiers & MOD_ALT) != 0 || (key == 0x5B || key == 0x5C) && (paste.Modifiers & MOD_WIN) != 0) continue; if ((GetAsyncKeyState(key) & 0x8000) != 0) return true; } return false; }
        private static bool IsMouseButton(int key) { return key==0x01 || key==0x02 || key==0x04 || key==0x05 || key==0x06; }
        // Only the "currently down" bit: the "pressed since last call" bit is shared by every process, so the click that focused the target
        // could survive into the transfer and cancel it before the first character. Checks run every few milliseconds, well within a click.
        private static bool MouseButtonDown() { return (GetAsyncKeyState(0x01)&0x8000)!=0 || (GetAsyncKeyState(0x02)&0x8000)!=0 || (GetAsyncKeyState(0x04)&0x8000)!=0 || (GetAsyncKeyState(0x05)&0x8000)!=0 || (GetAsyncKeyState(0x06)&0x8000)!=0; }
        // A failure is recorded once, however often the same shortcut fails again (the Ctrl+C capture shortcut is re-registered after every capture).
        private bool Register(string value, int id) { Paster.Core.Shortcut s=Paster.Core.Shortcut.Parse(value); shortcuts[id]=s; if (RegisterHotKey(Handle,id,s.Modifiers|MOD_NOREPEAT,s.Key)) return true; string error="Shortcut "+value+" could not be registered (another program may use it)."; if (String.IsNullOrEmpty(hotkeyError)) hotkeyError=error; else if (hotkeyError.IndexOf(error,StringComparison.Ordinal)<0) hotkeyError+=" "+error; return false; }
        // RegisterHotKey fires only when the key arrives after all modifiers. This hook covers the other order:
        // the key is already held and the final modifier completes the chord. Injected input (our own typing) is ignored.
        private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0) { KBDLLHOOKSTRUCT k=(KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam,typeof(KBDLLHOOKSTRUCT)); int msg=wParam.ToInt32(); bool down=msg==WM_KEYDOWN||msg==WM_SYSKEYDOWN; if ((k.flags & LLKHF_INJECTED)==0 && (down||msg==WM_KEYUP||msg==WM_SYSKEYUP)) { MarkInput(); if (down) physicalKeyPresses++; int vk=(int)k.vkCode; uint mods=CurrentModifiers(vk,down); if (down) TrackRemoteCopy(vk,mods); if (vk==0x56 && HandleWindowsPaste(down,mods)) return (IntPtr)1; bool modifierPressed=down && ModifierFlag(vk)!=0 && config.AnyOrderShortcuts; for (int id=HOT_CAPTURE; id<=HOT_CANCEL; id++) { Paster.Core.Shortcut s=shortcuts[id]; if (s==null) continue; bool complete=mods==s.Modifiers && IsDown((int)s.Key,vk,down); if (!complete) { latched[id]=false; continue; } if (latched[id]) continue; latched[id]=true; if (modifierPressed) PostMessage(Handle,WM_HOTKEY,(IntPtr)id,IntPtr.Zero); } } }
            return CallNextHookEx(hook,code,wParam,lParam);
        }
        // A fresh Ctrl+V into a listed app (Remote Desktop by default) is swallowed and typed instead, but only while the clipboard holds text
        // copied locally; everywhere else, after a copy inside the remote session, and in a listed app running as administrator (Paster
        // cannot type there, but the native paste still works), it stays a normal paste. The swallowed press (repeats
        // and key-up included) never reaches the remote or the async key state, so windowsPasteHeld tracks it until the physical key-up.
        private bool HandleWindowsPaste(bool down, uint mods) { if (!down) { pasteKeyDown=false; if (!windowsPasteHeld) return false; windowsPasteHeld=false; return true; } bool repeat=pasteKeyDown; pasteKeyDown=true; if (windowsPasteHeld) return true; if (repeat || !config.TypeOnCtrlV || mods!=MOD_CONTROL || !typeClipboardOnPaste || !IsTypeOnPasteTarget() || IsInputBlocked(GetForegroundWindow())) return false; windowsPasteHeld=true; PostMessage(Handle,WM_HOTKEY,(IntPtr)HOT_WINDOWS_PASTE,IntPtr.Zero); return true; }
        // A physical copy or cut inside the remote session means the user now wants the remote clipboard there, not the local text.
        // Until a copy made inside the remote window reaches the local clipboard, Ctrl+V there stays native: typing now would type older local text.
        private void TrackRemoteCopy(int vk, uint mods) { if ((mods==MOD_CONTROL && (vk==0x43 || vk==0x58 || vk==0x2D) || mods==MOD_SHIFT && vk==0x2E) && IsTypeOnPasteTarget()) typeClipboardOnPaste=false; }
        // Text copied locally is typed. Text arriving while a listed app is in front is a redirected remote copy: typed too when TypeRemoteCopies
        // is on (a nested session inside it may block paste, and Paster cannot see which window inside the session has focus), otherwise native.
        // Paster's own republished copy (paste normally first) is the same text and changes nothing.
        private void OnClipboardUpdate() { if (GetClipboardOwner()==Handle) return; typeClipboardOnPaste=IsClipboardFormatAvailable(CF_UNICODETEXT) && (config.TypeRemoteCopies || !IsTypeOnPasteTarget()); }
        // Runs on the transfer worker. The dialog is topmost and forced to the foreground (the paste keypress went to another app); it closes as
        // Cancel on the cancel shortcut or after the prompt timeout, so a dialog hidden behind a full-screen client cannot block Paster indefinitely.
        // After a non-cancel answer the target is reactivated and the keys or buttons used to answer are released before typing can start.
        public TransferChoice ConfirmTransfer(TransferPrompt prompt)
        {
            TransferChoice choice=TransferChoice.Cancel;
            using (Form f=new Form())
            {
                f.Text="Paster"; f.FormBorderStyle=FormBorderStyle.FixedDialog; f.StartPosition=FormStartPosition.CenterScreen; f.TopMost=true; f.ShowInTaskbar=false; f.MinimizeBox=false; f.MaximizeBox=false; f.ClientSize=new System.Drawing.Size(460,150);
                Label text=new Label(); text.Text=prompt.Message; text.Left=15; text.Top=15; text.AutoSize=true; text.MaximumSize=new System.Drawing.Size(430,0); f.Controls.Add(text);
                Button primary=new Button(), cancel=new Button(); primary.Text=prompt.CanResume?"&Resume":"&Type"; primary.DialogResult=prompt.CanResume?DialogResult.Yes:DialogResult.OK; cancel.Text="Cancel"; cancel.DialogResult=DialogResult.Cancel;
                Button[] buttons=prompt.CanResume ? new Button[] { primary, new Button(), cancel } : new Button[] { primary, cancel }; if (prompt.CanResume) { buttons[1].Text="&Start over"; buttons[1].DialogResult=DialogResult.OK; }
                int buttonTop=Math.Max(110,text.PreferredHeight+30); f.ClientSize=new System.Drawing.Size(460,buttonTop+40);
                for (int i=0; i<buttons.Length; i++) { buttons[i].Width=95; buttons[i].Top=buttonTop; buttons[i].Left=f.ClientSize.Width-15-(buttons.Length-i)*105+10; f.Controls.Add(buttons[i]); }
                f.AcceptButton=primary; f.CancelButton=cancel; f.Shown+=delegate { ForceForeground(f.Handle); primary.Focus(); };
                System.Windows.Forms.Timer expiry=new System.Windows.Forms.Timer(); expiry.Interval=1000; int remaining=config.PromptTimeoutSeconds; expiry.Tick+=delegate { if (prompt.Cancellation.IsCancellationRequested || --remaining<=0) { expiry.Stop(); f.DialogResult=DialogResult.Cancel; } }; expiry.Start();
                DialogResult result; try { result=f.ShowDialog(); } finally { expiry.Dispose(); } choice=result==DialogResult.Yes ? TransferChoice.Resume : result==DialogResult.OK ? TransferChoice.Start : TransferChoice.Cancel;
            }
            if (choice==TransferChoice.Cancel) return choice;
            WaitWhile(AnswerKeysDown,5000);
            ForceForeground(prompt.TargetWindow); WaitWhile(delegate { return GetForegroundWindow()!=prompt.TargetWindow; },1000);
            SetInputBaseline(); return choice;
        }
        private static bool AnswerKeysDown() { int[] keys={0x01,0x02,0x04,0x0D,0x20,0x12,0x52,0x53,0x54}; for (int i=0; i<keys.Length; i++) if ((GetAsyncKeyState(keys[i])&0x8000)!=0) return true; return false; }
        // Windows only lets the process that received the last input take the foreground; briefly sharing the foreground thread's input state lifts that.
        private static void ForceForeground(IntPtr window) { uint ignored; uint foregroundThread=GetWindowThreadProcessId(GetForegroundWindow(),out ignored), current=GetCurrentThreadId(); bool attached=foregroundThread!=0 && foregroundThread!=current && AttachThreadInput(current,foregroundThread,true); try { SetForegroundWindow(window); BringWindowToTop(window); } finally { if (attached) AttachThreadInput(current,foregroundThread,false); } }
        private bool IsTypeOnPasteTarget() { return IsRemoteWindow(GetForegroundWindow()); }
        public bool IsRemoteWindow(IntPtr window) { return config.IsTypeOnPasteApp(ProcessPath(window),WindowTitle(window)); }
        public bool IsInputBlocked(IntPtr window) { return TargetIntegrity.IsInputBlocked(window); }
        // The program's own description ("Microsoft Teams", "Visual Studio Code") rather than the window title, which can contain document
        // names or message subjects that do not belong in a notification.
        public string DescribeWindow(IntPtr window) { string path=ProcessPath(window); if (path==null) return null; try { string description=FileVersionInfo.GetVersionInfo(path).FileDescription; if (!String.IsNullOrWhiteSpace(description)) return description.Trim(); } catch { } return System.IO.Path.GetFileNameWithoutExtension(path); }
        // ---- Paste normally first ----
        // Paster republishes the clipboard content with delayed rendering: Windows then asks Paster for the data at the moment a program reads
        // it, and a read by the remote-desktop client proves that a paste inside its session (or a session nested in it) reached here. Every
        // format is copied faithfully (plain data byte for byte, bitmaps and metafiles as copies of the picture), so nothing is lost.
        private const int WM_RENDERFORMAT=0x0305, WM_RENDERALLFORMATS=0x0306, WM_DESTROYCLIPBOARD=0x0307, WM_PUBLISH_CLIPBOARD=0x8003, NormalPasteTimeoutMilliseconds=3000, AnnounceMilliseconds=300;
        private const uint CF_TEXT=1, CF_OEMTEXT=7, CF_PALETTE=9, CF_LOCALE=16, CF_OWNERDISPLAY=0x80;
        private static readonly uint HistoryFormat=RegisterClipboardFormat("CanIncludeInClipboardHistory"), CloudFormat=RegisterClipboardFormat("CanUploadToCloudClipboard"), ExcludeFormat=RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), ViewerIgnoreFormat=RegisterClipboardFormat("Clipboard Viewer Ignore"), OlePrivateFormat=RegisterClipboardFormat("Ole Private Data"), OleObjectFormat=RegisterClipboardFormat("DataObject");
        private ClipboardSnapshot published, toPublish, republishing; private volatile int readerToWatch, lastReader; private volatile bool stopping;
        private readonly ManualResetEvent clipboardRead=new ManualResetEvent(false), normalPasteCancelled=new ManualResetEvent(false);
        // The process that last read content Paster republished (diagnostics).
        public int LastClipboardReader { get { return lastReader; } }
        public bool IsPasteFirstWindow(IntPtr window) { return config.PasteNormallyFirst && config.IsPasteFirstApp(ProcessPath(window),WindowTitle(window)); }
        public bool IsPublished(ClipboardSnapshot snapshot) { return snapshot!=null && Object.ReferenceEquals(published,snapshot); }
        public static bool IsWindowAlive(IntPtr window) { return IsWindow(window); }
        // The window that has the keyboard focus inside the target, which identifies the session: clients showing several sessions in one
        // window (RDCMan, mRemoteNG, Royal TS) give each its own remote desktop control, so a blocked session does not make the others type.
        // Paster still cannot see into a session, so a session nested inside it shares its result.
        public IntPtr SessionWindow(IntPtr target)
        {
            uint pid; uint thread=GetWindowThreadProcessId(target,out pid); if (thread==0) return target;
            GUITHREADINFO info=new GUITHREADINFO(); info.cbSize=Marshal.SizeOf(typeof(GUITHREADINFO));
            return GetGUIThreadInfo(thread,ref info) && info.hwndFocus!=IntPtr.Zero && (info.hwndFocus==target || IsChild(target,info.hwndFocus)) ? info.hwndFocus : target;
        }
        // Text: the content can be republished faithfully. Private: the source asked clipboard tools to keep no copy (password managers), so it
        // is never republished. Other: formats only their own program understands (private handles), or more than the size limit, or (with
        // plainTextOnly) anything beyond plain text, which is then left untouched without a single format being read.
        public ClipboardKind ReadClipboard(bool plainTextOnly, out ClipboardSnapshot snapshot)
        {
            snapshot=null; ClipboardSnapshot own=published; if (own!=null && GetClipboardOwner()==Handle) { snapshot=own; return ClipboardKind.Text; }
            if (IsClipboardFormatAvailable(ExcludeFormat) || IsClipboardFormatAvailable(ViewerIgnoreFormat)) return ClipboardKind.Private;
            if (!OpenClipboardWithRetry(IntPtr.Zero)) return ClipboardKind.Other;
            ClipboardSnapshot result=new ClipboardSnapshot();
            try
            {
                if (IsClipboardFormatAvailable(HistoryFormat) && ReadClipboardDword(HistoryFormat)==0) return ClipboardKind.Private;
                if (plainTextOnly) for (uint format=EnumClipboardFormats(0); format!=0; format=EnumClipboardFormats(format)) if (!IsBookkeeping(format) && format!=CF_UNICODETEXT && format!=CF_TEXT && format!=CF_OEMTEXT && format!=CF_LOCALE) return ClipboardKind.Other;
                for (uint format=EnumClipboardFormats(0); format!=0; format=EnumClipboardFormats(format))
                {
                    if (IsBookkeeping(format)) continue;
                    if (format>=0x200 && format<=0x3FF) return ClipboardKind.Other;
                    // A format the source cannot provide is left out; the others still paste.
                    IntPtr handle=GetClipboardData(format); if (handle==IntPtr.Zero) continue;
                    if (!result.Add(format,handle)) return ClipboardKind.Other;
                }
                if (!result.Contains(CF_UNICODETEXT)) return ClipboardKind.Other;
                snapshot=result; result=null; return ClipboardKind.Text;
            }
            finally { CloseClipboard(); if (result!=null) result.Dispose(); }
        }
        // Not content to paste: OLE's bookkeeping, which only the program that copied can use (OLE treats the republished content as plain
        // clipboard data without it), display-only formats, and the clipboard history markers, which Paster sets itself when republishing.
        private static bool IsBookkeeping(uint format) { return format==OlePrivateFormat || format==OleObjectFormat || format==CF_PALETTE || format==CF_OWNERDISPLAY || format==HistoryFormat || format==CloudFormat; }
        // Whether the clipboard content was copied inside the window's program: a copy made in a remote session reaches this PC through the
        // remote desktop program, which then owns the clipboard.
        public bool IsClipboardFrom(IntPtr window) { IntPtr owner=GetClipboardOwner(); if (owner==IntPtr.Zero || owner==Handle) return false; uint ownerPid, windowPid; GetWindowThreadProcessId(owner,out ownerPid); GetWindowThreadProcessId(window,out windowPid); return ownerPid!=0 && ownerPid==windowPid; }
        // Republishes the content, sends Ctrl+V to the target, and waits for its process to read it. Only that process counts: clipboard
        // managers and other programs may read the clipboard too, and while the paste is watched they get nothing.
        public NormalPasteResult TryNormalPaste(IntPtr target, ClipboardSnapshot snapshot)
        {
            uint pid; GetWindowThreadProcessId(target,out pid); if (pid==0 || snapshot==null) return NormalPasteResult.NotSent;
            normalPasteCancelled.Reset(); clipboardRead.Reset(); if (stopping) return NormalPasteResult.Cancelled;
            // Watched from before the republish, so no other reader can take the first copy of the data.
            readerToWatch=(int)pid;
            try
            {
                if (!PublishClipboard(snapshot)) return NormalPasteResult.NotSent;
                // The client announces the republished content to its session (and on to nested sessions). After an earlier paste a session
                // holds its own copy, and a Ctrl+V arriving before the announcement would paste that copy without asking: the paste would work
                // but look blocked. The head start lets the announcement arrive first.
                if (normalPasteCancelled.WaitOne(AnnounceMilliseconds)) return NormalPasteResult.Cancelled;
                if (GetForegroundWindow()!=target) return NormalPasteResult.FocusLost;
                if (!SendPasteShortcut()) return NormalPasteResult.NotSent;
                switch (WaitHandle.WaitAny(new WaitHandle[] { clipboardRead, normalPasteCancelled },NormalPasteTimeoutMilliseconds)) { case 0: return NormalPasteResult.Read; case 1: return NormalPasteResult.Cancelled; default: return NormalPasteResult.NotRead; }
            }
            finally { readerToWatch=0; }
        }
        // The physical Ctrl+V was swallowed; once its keys are released, a clean Ctrl+V reaches the window (Paster's hook ignores injected keys).
        public bool SendPasteShortcut() { bool control=SendKey(0x11,false), v=control && SendKey(0x56,false); if (v) SendKey(0x56,true); if (control) SendKey(0x11,true); return v; }
        public void CancelNormalPaste() { normalPasteCancelled.Set(); }
        public void StopNormalPaste() { stopping=true; normalPasteCancelled.Set(); }
        // The clipboard window lives on the window thread, which therefore owns the republished content and answers the read requests.
        public bool PublishClipboard(ClipboardSnapshot snapshot)
        {
            for (int attempt=0; attempt<15; attempt++)
            {
                toPublish=snapshot; IntPtr result;
                if (SendMessageTimeout(Handle,WM_PUBLISH_CLIPBOARD,IntPtr.Zero,IntPtr.Zero,0,1000,out result)!=IntPtr.Zero && result!=IntPtr.Zero) return true;
                Thread.Sleep(20);
            }
            return false;
        }
        // One attempt only: this runs on the window thread, which also runs the keyboard hook and must not wait for a busy clipboard.
        private bool PublishNow(ClipboardSnapshot snapshot)
        {
            if (snapshot==null || !OpenClipboard(Handle)) return false;
            republishing=snapshot;
            try
            {
                if (!EmptyClipboard()) return false;
                published=snapshot; // after EmptyClipboard: its WM_DESTROYCLIPBOARD has already released an earlier Paster copy
                for (int i=0; i<snapshot.Count; i++) SetClipboardData(snapshot.FormatAt(i),IntPtr.Zero);
                // The original copy is already in the clipboard history where its source allowed it; this republished copy never appears there,
                // or in the cloud clipboard, a second time.
                SetClipboardBytes(HistoryFormat,BitConverter.GetBytes(0)); SetClipboardBytes(CloudFormat,BitConverter.GetBytes(0));
                return true;
            }
            finally { republishing=null; CloseClipboard(); }
        }
        // Someone else took the clipboard: Paster's copy is released, unless it is the very copy being republished right now.
        private void ReleasePublished() { ClipboardSnapshot old=published; published=null; if (old!=null && !Object.ReferenceEquals(old,republishing)) old.Dispose(); }
        // Runs when a program reads a republished format. While a normal paste is watched, only the target's own process gets the data:
        // a clipboard manager reading first would turn the format into plain data, and the target's read would go unnoticed.
        private void RenderFormat(uint format)
        {
            IntPtr reader=GetOpenClipboardWindow(); uint pid=0; if (reader!=IntPtr.Zero) GetWindowThreadProcessId(reader,out pid);
            lastReader=(int)pid; int watched=readerToWatch;
            if (watched!=0 && pid!=(uint)watched) return;
            ClipboardSnapshot snapshot=published; if (snapshot!=null) { IntPtr data=snapshot.Render(format); if (data!=IntPtr.Zero && SetClipboardData(format,data)==IntPtr.Zero) ClipboardSnapshot.Free(format,data); }
            if (watched!=0) clipboardRead.Set();
        }
        // Paster is closing while it still owns the clipboard: everything is rendered so the content stays available.
        private void RenderAllFormats() { ClipboardSnapshot snapshot=published; if (snapshot==null || !OpenClipboard(Handle)) return; try { if (GetClipboardOwner()==Handle) for (int i=0; i<snapshot.Count; i++) { uint format=snapshot.FormatAt(i); IntPtr data=snapshot.Render(format); if (data!=IntPtr.Zero && SetClipboardData(format,data)==IntPtr.Zero) ClipboardSnapshot.Free(format,data); } } finally { CloseClipboard(); } }
        private static bool OpenClipboardWithRetry(IntPtr owner) { for (int attempt=0; attempt<25; attempt++) { if (OpenClipboard(owner)) return true; Thread.Sleep(20); } return false; }
        private static void SetClipboardBytes(uint format, byte[] data) { IntPtr handle=ClipboardSnapshot.ToGlobal(data); if (handle!=IntPtr.Zero && SetClipboardData(format,handle)==IntPtr.Zero) GlobalFree(handle); }
        private static int ReadClipboardDword(uint format) { IntPtr handle=GetClipboardData(format); if (handle==IntPtr.Zero || GlobalSize(handle).ToUInt64()<4) return -1; IntPtr memory=GlobalLock(handle); if (memory==IntPtr.Zero) return -1; try { return Marshal.ReadInt32(memory); } finally { GlobalUnlock(handle); } }
        [StructLayout(LayoutKind.Sequential)] private struct GUITHREADINFO { public int cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public int left, top, right, bottom; }
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread,ref GUITHREADINFO info); [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent,IntPtr window);
        private static string WindowTitle(IntPtr window) { StringBuilder title=new StringBuilder(512); return GetWindowText(window,title,title.Capacity)>0 ? title.ToString() : null; }
        private static string ProcessPath(IntPtr window) { uint pid; GetWindowThreadProcessId(window,out pid); if (pid==0) return null; IntPtr process=OpenProcess(0x1000,false,pid); if (process==IntPtr.Zero) return null; try { StringBuilder path=new StringBuilder(1024); int size=path.Capacity; return QueryFullProcessImageName(process,0,path,ref size) ? path.ToString() : null; } finally { CloseHandle(process); } }
        private static uint CurrentModifiers(int vk, bool down) { return (IsDown(0xA2,vk,down)||IsDown(0xA3,vk,down)?MOD_CONTROL:0)|(IsDown(0xA0,vk,down)||IsDown(0xA1,vk,down)?MOD_SHIFT:0)|(IsDown(0xA4,vk,down)||IsDown(0xA5,vk,down)?MOD_ALT:0)|(IsDown(0x5B,vk,down)||IsDown(0x5C,vk,down)?MOD_WIN:0); }
        private static uint ModifierFlag(int vk) { if (vk==0x11||vk==0xA2||vk==0xA3) return MOD_CONTROL; if (vk==0x10||vk==0xA0||vk==0xA1) return MOD_SHIFT; if (vk==0x12||vk==0xA4||vk==0xA5) return MOD_ALT; if (vk==0x5B||vk==0x5C) return MOD_WIN; return 0; }
        private static bool IsDown(int key, int vk, bool down) { return key==vk ? down : (GetAsyncKeyState(key)&0x8000)!=0; }
        protected override void WndProc(ref Message m) { if (m.Msg==WM_RENDERFORMAT) { RenderFormat((uint)m.WParam.ToInt64()); m.Result=IntPtr.Zero; return; } if (m.Msg==WM_RENDERALLFORMATS) { RenderAllFormats(); m.Result=IntPtr.Zero; return; } if (m.Msg==WM_DESTROYCLIPBOARD) { ReleasePublished(); m.Result=IntPtr.Zero; return; } if (m.Msg==WM_PUBLISH_CLIPBOARD) { m.Result=(IntPtr)(PublishNow(toPublish)?1:0); return; } if (m.Msg==WM_WATCH_MOUSE) { SetMouseHook(m.WParam!=IntPtr.Zero); m.Result=(IntPtr)1; return; } if (m.Msg==WM_CAPTURE_HOTKEY) { bool ok=true; if (m.WParam!=IntPtr.Zero) ok=Register(config.CaptureShortcut,HOT_CAPTURE); else UnregisterHotKey(Handle,HOT_CAPTURE); /* on this thread it fails only when the shortcut was not registered, which also lets the copy through */ m.Result=(IntPtr)(ok?1:0); return; } if (m.Msg==WM_CLIPBOARDUPDATE) OnClipboardUpdate(); else if (m.Msg==WM_TIMER && m.WParam.ToInt64()==HOOK_TIMER || m.Msg==WM_POWERBROADCAST && m.WParam.ToInt64()==PBT_APMRESUMEAUTOMATIC) InstallHook(); else if (m.Msg==WM_WTSSESSION_CHANGE) { long reason=m.WParam.ToInt64(); if (reason==WTS_SESSION_UNLOCK || reason==WTS_CONSOLE_CONNECT || reason==WTS_REMOTE_CONNECT) InstallHook(); } else if (m.Msg==WM_HOTKEY) { int id=m.WParam.ToInt32(); if(id==HOT_CAPTURE && CaptureRequested!=null) CaptureRequested(); else if((id==HOT_PASTE || id==HOT_WINDOWS_PASTE) && PasteRequested!=null) PasteRequested(id==HOT_WINDOWS_PASTE); else if(id==HOT_CANCEL && CancelRequested!=null) CancelRequested(); } base.WndProc(ref m); }
        public void Dispose() { StopNormalPaste(); KillTimer(Handle,(UIntPtr)HOOK_TIMER); SetMouseHook(false); WTSUnRegisterSessionNotification(Handle); RemoveClipboardFormatListener(Handle); if (hook!=IntPtr.Zero) { UnhookWindowsHookEx(hook); hook=IntPtr.Zero; } UnregisterHotKey(Handle,HOT_CAPTURE); UnregisterHotKey(Handle,HOT_PASTE); UnregisterHotKey(Handle,HOT_CANCEL); DestroyHandle(); }
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public KEYBDINPUT ki; private uint padding1; private uint padding2; } [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk,wScan; public uint dwFlags,time; public UIntPtr dwExtraInfo; }
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd,out uint processId); [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access,bool inherit,uint processId); [DllImport("kernel32.dll", CharSet=CharSet.Unicode, EntryPoint="QueryFullProcessImageNameW")] private static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref int size); [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd,StringBuilder text,int maxCount); [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hWnd); [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hWnd); [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd); [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd); [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach,uint attachTo,bool enable); [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [StructLayout(LayoutKind.Sequential)] private struct PROCESS_POWER_THROTTLING_STATE { public uint Version,ControlMask,StateMask; }
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess(); [DllImport("kernel32.dll")] private static extern bool SetProcessInformation(IntPtr process,int informationClass,ref PROCESS_POWER_THROTTLING_STATE information,int size); [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period); [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
        [StructLayout(LayoutKind.Sequential)] private struct KBDLLHOOKSTRUCT { public uint vkCode,scanCode,flags,time; public UIntPtr dwExtraInfo; } private delegate IntPtr LowLevelKeyboardProc(int code,IntPtr wParam,IntPtr lParam);
        [DllImport("user32.dll", SetLastError=true)] private static extern IntPtr SetWindowsHookEx(int idHook,LowLevelKeyboardProc proc,IntPtr module,uint threadId); [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook); [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wParam,IntPtr lParam); [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name); [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd,uint msg,IntPtr wParam,IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd,int id,uint fsModifiers,uint vk); [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd,int id); [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint n,ref INPUT i,int size); [DllImport("user32.dll", EntryPoint="SendInput", SetLastError=true)] private static extern uint SendInputBatch(uint n,[In] INPUT[] inputs,int size); [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey); [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber(); [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern short VkKeyScanEx(char ch,IntPtr layout); [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner); [DllImport("user32.dll")] private static extern bool CloseClipboard(); [DllImport("user32.dll")] private static extern bool EmptyClipboard(); [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format); [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format); [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format,IntPtr data); [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner(); [DllImport("user32.dll")] private static extern IntPtr GetOpenClipboardWindow(); [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name); [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags,UIntPtr bytes); [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory); [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory); [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr memory); [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
        [DllImport("user32.dll")] private static extern uint MapVirtualKeyEx(uint code,uint mapType,IntPtr layout);
        [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr hWnd,int msg,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);
        [DllImport("user32.dll")] private static extern UIntPtr SetTimer(IntPtr hWnd,UIntPtr id,uint elapse,IntPtr callback); [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr hWnd,UIntPtr id); [DllImport("wtsapi32.dll")] private static extern bool WTSRegisterSessionNotification(IntPtr hWnd,uint flags); [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);    }

    // Clipboard content copied faithfully, which Paster republishes with delayed rendering to see who reads it. Plain data is kept as bytes;
    // bitmaps and metafiles (pictures from Office and image programs) are kept as Paster's own copies of the picture. Every read gets a fresh
    // copy, because the clipboard takes ownership of what it is given.
    internal sealed class ClipboardSnapshot : IDisposable
    {
        private const uint CF_BITMAP=2, CF_METAFILEPICT=3, CF_ENHMETAFILE=14, CF_DSPBITMAP=0x82, CF_DSPMETAFILEPICT=0x83, CF_DSPENHMETAFILE=0x8E, GMEM_MOVEABLE=2;
        private const long MaximumBytes=128L*1024*1024;
        private readonly List<uint> formats=new List<uint>(); private readonly List<byte[]> bytes=new List<byte[]>(); private readonly List<IntPtr> pictures=new List<IntPtr>(); private readonly List<int[]> metafileSizes=new List<int[]>(); private long total; private bool disposed;
        public int Count { get { return formats.Count; } }
        public uint FormatAt(int index) { return formats[index]; }
        public bool Contains(uint format) { return formats.Contains(format); }
        public byte[] Get(uint format) { int index=formats.IndexOf(format); return index>=0 ? bytes[index] : null; }
        private static bool IsBitmap(uint format) { return format==CF_BITMAP || format==CF_DSPBITMAP; }
        private static bool IsEnhancedMetafile(uint format) { return format==CF_ENHMETAFILE || format==CF_DSPENHMETAFILE; }
        private static bool IsMetafilePicture(uint format) { return format==CF_METAFILEPICT || format==CF_DSPMETAFILEPICT; }
        // Returns false when the content is larger than Paster keeps; a picture it cannot copy is left out, like a format the source refused.
        public bool Add(uint format, IntPtr handle)
        {
            if (IsBitmap(format)) { IntPtr copy=CopyImage(handle,0,0,0,0); if (copy!=IntPtr.Zero) Keep(format,null,copy,null); return true; }
            if (IsEnhancedMetafile(format)) { IntPtr copy=CopyEnhMetaFile(handle,null); if (copy!=IntPtr.Zero) Keep(format,null,copy,null); return true; }
            if (IsMetafilePicture(format))
            {
                IntPtr memory=GlobalLock(handle); if (memory==IntPtr.Zero) return true;
                try { METAFILEPICT picture=(METAFILEPICT)Marshal.PtrToStructure(memory,typeof(METAFILEPICT)); IntPtr copy=CopyMetaFile(picture.hMF,null); if (copy!=IntPtr.Zero) Keep(format,null,copy,new int[] { picture.mm, picture.xExt, picture.yExt }); }
                finally { GlobalUnlock(handle); }
                return true;
            }
            long size=(long)GlobalSize(handle).ToUInt64(); if (size==0) return true;
            total+=size; if (total>MaximumBytes) return false;
            IntPtr data=GlobalLock(handle); if (data==IntPtr.Zero) return true;
            try { byte[] content=new byte[size]; Marshal.Copy(data,content,0,(int)size); Keep(format,content,IntPtr.Zero,null); } finally { GlobalUnlock(handle); }
            return true;
        }
        private void Keep(uint format, byte[] content, IntPtr picture, int[] metafileSize) { formats.Add(format); bytes.Add(content); pictures.Add(picture); metafileSizes.Add(metafileSize); }
        // A fresh handle for the clipboard to own, or zero when the format is not part of the snapshot.
        public IntPtr Render(uint format)
        {
            int index=formats.IndexOf(format); if (index<0 || disposed) return IntPtr.Zero;
            if (bytes[index]!=null) return ToGlobal(bytes[index]);
            if (IsBitmap(format)) return CopyImage(pictures[index],0,0,0,0);
            if (IsEnhancedMetafile(format)) return CopyEnhMetaFile(pictures[index],null);
            IntPtr copy=CopyMetaFile(pictures[index],null); if (copy==IntPtr.Zero) return IntPtr.Zero;
            METAFILEPICT picture=new METAFILEPICT(); picture.mm=metafileSizes[index][0]; picture.xExt=metafileSizes[index][1]; picture.yExt=metafileSizes[index][2]; picture.hMF=copy;
            IntPtr handle=GlobalAlloc(GMEM_MOVEABLE,(UIntPtr)(uint)Marshal.SizeOf(typeof(METAFILEPICT))); IntPtr memory=handle==IntPtr.Zero ? IntPtr.Zero : GlobalLock(handle);
            if (memory==IntPtr.Zero) { if (handle!=IntPtr.Zero) GlobalFree(handle); DeleteMetaFile(copy); return IntPtr.Zero; }
            Marshal.StructureToPtr(picture,memory,false); GlobalUnlock(handle); return handle;
        }
        // Frees a rendered handle the clipboard did not accept.
        public static void Free(uint format, IntPtr handle)
        {
            if (IsBitmap(format)) DeleteObject(handle);
            else if (IsEnhancedMetafile(format)) DeleteEnhMetaFile(handle);
            else if (IsMetafilePicture(format)) { IntPtr memory=GlobalLock(handle); if (memory!=IntPtr.Zero) { DeleteMetaFile(((METAFILEPICT)Marshal.PtrToStructure(memory,typeof(METAFILEPICT))).hMF); GlobalUnlock(handle); } GlobalFree(handle); }
            else GlobalFree(handle);
        }
        public static IntPtr ToGlobal(byte[] data) { IntPtr handle=GlobalAlloc(GMEM_MOVEABLE,(UIntPtr)(uint)data.Length); if (handle==IntPtr.Zero) return IntPtr.Zero; IntPtr memory=GlobalLock(handle); if (memory==IntPtr.Zero) { GlobalFree(handle); return IntPtr.Zero; } Marshal.Copy(data,0,memory,data.Length); GlobalUnlock(handle); return handle; }
        public void Dispose()
        {
            if (disposed) return; disposed=true;
            for (int i=0; i<formats.Count; i++) { if (pictures[i]==IntPtr.Zero) continue; if (IsBitmap(formats[i])) DeleteObject(pictures[i]); else if (IsEnhancedMetafile(formats[i])) DeleteEnhMetaFile(pictures[i]); else DeleteMetaFile(pictures[i]); }
        }
        [StructLayout(LayoutKind.Sequential)] private struct METAFILEPICT { public int mm, xExt, yExt; public IntPtr hMF; }
        [DllImport("user32.dll")] private static extern IntPtr CopyImage(IntPtr image,uint type,int width,int height,uint flags);
        [DllImport("gdi32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr CopyEnhMetaFile(IntPtr metafile,string file);
        [DllImport("gdi32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr CopyMetaFile(IntPtr metafile,string file);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr gdiObject); [DllImport("gdi32.dll")] private static extern bool DeleteEnhMetaFile(IntPtr metafile); [DllImport("gdi32.dll")] private static extern bool DeleteMetaFile(IntPtr metafile);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags,UIntPtr bytes); [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory); [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory); [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr memory); [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
    }
    // Read: the target's process read the text, so the paste reached its session. NotRead: no read within the timeout, the paste is blocked.
    // NotSent: nothing reached the window (the clipboard was busy or input was rejected), so typing cannot double anything. FocusLost and
    // Cancelled: Paster stopped before or while pasting and does nothing more.
    internal enum NormalPasteResult { Read, NotRead, NotSent, FocusLost, Cancelled }

    // Ctrl+V in a remote desktop app (browsers excepted): the session's own paste is tried, and Paster types only where it is blocked.
    internal sealed class PasteFirstFlow
    {
        private readonly WindowsPlatform platform; private readonly TransferCoordinator coordinator; private readonly PasterConfig config; public readonly PasteFirstMemory Checks=new PasteFirstMemory();
        public PasteFirstFlow(WindowsPlatform p, TransferCoordinator c, PasterConfig cfg) { platform=p; coordinator=c; config=cfg; }
        // Returns false when the text should be typed now: the window does not paste normally first, its paste is blocked, or the clipboard
        // content cannot be watched in a window not yet checked. Returns true when Paster pasted normally or deliberately did nothing.
        public bool Run(IntPtr target)
        {
            if (!platform.IsPasteFirstWindow(target)) return false;
            IntPtr session=platform.SessionWindow(target);
            // A session known to block types without the clipboard being inspected at all.
            PasteCheck check=Checks.Get(session,WindowsPlatform.IsWindowAlive); if (check==PasteCheck.Blocked) return false;
            ClipboardSnapshot snapshot=null;
            try
            {
                ClipboardKind kind=platform.ReadClipboard(PasteFirstMemory.PlainTextOnly(check,platform.IsClipboardFrom(target)),out snapshot);
                PasteAction action=PasteFirstMemory.Decide(check,kind);
                if (action==PasteAction.Type) return false;
                if (action==PasteAction.PasteNormally) { if (platform.ForegroundWindow==target) platform.SendPasteShortcut(); return true; }
                switch (platform.TryNormalPaste(target,snapshot))
                {
                    case NormalPasteResult.Read: Checks.Set(session,PasteCheck.Works,WindowsPlatform.IsWindowAlive); return true;
                    case NormalPasteResult.NotRead: Checks.Set(session,PasteCheck.Blocked,WindowsPlatform.IsWindowAlive); coordinator.ReportError(PasteFirstMemory.BlockedMessage(platform.DescribeWindow(target))); return true;
                    case NormalPasteResult.NotSent: return false;
                    default: return true;
                }
            }
            // A copy that never reached the clipboard holds pictures Paster made; they are released here.
            finally { if (snapshot!=null && !platform.IsPublished(snapshot)) snapshot.Dispose(); }
        }
        public string StatusLine { get { return !config.PasteNormallyFirst ? null : String.Format("Paste normally first: works in {0}, blocked in {1} session(s)",Checks.Count(PasteCheck.Works),Checks.Count(PasteCheck.Blocked)); } }
    }

    internal sealed class PasterApplicationContext : ApplicationContext
    {
        private readonly TransferCoordinator coordinator; private readonly PasterConfig config; private readonly string configPath; private readonly WindowsPlatform platform; private readonly ManagementPipe pipe; private readonly Control dispatcher; private readonly object workerGate=new object(); private readonly List<Thread> workers=new List<Thread>(); private long lastTransferFailure; private volatile bool pastePending; private TrayIcon tray; private volatile bool exiting; private readonly PasteFirstFlow pasteFirst;
        public PasterApplicationContext(WindowsPlatform p, TransferCoordinator c, PasterConfig cfg, string path, string startupError)
        {
            platform=p; coordinator=c; config=cfg; configPath=path; dispatcher=new Control();dispatcher.CreateControl(); pasteFirst=new PasteFirstFlow(platform,coordinator,config);
            platform.CaptureRequested += delegate { StartWorker(delegate { try { coordinator.Capture(); } catch(Exception ex) { coordinator.ReportError(ex.Message); } }); };
            platform.PasteRequested += delegate(bool ctrlV) { if (pastePending || coordinator.Status.IsTransferring || TransferFailedRecently()) return; pastePending=true; StartWorker(delegate { WindowsPlatform.SetHighResolutionTimer(true); platform.WatchMouse(true); try { if (!platform.WaitForPasteShortcutRelease()) { coordinator.ReportError("Paste shortcut keys were not released."); return; } if (ctrlV && pasteFirst.Run(platform.ForegroundWindow)) return; if (!coordinator.Transfer(CodeModeKeys.None, config.CodeModeForRemotePaste ? config.CodeModeKeys : CodeModeKeys.None)) Interlocked.Exchange(ref lastTransferFailure,Stopwatch.GetTimestamp()); } catch(Exception ex) { coordinator.ReportError(ex.Message); } finally { platform.WatchMouse(false); pastePending=false; WindowsPlatform.SetHighResolutionTimer(false); } }); };
            platform.CancelRequested += delegate { coordinator.Cancel(); platform.CancelNormalPaste(); };
            coordinator.Failed += Notify;
            UpdateTray(); if (!String.IsNullOrEmpty(startupError)) Notify(startupError);
            pipe=new ManagementPipe(this);
        }
        // A paste shortcut pressed while typing interrupts the transfer; its own hotkey message, handled right after, must not restart it.
        // Stopwatch timestamps are 64-bit, so a failure long ago can never look recent (Environment.TickCount wraps after 24.9 days).
        private bool TransferFailedRecently() { long at=Interlocked.Read(ref lastTransferFailure); return at!=0 && Stopwatch.GetTimestamp()-at < Stopwatch.Frequency; }
        // Failures are otherwise visible only through --status. Notifications need an icon to belong to, so with notifications on and the tray
        // icon off, the icon exists but is shown only while a notification is on screen.
        private void Notify(string message) { if (exiting || dispatcher.IsDisposed) return; try { dispatcher.BeginInvoke((MethodInvoker)delegate { if (!exiting && tray!=null && config.ShowNotifications) tray.ShowError(message); }); } catch(InvalidOperationException) { } }
        // Exit can arrive while a modal dialog (settings, status) runs its own message loop; once it has, nothing may create a new tray icon,
        // which would outlive the process as a ghost icon.
        private void UpdateTray() { if (exiting) return; bool needed=config.ShowTrayIcon || config.ShowNotifications; if (needed && tray==null) tray=new TrayIcon(DispatchCommand,config.ShowTrayIcon); else if (!needed && tray!=null) { tray.Dispose(); tray=null; } else if (tray!=null) tray.SetPermanent(config.ShowTrayIcon); }
        // Runs after the settings dialog saved: shortcuts are re-registered at once, so no restart is needed.
        private string ApplySettings() { string error=platform.ReloadShortcuts(); UpdateTray(); return error; }
        public void DispatchCommand(string command) { if(dispatcher.IsDisposed) return; try { dispatcher.BeginInvoke((MethodInvoker)delegate { Command(command); }); } catch(InvalidOperationException) { } }
        public void Command(string command) { if (exiting) return; if (command=="settings") Management.ShowSettings(config,configPath,ApplySettings); else if(command=="status") Management.ShowStatus(coordinator,pasteFirst.StatusLine); else if(command=="clear") { coordinator.Clear(); pasteFirst.Checks.Clear(); } else if(command=="exit") ExitThread(); else if(command=="enable-autostart") Management.SetAutostart(true); else if(command=="disable-autostart") Management.SetAutostart(false); }
        private void StartWorker(ThreadStart action) { Thread t=null;t=new Thread((ThreadStart)delegate { try { action(); } finally { lock(workerGate) workers.Remove(Thread.CurrentThread); } });t.IsBackground=true;t.SetApartmentState(ApartmentState.STA);lock(workerGate) workers.Add(t);t.Start(); }
        protected override void ExitThreadCore() { if (exiting) return; exiting=true; coordinator.Failed -= Notify; coordinator.Cancel(); platform.StopNormalPaste(); Thread[] active;lock(workerGate) active=workers.ToArray();for(int i=0;i<active.Length;i++) active[i].Join(2500);pipe.Dispose();if (tray!=null) { tray.Dispose(); tray=null; }dispatcher.Dispose();base.ExitThreadCore(); }
    }
}
