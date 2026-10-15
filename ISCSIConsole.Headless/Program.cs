using System;

namespace ISCSIConsole
{
    public partial class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "/help" || args[0] == "--help")
            {
                Console.WriteLine("iSCSIConsole headless 1.5.9 (self-contained; no installed .NET required)");
                Console.WriteLine("Use /start /config targets.xml, /serve /help, or a VHD/VHDX path.");
                return 0;
            }
            try
            {
                return HeadlessServer.Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR " + ex);
                return 1;
            }
        }
    }
}
