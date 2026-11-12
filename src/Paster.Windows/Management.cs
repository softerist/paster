using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Paster.Core;

namespace Paster.Windows
{
    internal sealed class ManagementPipe : IDisposable
    {
        private readonly PasterApplicationContext context; private readonly Thread thread; private readonly ManualResetEvent stopped=new ManualResetEvent(false);
        public ManagementPipe(PasterApplicationContext c) { context=c; thread=new Thread(Listen); thread.IsBackground=true; thread.Start(); }
        // Every wait is asynchronous so Dispose can end it: disposing a pipe does not release a thread blocked in a synchronous
        // WaitForConnection, which made --exit hang until the process was killed.
        private void Listen()
        {
            while (!stopped.WaitOne(0))
            {
                try
                {
                    using (NamedPipeServerStream p=new NamedPipeServerStream(Management.PipeName,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous))
                    {
                        IAsyncResult connect=p.BeginWaitForConnection(null,null);
                        if (WaitHandle.WaitAny(new WaitHandle[] { connect.AsyncWaitHandle, stopped })!=0) return;
                        p.EndWaitForConnection(connect);
                        string command=ReadCommand(p); if (!String.IsNullOrEmpty(command)) context.DispatchCommand(command);
                    }
                }
                catch { if (stopped.WaitOne(100)) return; }
            }
        }
        // One short line; a client that connects but never writes is dropped after a second instead of blocking the listener.
        private string ReadCommand(NamedPipeServerStream p)
        {
            byte[] buffer=new byte[256]; int length=0;
            while (length<buffer.Length)
            {
                IAsyncResult read=p.BeginRead(buffer,length,buffer.Length-length,null,null);
                if (WaitHandle.WaitAny(new WaitHandle[] { read.AsyncWaitHandle, stopped },1000)!=0) return null;
                int n=p.EndRead(read); if (n==0) break; length+=n;
                if (Array.IndexOf(buffer,(byte)'\n',0,length)>=0) break;
            }
            return PasterConfig.FirstLine(Encoding.UTF8.GetString(buffer,0,length)).Trim();
        }
        public void Dispose() { stopped.Set(); if (thread!=Thread.CurrentThread) thread.Join(2000); }
    }

    // Notification-area icon: failures appear as notifications, and its menu offers the management commands. A permanent icon stays visible;
    // otherwise it appears only while a notification is shown (Windows attributes every notification to an icon) and hides again afterwards.
    internal sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon icon=new NotifyIcon(); private readonly ContextMenuStrip menu=new ContextMenuStrip(); private readonly Icon image=CreateIcon(); private readonly System.Windows.Forms.Timer hide=new System.Windows.Forms.Timer(); private bool permanent;
        // Commands are queued rather than run inside the menu's click handler, so Exit can dispose the menu safely.
        public TrayIcon(Action<string> command, bool permanent)
        {
            // Long enough for the notification to be read; a later notification restarts it. Clicking the notification opens the status.
            hide.Interval=15000; hide.Tick+=delegate { hide.Stop(); if (!this.permanent) icon.Visible=false; };
            icon.BalloonTipClicked+=delegate { command("status"); };
            menu.Items.Add("Status", null, delegate { command("status"); });
            menu.Items.Add("Settings...", null, delegate { command("settings"); });
            menu.Items.Add("Clear captured text", null, delegate { command("clear"); });
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem autostart=new ToolStripMenuItem("Start with Windows"); autostart.Click+=delegate { command(autostart.Checked ? "disable-autostart" : "enable-autostart"); }; menu.Items.Add(autostart);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { command("exit"); });
            menu.Opening+=delegate { autostart.Checked=Management.IsAutostartEnabled(); };
            icon.ContextMenuStrip=menu; icon.Icon=image; icon.Text="Paster"; icon.DoubleClick+=delegate { command("settings"); }; SetPermanent(permanent);
        }
        public void SetPermanent(bool value) { permanent=value; if (value) { hide.Stop(); icon.Visible=true; } else if (!hide.Enabled) icon.Visible=false; }
        // ShowBalloonTip rejects empty text, and an exception message can be empty.
        public void ShowError(string message) { if (String.IsNullOrWhiteSpace(message)) message="Something went wrong; see --status."; if (!permanent) { icon.Visible=true; hide.Stop(); hide.Start(); } icon.ShowBalloonTip(10000,"Paster",message.Length>255 ? message.Substring(0,252)+"..." : message,ToolTipIcon.Warning); }
        public void Dispose() { hide.Dispose(); icon.Visible=false; icon.Dispose(); menu.Dispose(); }
        // Drawn at 32 pixels so the shell scales it down at any display scale. The icon handle lives as long as the process.
        private static Icon CreateIcon()
        {
            using (Bitmap b=new Bitmap(32,32))
            {
                using (Graphics g=Graphics.FromImage(b))
                {
                    g.SmoothingMode=SmoothingMode.AntiAlias; g.TextRenderingHint=TextRenderingHint.AntiAlias; g.Clear(Color.Transparent);
                    using (GraphicsPath shape=new GraphicsPath()) using (Brush fill=new SolidBrush(Color.FromArgb(0,103,192)))
                    {
                        const int r=14; shape.AddArc(1,1,r,r,180,90); shape.AddArc(31-r,1,r,r,270,90); shape.AddArc(31-r,31-r,r,r,0,90); shape.AddArc(1,31-r,r,r,90,90); shape.CloseFigure(); g.FillPath(fill,shape);
                    }
                    using (Font font=new Font("Segoe UI",22,FontStyle.Bold,GraphicsUnit.Pixel)) using (StringFormat center=new StringFormat()) { center.Alignment=StringAlignment.Center; center.LineAlignment=StringAlignment.Center; g.DrawString("P",font,Brushes.White,new RectangleF(0,1,32,32),center); }
                }
                return Icon.FromHandle(b.GetHicon());
            }
        }
    }

    internal static class Management
    {
        private const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        // Pipe names are machine-wide; the session suffix keeps each signed-in user's instance separate, like the per-session mutex.
        public static readonly string PipeName="Paster.Management."+Process.GetCurrentProcess().SessionId;
        public static bool TrySend(string arg, string ignored) { string command=arg.TrimStart('-').Replace("-", "-"); try { using(NamedPipeClientStream p=new NamedPipeClientStream(".",PipeName,PipeDirection.Out)) { p.Connect(300); using(StreamWriter w=new StreamWriter(p)) { w.AutoFlush=true; w.WriteLine(command); } } return true; } catch { return false; } }
        public static void SetAutostart(bool enabled) { using(RegistryKey k=Registry.CurrentUser.CreateSubKey(RunKey)) { if(enabled) k.SetValue("Paster", "\""+Application.ExecutablePath+"\" --background"); else k.DeleteValue("Paster", false); } }
        public static bool IsAutostartEnabled() { using(RegistryKey k=Registry.CurrentUser.OpenSubKey(RunKey)) return k!=null && k.GetValue("Paster")!=null; }
        public static void ShowStatus(TransferCoordinator c, string extra) { TransferStatus s=c.Status; MessageBox.Show("Running\r\nCaptured text: "+(s.HasCapture?"yes":"no")+"\r\nTransfer active: "+(s.IsTransferring?"yes":"no")+"\r\nLast input accepted: "+(s.IsInputAvailable?"yes":"no")+"\r\nTarget window: "+(s.TargetWindow==IntPtr.Zero?"none":s.TargetWindow.ToString())+"\r\nResume point: "+(s.ResumeOffset==0?"none":String.Format("{0:N0} of {1:N0} characters",s.ResumeOffset,s.ResumeTotal))+"\r\nLast error: "+(s.LastError??"none")+(extra!=null ? "\r\n"+extra : ""),"Paster status"); }

        private sealed class Field { public string Label, Member; public Control Control; public TabPage Page; public Action<PasterConfig> Apply; }
        private static Form openSettings;
        // Starts from the file's current contents so hand edits made while Paster runs are shown and kept. Invalid input keeps the dialog open
        // and points at the field. Saved values apply to the running process at once; apply (null when Paster is not running) re-registers
        // the shortcuts and returns any problem to show.
        public static void ShowSettings(PasterConfig live, string path, Func<string> apply)
        {
            if (openSettings!=null) { openSettings.Activate(); return; }
            PasterConfig c=PasterConfig.TryLoad(path) ?? live, saved=null; List<Field> fields=new List<Field>();
            using (Form f=new Form())
            {
                f.Text="Paster settings"; f.FormBorderStyle=FormBorderStyle.FixedDialog; f.MaximizeBox=false; f.MinimizeBox=false; f.ShowIcon=false; f.StartPosition=FormStartPosition.CenterScreen;
                TabControl tabs=new TabControl(); tabs.Dock=DockStyle.Fill;
                TableLayoutPanel t=AddPage(tabs,"General");
                AddText(fields,t,"Capture shortcut","CaptureShortcut",c.CaptureShortcut,(x,v)=>{ Paster.Core.Shortcut.Parse(v); x.CaptureShortcut=v; });
                AddText(fields,t,"Paste shortcut","PasteShortcut",c.PasteShortcut,(x,v)=>{ Paster.Core.Shortcut.Parse(v); x.PasteShortcut=v; });
                AddText(fields,t,"Cancel shortcut","CancelShortcut",c.CancelShortcut,(x,v)=>{ Paster.Core.Shortcut.Parse(v); x.CancelShortcut=v; });
                AddCheck(fields,t,"Shortcuts work in any key order","AnyOrderShortcuts",c.AnyOrderShortcuts,(x,v)=>x.AnyOrderShortcuts=v);
                AddCheck(fields,t,"Stop typing on key press or click","StopOnUserInput",c.StopOnUserInput,(x,v)=>x.StopOnUserInput=v);
                AddCheck(fields,t,"Show tray icon","ShowTrayIcon",c.ShowTrayIcon,(x,v)=>x.ShowTrayIcon=v);
                AddCheck(fields,t,"Show notifications when something goes wrong","ShowNotifications",c.ShowNotifications,(x,v)=>x.ShowNotifications=v);
                AddNumber(fields,t,"Confirm pastes over (lines, 0=never)","ConfirmAboveLines",c.ConfirmAboveLines,(x,v)=>x.ConfirmAboveLines=v);
                AddNumber(fields,t,"Prompt timeout (s)","PromptTimeoutSeconds",c.PromptTimeoutSeconds,(x,v)=>x.PromptTimeoutSeconds=v);
                t=AddPage(tabs,"Typing");
                AddNumber(fields,t,"Start delay (ms)","StartDelayMilliseconds",c.StartDelayMilliseconds,(x,v)=>x.StartDelayMilliseconds=v);
                AddNumber(fields,t,"Delay per batch (ms)","CharacterDelayMilliseconds",c.CharacterDelayMilliseconds,(x,v)=>x.CharacterDelayMilliseconds=v);
                AddNumber(fields,t,"Characters per batch","CharactersPerBatch",c.CharactersPerBatch,(x,v)=>x.CharactersPerBatch=v);
                AddNumber(fields,t,"Clipboard timeout (ms)","ClipboardTimeoutMilliseconds",c.ClipboardTimeoutMilliseconds,(x,v)=>x.ClipboardTimeoutMilliseconds=v);
                AddNumber(fields,t,"Maximum characters","MaximumTextCharacters",c.MaximumTextCharacters,(x,v)=>x.MaximumTextCharacters=v);
                AddCheck(fields,t,"Type uppercase and symbols as Unicode (no Shift)","TypeShiftedAsUnicode",c.TypeShiftedAsUnicode,(x,v)=>x.TypeShiftedAsUnicode=v);
                AddCheck(fields,t,"Type all characters as Unicode (any keyboard layout)","TypeAllAsUnicode",c.TypeAllAsUnicode,(x,v)=>x.TypeAllAsUnicode=v);
                t=AddPage(tabs,"Remote windows");
                AddCheck(fields,t,"Type on Ctrl+V in listed apps","TypeOnCtrlV",c.TypeOnCtrlV,(x,v)=>x.TypeOnCtrlV=v);
                AddText(fields,t,"Type on Ctrl+V in apps","TypeOnPasteApps",c.TypeOnPasteApps,(x,v)=>x.TypeOnPasteApps=v);
                AddCheck(fields,t,"Paste normally where the session allows it (type only where blocked)","PasteNormallyFirst",c.PasteNormallyFirst,(x,v)=>x.PasteNormallyFirst=v);
                AddCheck(fields,t,"Type text copied inside remote windows","TypeRemoteCopies",c.TypeRemoteCopies,(x,v)=>x.TypeRemoteCopies=v);
                AddNumber(fields,t,"Remote speed (keys/s, 0=max)","RemoteCharactersPerSecond",c.RemoteCharactersPerSecond,(x,v)=>x.RemoteCharactersPerSecond=v);
                AddNumber(fields,t,"Pause after Enter and Tab (ms)","RemoteKeyPauseMilliseconds",c.RemoteKeyPauseMilliseconds,(x,v)=>x.RemoteKeyPauseMilliseconds=v);
                AddCheck(fields,t,"Use code mode in remote windows","CodeModeForRemotePaste",c.CodeModeForRemotePaste,(x,v)=>x.CodeModeForRemotePaste=v);
                t=AddPage(tabs,"Code mode");
                AddCheck(fields,t,"Code mode: Space+Backspace before Enter and Tab (closes popups)","CodeModeDismissSuggestions",c.CodeModeDismissSuggestions,(x,v)=>x.CodeModeDismissSuggestions=v);
                AddCheck(fields,t,"Code mode: Home after Enter","CodeModeHomeAfterEnter",c.CodeModeHomeAfterEnter,(x,v)=>x.CodeModeHomeAfterEnter=v);
                AddCheck(fields,t,"Code mode: Escape before Enter and Tab","CodeModeEscapeBeforeEnter",c.CodeModeEscapeBeforeEnter,(x,v)=>x.CodeModeEscapeBeforeEnter=v);
                AddCheck(fields,t,"Code mode: Delete auto-closed brackets and quotes","CodeModeDeleteAutoClosed",c.CodeModeDeleteAutoClosed,(x,v)=>x.CodeModeDeleteAutoClosed=v);
                FlowLayoutPanel buttons=new FlowLayoutPanel(); buttons.Dock=DockStyle.Bottom; buttons.FlowDirection=FlowDirection.RightToLeft; buttons.AutoSize=true; buttons.Padding=new Padding(6);
                Button cancel=new Button(); cancel.Text="Cancel"; cancel.DialogResult=DialogResult.Cancel; Button save=new Button(); save.Text="Save";
                buttons.Controls.Add(cancel); buttons.Controls.Add(save); f.Controls.Add(tabs); f.Controls.Add(buttons); f.AcceptButton=save; f.CancelButton=cancel;
                save.Click+=delegate
                {
                    PasterConfig candidate=c.Clone(); Field current=null;
                    try { foreach (Field field in fields) { current=field; field.Apply(candidate); } current=null; candidate.Validate(); candidate.Save(path); }
                    catch (Exception ex)
                    {
                        ArgumentException argument=ex as ArgumentException; if (current==null && argument!=null && argument.ParamName!=null) current=fields.Find(x=>x.Member==argument.ParamName);
                        string message=PasterConfig.FirstLine(ex.Message); if (ex is IOException || ex is UnauthorizedAccessException) message="The settings file could not be saved: "+message;
                        if (current!=null) { tabs.SelectedTab=current.Page; current.Control.Focus(); message=current.Label+": "+message; }
                        MessageBox.Show(f,message,"Invalid setting",MessageBoxButtons.OK,MessageBoxIcon.Warning); return;
                    }
                    saved=candidate; f.DialogResult=DialogResult.OK;
                };
                // Fixed coordinates overflowed small or scaled screens; the tabs fit in 400 pixels and scroll if the screen is smaller still.
                f.ClientSize=new Size(560,400); Rectangle area=Screen.FromPoint(Cursor.Position).WorkingArea; if (f.Height>area.Height) f.Height=area.Height;
                openSettings=f; try { if (f.ShowDialog()!=DialogResult.OK || saved==null) return; } finally { openSettings=null; }
            }
            live.CopyFrom(saved);
            string warning=apply!=null ? apply() : null;
            if (apply==null) MessageBox.Show("Saved. Paster is not running; the settings apply when it starts.","Paster");
            else if (!String.IsNullOrEmpty(warning)) MessageBox.Show("Settings saved. "+warning,"Paster",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
        private static TableLayoutPanel AddPage(TabControl tabs, string title)
        {
            TabPage page=new TabPage(title); page.AutoScroll=true; page.Padding=new Padding(8);
            TableLayoutPanel t=new TableLayoutPanel(); t.ColumnCount=2; t.AutoSize=true; t.AutoSizeMode=AutoSizeMode.GrowAndShrink; t.Dock=DockStyle.Top;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            page.Controls.Add(t); tabs.TabPages.Add(page); return t;
        }
        private static void AddText(List<Field> fields, TableLayoutPanel t, string label, string member, string value, Action<PasterConfig,string> set)
        {
            Label l=new Label(); l.Text=label; l.AutoSize=true; l.Anchor=AnchorStyles.Left; l.Margin=new Padding(3,6,12,6);
            TextBox box=new TextBox(); box.Text=value; box.Dock=DockStyle.Fill; t.Controls.Add(l); t.Controls.Add(box);
            Field field=new Field(); field.Label=label; field.Member=member; field.Control=box; field.Page=(TabPage)t.Parent; field.Apply=x=>set(x,box.Text.Trim()); fields.Add(field);
        }
        private static void AddNumber(List<Field> fields, TableLayoutPanel t, string label, string member, int value, Action<PasterConfig,int> set)
        {
            AddText(fields,t,label,member,value.ToString(),(x,v)=>{ int n; if (!Int32.TryParse(v,out n)) throw new FormatException("Enter a whole number."); set(x,n); });
        }
        private static void AddCheck(List<Field> fields, TableLayoutPanel t, string label, string member, bool value, Action<PasterConfig,bool> set)
        {
            CheckBox box=new CheckBox(); box.Text=label; box.Checked=value; box.AutoSize=true; box.Margin=new Padding(3,6,3,3); t.Controls.Add(box); t.SetColumnSpan(box,2);
            Field field=new Field(); field.Label=label; field.Member=member; field.Control=box; field.Page=(TabPage)t.Parent; field.Apply=x=>set(x,box.Checked); fields.Add(field);
        }
    }
}
