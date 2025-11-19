namespace BrowserReporterService.Services
{
    public class CommandLineArgs
    {
        public bool IsDebug { get; private set; }
        public bool IsOnce { get; private set; }
        public bool Install { get; private set; }
        public bool Uninstall { get; private set; }
        public bool EncryptConfig { get; private set; }
        public bool NoTray { get; private set; }
        public string? ConfigPath { get; private set; }
        public string? ServerUrl { get; private set; }

        public bool ShouldRunApplication => !Install && !Uninstall && !EncryptConfig;

        public CommandLineArgs(string[] args)
        {
            IsDebug = args.Contains("--debug");
            IsOnce = args.Contains("--once");
            Install = args.Contains("--install");
            Uninstall = args.Contains("--uninstall");
            EncryptConfig = args.Contains("--encryptconfig");
            NoTray = args.Contains("--no-tray");

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--config")
                {
                    ConfigPath = args[i + 1];
                }
                else if (args[i] == "--server")
                {
                    ServerUrl = args[i + 1];
                }
            }

            if (EncryptConfig && string.IsNullOrEmpty(ConfigPath))
            {
                throw new ArgumentException("The --encryptconfig flag requires a --config path to be specified.");
            }
        }
    }
} 