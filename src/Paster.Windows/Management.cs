using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows.Forms;
using Paster.Core;

namespace Paster.Windows
{
    internal sealed class ManagementPipe : IDisposable
    {
        private readonly PasterApplicationContext context; private readonly Thread thread; private readonly object pipeGate=new object(); private volatile bool stop; private NamedPipeServerStream activePipe;
        public ManagementPipe(PasterApplicationContext c) { context=c; thread=new Thread(Listen); thread.IsBackground=true; thread.Start(); }
        private void Listen() { while(!stop) { try { using(NamedPipeServerStream p=new NamedPipeServerStream("Paster.Management",PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.None)) { lock(pipeGate) { if(stop) return; activePipe=p; } try { p.WaitForConnection(); using(StreamReader r=new StreamReader(p)) { string command=r.ReadLine(); if(!String.IsNullOrEmpty(command)) context.DispatchCommand(command); } } finally { lock(pipeGate) { if(Object.ReferenceEquals(activePipe,p)) activePipe=null; } } } } catch { if(!stop) Thread.Sleep(100); } } }
        public void Dispose() { stop=true; lock(pipeGate) { if(activePipe!=null) { try { activePipe.Dispose(); } catch { } activePipe=null; } } if (thread != Thread.CurrentThread) thread.Join(); }
    }
    internal static class Management
    {
        public static bool TrySend(string arg, string ignored) { string command=arg.TrimStart('-').Replace("-", "-"); try { using(NamedPipeClientStream p=new NamedPipeClientStream(".","Paster.Management",PipeDirection.Out)) { p.Connect(300); using(StreamWriter w=new StreamWriter(p)) { w.AutoFlush=true; w.WriteLine(command); } } return true; } catch { return false; } }
        public static void SetAutostart(bool enabled) { string key="Software\\Microsoft\\Windows\\CurrentVersion\\Run"; using(Microsoft.Win32.RegistryKey k=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(key)) { if(enabled) k.SetValue("Paster", "\""+Application.ExecutablePath+"\" --background"); else k.DeleteValue("Paster", false); } }
        public static void ShowStatus(TransferCoordinator c) { TransferStatus s=c.Status; MessageBox.Show("Running\r\nCaptured text: "+(s.HasCapture?"yes":"no")+"\r\nTransfer active: "+(s.IsTransferring?"yes":"no")+"\r\nInput available: "+(s.IsInputAvailable?"yes":"no")+"\r\nTarget window: "+(s.TargetWindow==IntPtr.Zero?"none":s.TargetWindow.ToString())+"\r\nLast error: "+(s.LastError??"none"),"Paster status"); }
        public static void ShowSettings(PasterConfig c, string path) { using(Form f=new Form()) { f.Text="Paster settings"; f.Width=460; f.Height=330; f.FormBorderStyle=FormBorderStyle.FixedDialog; f.MaximizeBox=false; string[] labels={"Capture shortcut","Paste shortcut","Cancel shortcut","Start delay (ms)","Character delay (ms)","Clipboard timeout (ms)","Maximum characters"}; TextBox[] boxes=new TextBox[7]; for(int i=0;i<labels.Length;i++){ Label l=new Label(); l.Text=labels[i]; l.Left=15;l.Top=15+i*34;l.Width=190; f.Controls.Add(l); boxes[i]=new TextBox(); boxes[i].Left=215;boxes[i].Top=l.Top-3;boxes[i].Width=200;f.Controls.Add(boxes[i]); } boxes[0].Text=c.CaptureShortcut;boxes[1].Text=c.PasteShortcut;boxes[2].Text=c.CancelShortcut;boxes[3].Text=c.StartDelayMilliseconds.ToString();boxes[4].Text=c.CharacterDelayMilliseconds.ToString();boxes[5].Text=c.ClipboardTimeoutMilliseconds.ToString();boxes[6].Text=c.MaximumTextCharacters.ToString(); Button save=new Button();save.Text="Save";save.Left=215;save.Top=260;save.DialogResult=DialogResult.OK;f.Controls.Add(save);f.AcceptButton=save; if(f.ShowDialog()==DialogResult.OK) try { PasterConfig candidate=c.Clone();candidate.CaptureShortcut=boxes[0].Text;candidate.PasteShortcut=boxes[1].Text;candidate.CancelShortcut=boxes[2].Text;candidate.StartDelayMilliseconds=Int32.Parse(boxes[3].Text);candidate.CharacterDelayMilliseconds=Int32.Parse(boxes[4].Text);candidate.ClipboardTimeoutMilliseconds=Int32.Parse(boxes[5].Text);candidate.MaximumTextCharacters=Int32.Parse(boxes[6].Text);candidate.Save(path);c.CopyFrom(candidate); MessageBox.Show("Saved. Restart Paster for shortcut changes.","Paster"); } catch(Exception ex) { MessageBox.Show(ex.Message,"Invalid settings"); } } }
    }
}
