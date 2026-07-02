using CFIT.AppTools;
using CFIT.Installer.Tasks;
using Localization = CFIT.Installer.UI.Localization;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Installer.Worker
{
    public enum DesktopLinkOperation
    {
        CREATE = 1,
        REMOVE = 2,
    }

    public class WorkerDesktopLink : TaskWorker<Config>
    {
        public DesktopLinkOperation Operation { get; set; }

        public WorkerDesktopLink(Config config, DesktopLinkOperation operation) : base(config, Localization.Translate("Desktop Link"), Localization.Translate("Creating Link ..."))
        {
            Model.DisplayCompleted = true;
            Model.DisplayInSummary = true;
            Operation = operation;
        }

        protected virtual bool CreateLink()
        {
            return Sys.CreateLink(Config.ProfileManagerName, Config.ProfileManagerExePath, Localization.Translate("Start {0}", Config.ProfileManagerName));
        }

        protected virtual bool RemoveLink()
        {
            string link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{Config.ProfileManagerName}.lnk");

            if (File.Exists(link))
                File.Delete(link);
            else
            {
                Model.DisplayCompleted = false;
                Model.DisplayInSummary = false;
            }

            return !File.Exists(link);
        }

        protected override async Task<bool> DoRun()
        {
            await Task.Delay(0);
            bool result = false;
            if (Operation == DesktopLinkOperation.CREATE)
            {
                result = CreateLink();
                if (result)
                    Model.SetSuccess(Localization.Translate("Link for {0} placed on Desktop!", Config.ProfileManagerName));
            }
            else if (Operation == DesktopLinkOperation.REMOVE)
            {
                result = RemoveLink();
                if (result)
                    Model.SetSuccess(Localization.Translate("Link for {0} removed from Desktop!", Config.ProfileManagerName));
            }

            return result;
        }
    }
}
