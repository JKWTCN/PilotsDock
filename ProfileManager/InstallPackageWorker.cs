using CFIT.AppLogger;
using CFIT.AppTools;
using CFIT.Installer.LibFunc;
using CFIT.Installer.LibWorker;
using CFIT.Installer.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using MessageBox = System.Windows.Forms.MessageBox;
using ProfileManager;

namespace ProfileManager
{
    public class InstallPackageWorker
    {
        protected List<TaskModel> ProfileTaskList { get; } = [];
        protected Dictionary<string, TaskModel> ProfileTasks { get; } = [];
        protected bool ProfileTasksCompleted { get { return ProfileTaskList.All(t => t.State == TaskState.COMPLETED); } }
        protected DispatcherTimer SwapTimer { get; set; }
        protected ProfileController ProfileController { get; set; } = new();
        protected FuncStreamDock StreamDock { get; set; } = new();
        protected bool IsCanceled { get; set; } = false;
        public PackageFile PackageFile { get; protected set; } = new(null);

        public bool OptionRemoveOldProfiles { get; set; } = false;

        public bool IsValid { get { return IsChecked && IsLoaded && IsCompatible; } }
        public bool IsChecked { get; protected set; } = false;
        public bool IsLoaded { get; protected set; } = false;
        public bool IsCompatible { get { return PackageFile.IsCompatible; } }
        public bool FilesInstalled { get; protected set; } = false;
        public bool IsInstalled { get { return InstallationCompleted && FilesInstalled && ProfileTasksCompleted; } }
        public bool InstallationCompleted { get; protected set; }
        protected bool IsInstalling { get; set; }

        public int CountValidFiles { get { return PackageFile.CountValidTotal; } }
        public int CountTotalFiles { get { return PackageFile.CountValidTotal + PackageFile.FilesUnknown.Count; } }
        public int CountProfileUpdates { get { return PackageFile.PackagedProfiles.Where(p => p.HasOldProfile).Count(); } }

        public void SetFile(string filePath, RotaryProfileTarget rotaryTarget = RotaryProfileTarget.N4Pro)
        {
            PackageFile = new(filePath) { RotaryTarget = rotaryTarget };
        }

        public bool LoadPackage()
        {
            try
            {
                IsChecked = PackageFile.CheckFile();
                if (IsChecked)
                    IsLoaded = PackageFile.LoadPackageInfo();

                if (IsValid && PackageFile.CountProfiles > 0)
                    CheckExistingProfiles();
            }
            catch (Exception ex)
            {
                IsLoaded = false;
                TaskStore.CurrentTask.SetError(ex);
            }

            return IsValid;
        }

        public void CheckExistingProfiles()
        {
            var checkTask = TaskStore.Add(Localization.Translate("Check existing Profiles"), "");
            checkTask.DisplayCompleted = false;
            ProfileController.LoadManifestsOnly();
            if (!ProfileController.IsLoaded)
                throw new InvalidOperationException("Could not read existing profiles.");
            foreach (var profile in PackageFile.PackagedProfiles)
            {
                profile.ExistingDirectories.Clear();
                profile.HasOldProfile = false;
                foreach (var manifest in ProfileController.ProfileManifests)
                    profile.ExistingDirectories.Add(manifest.ProfileDirectory);
                if (!string.IsNullOrEmpty(profile.ProfileName) && ProfileController.ManifestNameExits(profile.ProfileName))
                {
                    checkTask.Message = $"Found match for '{profile.ProfileName}' (File: {profile.FileName})";
                    profile.HasOldProfile = true;
                }
            }
            OptionRemoveOldProfiles = CountProfileUpdates > 0;
            checkTask.SetState($"Found {CountProfileUpdates} existing Profiles", TaskState.COMPLETED);
        }

        public async Task InstallPackageAsync()
        {
            IsInstalling = true;
            try
            {
                bool replaceOld = OptionRemoveOldProfiles;
                if (PackageFile.CountProfiles > 0)
                    CheckExistingProfiles();
                OptionRemoveOldProfiles = replaceOld;
                FilesInstalled = PackageFile.InstallPackage();
                PackageFile.Dispose();
                if (!FilesInstalled)
                    return;

                if (PackageFile.CountProfiles > 0)
                {
                    if (!await AddStreamDockProfilesAsync() || !await CheckProfilesInstalled())
                        return;
                    if (OptionRemoveOldProfiles)
                    {
                        MainWindow.SetForeground();
                        SwapProfiles();
                    }
                    else
                        MainWindow.SetForeground();
                }
                else
                    Logger.Debug($"Skipping Add & Swap for Package with no Profiles!");
                InstallationCompleted = !IsCanceled;
            }
            catch (Exception ex)
            {
                TaskStore.CurrentTask.SetError(ex);
            }
            finally
            {
                IsInstalling = false;
                if (InstallationCompleted || IsCanceled)
                    PackageFile.Cleanup();
            }
        }

        protected async Task<bool> AddStreamDockProfilesAsync()
        {
            Logger.Debug($"Installing Profiles in {Parameters.PlatformSoftwareName} ...");
            bool result = false;
            try
            {
                if (IsCanceled)
                    return false;
                // Start VSD Craft if it is not running so it can import the profiles.
                if (!FuncStreamDock.IsStreamDockRunning())
                {
                    Logger.Information($"Starting {Parameters.PlatformSoftwareName} for profile installation...");
                    var worker = new WorkerStreamDockStartStop<Config>(Config.Instance, DeckProcessOperation.START) { RefocusWindow = true, RefocusWindowTitle = MainWindow.AppTitle };
                    await worker.Run(System.Threading.CancellationToken.None);
                    if (IsCanceled || !FuncStreamDock.IsStreamDockRunning())
                        return false;
                }

                bool disableLinks = false;
                foreach (var profile in PackageFile.PackagedProfiles)
                {
                    Logger.Debug($"Adding Task for '{profile.FileName}'");
                    var task = TaskStore.Add(Localization.Translate("Add Profile '{0}' to {1}", profile.ProfileName, Parameters.PlatformName));
                    task.AddMessage(Localization.Translate("Click the Link to import the Profile into {0} (or click Ignore):", Parameters.PlatformName), true, false, false, FontWeights.DemiBold);
                    task.State = TaskState.WAITING;
                    task.DisplayCompleted = true;
                    var link = task.AddLink(profile.FileName, () => OpenProfileFile(profile, task));
                    task.DisableAllLinksOnClick = disableLinks;
                    link.DisableLinkOnClick = disableLinks;
                    link.LinkStyleBold = true;
                    link.LinkFontSize = 12;
                    link.StateOnLinkClicked = TaskState.ACTIVE;
                    link.ClickedCallback = () => { profile.ClickResponse = PackageClickResponse.Clicked; Logger.Debug($"Clicked Install for on '{profile.FileName}'"); };
                    link = task.AddLink(Localization.Translate("Ignore"), null);
                    link.DisableLinkOnClick = disableLinks;
                    link.LinkStyleBold = true;
                    link.LinkFontSize = 12;
                    link.StateOnLinkClicked = TaskState.COMPLETED;
                    link.ClickedCallback = () => { profile.ClickResponse = PackageClickResponse.Ignored; Logger.Debug($"Clicked Ignore for on '{profile.FileName}'"); };
                    ProfileTaskList.Add(task);
                    ProfileTasks.Add(profile.SourceFileName, task);
                }
                return true;
            }
            catch (Exception ex)
            {
                TaskStore.CurrentTask.SetError(ex);
                result = false;
            }

            return result;
        }

        protected static void OpenProfileFile(PackageFile.PackagedProfile profile, TaskModel task)
        {
            try
            {
                Logger.Debug($"Opening Profile File '{profile.InstallFilePath}' through ShellExecute");
                Process.Start(new ProcessStartInfo(profile.InstallFilePath) { UseShellExecute = true });
                SetStreamDockWindowForeground();
            }
            catch (Exception ex)
            {
                task.SetError($"Unable to open Profile '{profile.FileName}' for import: {ex.Message}");
                Logger.LogException(ex);
            }
        }

        private static void MessageWaitForProfilesInstalled(IEnumerable<PackageFile.PackagedProfile> query, TaskModel task)
        {
            string msg = $"Wait for Profiles to be added to {Parameters.PlatformName}:";
            foreach (var profile in query)
                msg += $"\r\n{profile.FileName}";
            task.ReplaceLastMessage(msg);
        }

        protected void CompleteTasks(IEnumerable<PackageFile.PackagedProfile> query)
        {
            foreach (var profile in query)
            {
                if (!ProfileTasks.TryGetValue(profile.SourceFileName, out var task))
                    continue;

                foreach (var link in task.Links)
                    link.WasNavigated = true;
                profile.IsLinkDisabled = true;
                task.State = TaskState.COMPLETED;
                task.IsCompleted = true;
            }
        }

        protected async Task<bool> CheckProfilesInstalled()
        {
            var task = TaskStore.Add(Localization.Translate("Install {0} Profiles to {1}", PackageFile.PackagedProfiles.Count, Parameters.PlatformName), Localization.Translate("Wait for all Profiles to be clicked ..."));
            task.DisplayCompleted = false;
            task.State = TaskState.WAITING;

            try
            {
                int waitDelay = 1000;
                while (PackageFile.PackagedProfiles.Where(p => p.ClickResponse == PackageClickResponse.NotClicked).Any() && !IsCanceled)
                {
                    await Task.Delay(waitDelay);
                    if (IsCanceled)
                        return false;
                    CompleteTasks(PackageFile.PackagedProfiles.Where(p => p.ClickResponse == PackageClickResponse.Ignored && !p.IsLinkDisabled));

                    if (!QueryProfilesInstalled(task, PackageFile.PackagedProfiles.Where(p => p.ClickResponse == PackageClickResponse.Clicked && !p.IsInstalled)))
                        return false;

                    CompleteTasks(PackageFile.PackagedProfiles.Where(p => p.IsInstalled && !p.IsLinkDisabled));
                }
                task.ReplaceLastMessage("All profiles clicked!");

                var query = PackageFile.PackagedProfiles.Where(p => !p.IsInstalled && p.ClickResponse != PackageClickResponse.Ignored);
                waitDelay = 1000;
                while (query.Any() && !IsCanceled)
                {
                    MessageWaitForProfilesInstalled(query, task);
                    await Task.Delay(waitDelay);
                    if (IsCanceled)
                        return false;

                    if (!QueryProfilesInstalled(task, query))
                        return false;

                    CompleteTasks(PackageFile.PackagedProfiles.Where(p => p.IsInstalled && !p.IsLinkDisabled));
                    CompleteTasks(PackageFile.PackagedProfiles.Where(p => p.ClickResponse == PackageClickResponse.Ignored && !p.IsLinkDisabled));
                    query = PackageFile.PackagedProfiles.Where(p => !p.IsInstalled && p.ClickResponse != PackageClickResponse.Ignored);
                }

                if (IsCanceled)
                    return false;
                CompleteTasks(PackageFile.PackagedProfiles.Where(p => !p.IsLinkDisabled));
                task.SetSuccess(Localization.Translate("All Profiles installed (or ignored)!"));
                task.IsCompleted = true;
            }
            catch (Exception ex)
            {
                TaskStore.CurrentTask.SetError(ex);
                return false;
            }

            return true;
        }

        protected bool QueryProfilesInstalled(TaskModel task, IEnumerable<PackageFile.PackagedProfile> query)
        {
            ProfileController.LoadManifestsOnly();
            if (ProfileController.IsLoaded && !ProfileController.HasError)
            {
                foreach (var profile in query)
                {
                    var installed = ProfileController.ProfileManifests.Where(m => !profile.ExistingDirectories.Contains(m.ProfileDirectory)
                        && !PackageFile.PackagedProfiles.Any(p => p.InstalledDirectory == m.ProfileDirectory)
                        && (m.ProfileName == profile.ProfileName || ProfileController.MatchManifestCopy(m.ProfileName, profile.ProfileName))).ToList();
                    if (installed.Count == 1)
                    {
                        profile.IsInstalled = true;
                        profile.InstalledDirectory = installed[0].ProfileDirectory;
                        Logger.Debug($"Match found for '{profile.ProfileName}'");
                    }
                }
            }
            else
            {
                task.SetError(Localization.Translate("Unable to read Profile Data from {0}!", Parameters.PlatformSoftwareName));
                return false;
            }

            return true;
        }

        protected void SwapProfiles()
        {
            var query = PackageFile.PackagedProfiles.Where(p => p.IsInstalled && p.HasOldProfile);
            if (!query.Any())
            {
                Logger.Debug($"No Profiles to swap!");
                return;
            }

            ProfileController.SwapUpdateManifest([.. query], () => IsCanceled);
            if (ProfileController.HasError)
                throw new InvalidOperationException("Profile replacement failed. Review the installation tasks.");
        }

        public static bool SetStreamDockWindowForeground()
        {
            Sys.SetForegroundWindow(Parameters.SD_WINDOW_NAME);
            return true;
        }

        public void Dispose()
        {
            Logger.Debug("Dispose");
            ProfileTaskList.Clear();
            IsCanceled = true;
            SwapTimer?.Stop();
            PackageFile?.Dispose();
            if (!IsInstalling)
                PackageFile?.Cleanup();
        }
    }
}
