using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Paster.Core;

// The title becomes the file description, which Windows shows as the sender of Paster's notifications (otherwise "paster.exe").
[assembly: AssemblyTitle("Paster")]
[assembly: AssemblyProduct("Paster")]

namespace Paster.Windows
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Paster";
            string configPath = root + "\\settings.json"; PasterConfig config = PasterConfig.Load(configPath);
            if (args.Length > 0 && args[0] != "--background") { if (!Management.TrySend(args[0], String.Join(" ", args, 1, Math.Max(0, args.Length - 1)))) { if (args[0] == "--settings") { Management.ShowSettings(config, configPath, null); } else if (args[0] == "--status") { MessageBox.Show("Paster is not running.", "Paster"); } } return; }
            bool owns; using (Mutex mutex = new Mutex(true, "Local\\Paster.SingleInstance", out owns))
            {
                if (!owns) return; string settingsError; config = PasterConfig.LoadOrCreate(configPath, out settingsError);
                using (WindowsPlatform platform = new WindowsPlatform(config))
                {
                    TransferCoordinator coordinator = new TransferCoordinator(platform, new SystemClock(), config);
                    string startupError = String.Join(" ", new string[] { settingsError, platform.RegistrationError }).Trim(); if (startupError.Length == 0) startupError = null;
                    if (startupError != null) coordinator.ReportError(startupError);
                    using (ApplicationContext app = new PasterApplicationContext(platform, coordinator, config, configPath, startupError)) Application.Run(app);
                }
            }
        }
    }
}
