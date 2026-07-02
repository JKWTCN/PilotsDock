using CFIT.AppLogger;
using CFIT.AppTools;
using CFIT.Installer.Product;
using CFIT.Installer.UI;
using System.Text.Json.Nodes;

namespace Installer
{
    public class Definition : ProductDefinition
    {
        public Config Config { get { return BaseConfig as Config; } }
        public WorkerManager WorkerManager { get { return BaseWorker as WorkerManager; } }
        protected string[] Arguments { get; set; }

        public Definition(string[] args) : base(args)
        {
            Arguments = args;
            Localization.SetLanguageOverride(GetApplicationLanguage);
        }

        protected override void CreateConfig()
        {
            BaseConfig = new Config();
        }

        protected override void CreateWorker()
        {
            BaseWorker = new WorkerManager(Config);
        }

        protected override void ParseArguments(string[] args)
        {
            base.ParseArguments(args);
            Arguments = args;
            if (Sys.HasArgument(args, "--ignoremsfs20"))
            {
                Config.IgnoreMsfs2020 = true;
                Logger.Information("Installer was started with IgnoreMSFS (2020)");
            }
            if (Sys.HasArgument(args, "--ignoremsfs24"))
            {
                Config.IgnoreMsfs2024 = true;
                Logger.Information("Installer was started with IgnoreMSFS (2024)");
            }
        }

        protected virtual string GetApplicationLanguage()
        {
            try
            {
                string language = GetArgumentValue(Arguments, "--language");
                if (!string.IsNullOrWhiteSpace(language))
                    return language;

                string info = GetArgumentValue(Arguments, "--info");
                if (!string.IsNullOrWhiteSpace(info))
                    return JsonNode.Parse(info)?["application"]?["language"]?.GetValue<string>();
            }
            catch { }

            return "";
        }

        protected static string GetArgumentValue(string[] args, string key)
        {
            if (args == null || args.Length == 0)
                return "";

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == key && i + 1 < args.Length)
                    return args[i + 1];
                if (args[i].StartsWith(key + "="))
                    return args[i].Substring(key.Length + 1);
            }

            return "";
        }

        protected override void CreateWindowBehavior()
        {
            base.CreateWindowBehavior();
            BaseBehavior.MaxTasksShown = 6;
            BaseBehavior.CheckRunning = false;
            BaseBehavior.ShowInstallationWarnings = false;
            BaseBehavior.WelcomeLogoWidth = 192;
            BaseBehavior.WelcomeLogoResource = "Payload/icon";
        }

        protected override void CreatePageWelcome()
        {
            PageBehaviors.Add(InstallerPages.WELCOME, new PageWelcomePilotsDeck());
        }

        protected override void CreatePageConfig()
        {
            PageBehaviors.Add(InstallerPages.CONFIG, new ConfigPage());
        }
    }
}