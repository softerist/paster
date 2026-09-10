using System;
using System.Threading;
using System.Windows.Forms;
using Paster.Core;

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
            if (args.Length > 0 && args[0] != "--background") { if (!Management.TrySend(args[0], String.Join(" ", args, 1, Math.Max(0, args.Length - 1)))) { if (args[0] == "--settings") { Management.ShowSettings(config, configPath); } else if (args[0] == "--status") { MessageBox.Show("Paster is not running.", "Paster"); } } return; }
            bool owns; using (Mutex mutex = new Mutex(true, "Local\\Paster.SingleInstance", out owns)) { if (!owns) return; using (WindowsPlatform platform = new WindowsPlatform(config)) { TransferCoordinator coordinator = new TransferCoordinator(platform, new SystemClock(), config); using (ApplicationContext app = new PasterApplicationContext(platform, coordinator, config, configPath)) Application.Run(app); } }
        }
    }
}
