using CFIT.AppLogger;
using CFIT.AppTools;
using CFIT.Installer.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MessageBox = System.Windows.Forms.MessageBox;
namespace ProfileManager
{
    public class InstallPackageWorker
    {
        protected ProfileController ProfileController { get; set; } = new();
        protected bool IsCanceled { get; set; } = false;
        public PackageFile PackageFile { get; protected set; } = new(null);

        public bool OptionRemoveOldProfiles { get; set; } = false;

        public bool IsValid { get { return IsChecked && IsLoaded && IsCompatible; } }
        public bool IsChecked { get; protected set; } = false;
        public bool IsLoaded { get; protected set; } = false;
        public bool IsCompatible { get { return PackageFile.IsCompatible; } }
        public bool FilesInstalled { get; protected set; } = false;
        public bool IsInstalled { get { return FilesInstalled; } }

        public int CountValidFiles { get { return PackageFile.CountValidTotal; } }
        public int CountTotalFiles { get { return PackageFile.CountValidTotal + PackageFile.FilesUnknown.Count; } }
        public int CountProfileUpdates { get { return PackageFile.PackagedProfiles.Where(p => p.HasOldProfile).Count(); } }

        public void SetFile(string filePath)
        {
            PackageFile = new(filePath);
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
                TaskStore.CurrentTask.SetError(ex);
            }

            return IsValid;
        }

        public void CheckExistingProfiles()
        {
            var checkTask = TaskStore.Add($"Check existing Profiles", "");
            checkTask.DisplayCompleted = false;
            ProfileController.Load();
            foreach (var profile in PackageFile.PackagedProfiles)
            {
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
            try
            {
                FilesInstalled = PackageFile.InstallPackage();
                PackageFile.Dispose();
                if (!FilesInstalled)
                    return;

                if (PackageFile.CountProfiles > 0)
                {
                    string _temp = PackageFile.PackagedProfiles[0].InstallPath.Trim('"');
                    string argument = $"/select,\"{_temp}\"";
                    Process.Start("explorer.exe", argument);
                    MainWindow.SetForeground();
                    await SwapProfilesAsync();
                    string message = "You can use the Deck Scene Conversion plugin in SPACE to perform the conversion.\n\n" +
               "Would you like to open the plugin page to learn more?";
                    string caption = "Plugin Suggestion";

                    DialogResult result = MessageBox.Show(message, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                    if (result == DialogResult.Yes)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo("https://space.key123.vip/product?id=20251206002211")
                            {
                                UseShellExecute = true
                            });
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                            MessageBox.Show("Could not open the browser. Please visit: https://space.key123.vip/product?id=20251206002211");
                        }
                    }
                    MainWindow.SetForeground();
                }
                else
                    Logger.Debug($"Skipping Add & Swap for Package with no Profiles!");
            }
            catch (Exception ex)
            {
                TaskStore.CurrentTask.SetError(ex);
            }
        }

        protected async Task SwapProfilesAsync()
        {
            var query = PackageFile.PackagedProfiles.Where(p => p.IsInstalled && p.HasOldProfile);
            if (!query.Any())
            {
                Logger.Debug($"No Profiles to swap!");
                return;
            }

            await ProfileController.SwapUpdateManifest([.. query.Select(p => p.ProfileName)]);
        }

        public void Dispose()
        {
            Logger.Debug("Dispose");
            IsCanceled = true;
            PackageFile?.Dispose();
            PackageFile = null;
        }
    }
}
