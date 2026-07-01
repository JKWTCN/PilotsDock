using CFIT.AppLogger;
using CFIT.Installer.Product;
using Serilog;
using System;
using System.IO;

namespace ProfileManager
{
    public class Config : ConfigBase, ILoggerConfig
    {
        public static Config Instance { get; } = new Config();

        public string LogDirectory { get { return "log"; } }
        public string LogFile { get { return "ProfileManager.log"; } }
        public RollingInterval LogInterval { get { return RollingInterval.Infinite; } }
        public int SizeLimit { get { return 1024 * 1024; } }
        public int LogCount { get { return 1; } }
        public string LogTemplate { get { return "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] [{SourceContext}] {Message} {NewLine}"; } }
        public LogLevel LogLevel { get { return LogLevel.Debug; } }

        //ConfigBase
        public override string ProductName { get { return PluginBinary; } }
        public static string PluginBinary { get { return "PilotsDock"; } }
        public override string ProductConfigFile { get { return $"PluginConfig.json"; } }
        public override string ProductConfigPath { get { return Path.Combine(ProductPath, ProductConfigFile); } }
        public override string ProductExePath { get { return Path.Combine(ProductPath, ProductExe); } }
        public override string ProductPath { get { return GetPluginPath(); } }
        public virtual string ProductPathProfiles { get { return Path.Combine(ProductPath, "Profiles"); } }
        public virtual string ProductPathScripts { get { return Path.Combine(ProductPath, "Scripts"); } }

        private static string GetPluginPath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string streamDockPath = Path.Combine(appDataPath, "HotSpot", "StreamDock", "Plugins", Parameters.PLUGIN_FOLDER);
            if (Directory.Exists(streamDockPath))
                return streamDockPath;

            Logger.Warning($"StreamDock path not found: {streamDockPath}");
            return Parameters.PLUGIN_PATH;
        }
    }
}
