using CFIT.Installer.Tasks;
using Localization = CFIT.Installer.UI.Localization;
using System.IO;
using System.Threading.Tasks;

namespace Installer.Worker
{
    public class WorkerLegacyProfiles : TaskWorker<Config>
    {
        public WorkerLegacyProfiles(Config config) : base(config, Localization.Translate("Profile Mappings"), Localization.Translate("Checking for imported Profiles ..."))
        {
            Model.DisplayCompleted = true;
            Model.DisplayCompleted = false;
        }

        protected override async Task<bool> DoRun()
        {
            string legacyFile = Path.Combine(Config.ProductPathProfiles, "savedProfiles.txt");
            if (File.Exists(legacyFile))
            {
                Model.SetSuccess(Localization.Translate("Detected imported Profiles for automatic Switching. You need to reconfigure all your Mappings in the {0}!\r\nClick on the Link to open the Tool:", Config.ProfileManagerName));
                Model.State = TaskState.WAITING;

                Model.AddLink(Config.ProfileManagerName, Config.ProfileManagerExePath);
                Model.DisplayInSummary = true;
            }
            else
            {
                Model.SetSuccess(Localization.Translate("No legacy Profile Mappings found!"));
            }

            await Task.Delay(0);
            return true;
        }
    }
}
