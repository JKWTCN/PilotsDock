using CFIT.AppLogger;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace ProfileManager
{
    public partial class ViewProfileInstaller : UserControl
    {
        protected InstallPackageWorker InstallWorker { get; set; }
        protected Action<ViewProfileInstaller> ActionButtonConfirmation { get; set; } = null;
        public bool IsPackageActive { get; protected set; } = false;
        public DispatcherTimer TimerClearTasks { get; protected set; }

        public ViewProfileInstaller()
        {
            InitializeComponent();
            Localization.Apply(this);

            InstallWorker = new();

            LabelNotes.Background = this.Background;
            LabelNotes.BorderThickness = new Thickness(0);

            TimerClearTasks = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.75) };
            TimerClearTasks.Tick += (sender, args) =>
            {
                AreaTaskStatus.Deactivate(true, InstallWorker.CountProfileUpdates > 0);
                TimerClearTasks.Stop();
            };

            SetInitialVisibilityState();
        }

        public void OpenPackageFile(string filename)
        {
            SetStateLoadingPackage(filename);
        }

        public void SetInitialVisibilityState()
        {
            AreaTaskStatus.Visibility = Visibility.Collapsed;
            AreaFileDrop.Visibility = Visibility.Visible;
            AreaPackageInfo.Visibility = Visibility.Collapsed;
            AreaButtons.Visibility = Visibility.Collapsed;
            LabelInstallerNotice.Visibility = Visibility.Collapsed;

            ButtonConfirmation.Visibility = Visibility.Visible;
        }

        public void SetStateOpenPackage()
        {
            SetInitialVisibilityState();
            CheckboxKeepContents.Visibility = Visibility.Visible;
            CheckboxRemoveOld.Visibility = Visibility.Visible;
            CheckboxKeepContents.IsEnabled = true;
            CheckboxRemoveOld.IsEnabled = true;

            IsPackageActive = false;

            TimerClearTasks.Stop();
            AreaTaskStatus.Deactivate(true);

            TreeFileContents.Items.Clear();
            TreeRemoveFiles.Items.Clear();
            InstallWorker?.Dispose();
            InstallWorker = new();

            ActionButtonConfirmation = null;
        }

        protected void SetStateLoadingPackage(string filePath)
        {
            AreaTaskStatus.Visibility = Visibility.Visible;
            AreaFileDrop.Visibility = Visibility.Collapsed;
            AreaPackageInfo.Visibility = Visibility.Collapsed;
            AreaButtons.Visibility = Visibility.Collapsed;

            IsPackageActive = true;

            InstallWorker.SetFile(filePath);

            AreaTaskStatus.Activate(SetStateShowPackage, SetStateLoadFailed);

            Task.Run(InstallWorker.LoadPackage);
        }

        protected void SetStateLoadFailed()
        {
            AreaButtons.Visibility = Visibility.Visible;
            AreaTaskStatus.Deactivate();
            SetButtonState(false, true, false);
        }

        protected void SetStateShowPackage()
        {
            AreaTaskStatus.Visibility = Visibility.Visible;
            AreaFileDrop.Visibility = Visibility.Collapsed;
            AreaPackageInfo.Visibility = Visibility.Visible;
            AreaButtons.Visibility = Visibility.Visible;

            SetButtonState(InstallWorker.IsValid, !InstallWorker.IsValid, false, Localization.Translate("Install"), "box-arrow-in-right", Localization.Translate("Start Installation!"));
            ActionButtonConfirmation = v => v.SetStateInstallingPackage();

            TimerClearTasks.Start();

            ShowPackageInfo();
        }

        protected async void SetStateInstallingPackage()
        {
            AreaTaskStatus.Visibility = Visibility.Visible;
            AreaFileDrop.Visibility = Visibility.Collapsed;
            AreaPackageInfo.Visibility = Visibility.Collapsed;
            AreaButtons.Visibility = Visibility.Visible;
            LabelInstallerNotice.Visibility = Visibility.Visible;

            SetButtonState(false, true, false);

            TimerClearTasks.Stop();
            AreaTaskStatus.Activate(SetStateInstalledPackage, SetStateInstalledPackage);

            await InstallWorker.InstallPackageAsync();
        }

        protected virtual void CloseInstaller(ViewProfileInstaller v)
        {
            if (MainWindow.Instance.InstallProfileCommandline)
                MainWindow.Instance.Close();
            else
                v.SetStateOpenPackage();
        }

        protected void SetStateInstalledPackage()
        {
            AreaTaskStatus.Visibility = Visibility.Visible;
            AreaFileDrop.Visibility = Visibility.Collapsed;
            AreaPackageInfo.Visibility = Visibility.Collapsed;
            AreaButtons.Visibility = Visibility.Visible;
            LabelInstallerNotice.Visibility = Visibility.Collapsed;

            AreaTaskStatus.Deactivate();

            if (InstallWorker.IsInstalled)
            {
                ActionButtonConfirmation = v => CloseInstaller(v);
                SetButtonState(true, false, true, Localization.Translate("Close"), "check-square", Localization.Translate("Close this View"));
            }
            else
            {
                SetButtonState(false, true, false);
            }

            IsPackageActive = false;
        }

        protected void SetButtonState(bool success, bool hideConfirmation, bool hideCandel = false, string caption = null, string file = null, string tooltip = null)
        {
            ButtonConfirmation.IsEnabled = success;

            if (hideConfirmation)
                ButtonConfirmation.Visibility = Visibility.Collapsed;
            else
                ButtonConfirmation.Visibility = Visibility.Visible;

            if (hideCandel)
                ButtonCancel.Visibility = Visibility.Collapsed;
            else
                ButtonCancel.Visibility = Visibility.Visible;

            if (caption != null)
                LabelButtonConfirmation.Text = caption;

            if (tooltip != null)
                ButtonConfirmation.ToolTip = tooltip;

            if (ImageButtonConfirmation != null && !string.IsNullOrEmpty(file))
                Tools.SetButtonImage(ImageButtonConfirmation, file);
        }

        protected void ShowPackageInfo()
        {
            try
            {
                Logger.Debug($"Showing Package Info for '{InstallWorker.PackageFile?.Title}'");

                var PackageFile = InstallWorker.PackageFile;

                LabelTitle.Text = PackageFile.Title ?? "";

                LabelPackageVersion.Text = PackageFile.VersionPackage ?? "";

                LabelAircraft.Text = PackageFile.Aircraft ?? "";

                LabelAuthor.Text = PackageFile.Author ?? "";

                LabelURL.Inlines.Clear();
                if (!string.IsNullOrWhiteSpace(PackageFile.URL) && Uri.TryCreate(PackageFile.URL, UriKind.RelativeOrAbsolute, out Uri urlResult))
                {
                    Hyperlink link = new(new Run(PackageFile.URL))
                    {
                        NavigateUri = urlResult,
                    };
                    LabelURL.Inlines.Add(link);
                    LabelURL.AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(RequestNavigateHandler));
                }

                LabelNotes.Text = PackageFile.Notes ?? "";

                List<string> displayList = [];
                foreach (var profile in PackageFile.PackagedProfiles)
                {
                    if (profile.HasOldProfile)
                        displayList.Add(Localization.Translate("Update: {0}", profile.FileName));
                    else
                        displayList.Add(Localization.Translate("New: {0}", profile.FileName));
                }
                AddTreeItems(Localization.Translate("{0} Profiles", PackageFile.CountProfiles), displayList, null, true);
                AddTreeItems(Localization.Translate("{0} StreamDeck Profiles (conversion required)", PackageFile.CountStreamDeckProfiles), PackageFile.FilesStreamDeckProfiles, new SolidColorBrush(Colors.Orange), true);
                AddTreeItems(Localization.Translate("{0} Images", PackageFile.CountImages), PackageFile.FilesImages);
                AddTreeItems(Localization.Translate("{0} Scripts", PackageFile.CountScripts), PackageFile.FilesScripts);
                AddTreeItems(Localization.Translate("{0} Extras", PackageFile.CountExtras), PackageFile.FilesExtras);
                AddTreeItems(Localization.Translate("{0} Unknown", PackageFile.FilesUnknown.Count), PackageFile.FilesUnknown, new SolidColorBrush(Colors.Orange));
                if (PackageFile.Manifest.RemoveFiles.Count > 0)
                {
                    AddTreeItems(Localization.Translate("{0} Remove", PackageFile.Manifest.RemoveFiles.Count), PackageFile.Manifest.RemoveFiles, new SolidColorBrush(Colors.Orange), true, TreeRemoveFiles);
                    LabelRemoveFiles.Visibility = Visibility.Visible;
                    TreeRemoveFiles.Visibility = Visibility.Visible;
                }
                else
                {
                    LabelRemoveFiles.Visibility = Visibility.Collapsed;
                    TreeRemoveFiles.Visibility = Visibility.Collapsed;
                }

                CheckboxRemoveOld.IsChecked = InstallWorker.OptionRemoveOldProfiles;
                if (PackageFile.HasStreamDeckProfiles)
                {
                    LabelRemoveOld.Visibility = Visibility.Collapsed;
                    CheckboxRemoveOld.Visibility = Visibility.Collapsed;
                }
                else if (InstallWorker.CountProfileUpdates > 0)
                {
                    LabelRemoveOld.Visibility = Visibility.Visible;
                    CheckboxRemoveOld.Visibility = Visibility.Visible;
                }
                else
                {
                    LabelRemoveOld.Visibility = Visibility.Collapsed;
                    CheckboxRemoveOld.Visibility = Visibility.Collapsed;
                }

                CheckboxKeepContents.IsChecked = false;
                if (PackageFile.HasStreamDeckProfiles)
                {
                    CheckboxKeepContents.IsChecked = true;
                    CheckboxKeepContents.IsEnabled = false;
                    LabelKeepContents.ToolTip = Localization.Translate("StreamDock profiles must stay extracted so they can be converted.");
                    CheckboxKeepContents.ToolTip = Localization.Translate("StreamDeck profiles must stay extracted so they can be converted.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                MessageBox.Show($"Error while displaying Package: {ex.Message}", ex.GetType().ToString(), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected void AddTreeItems(string header, List<string> items, Brush brush = null, bool expand = false, TreeView treeView = null)
        {
            if (items.Count == 0)
                return;

            var treeitem = new TreeViewItem() { Header = header };
            if (brush != null)
                treeitem.Foreground = brush;

            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item))
                    treeitem.Items.Add(new TreeViewItem() { Header = item });
            }
            treeitem.IsExpanded = expand;
            if (treeView == null)
                TreeFileContents.Items.Add(treeitem);
            else
                treeView.Items.Add(treeitem);
        }

        protected void RequestNavigateHandler(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Tools.OpenUri(sender, e);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                MessageBox.Show($"{ex.GetType()} - {ex.Message}", Localization.Translate("Error loading URL"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ButtonOpenPackage_Drop(object sender, DragEventArgs e)
        {
            try
            {
                Logger.Debug($"Received File Drop");

                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files.Length > 0)
                    {
                        SetStateLoadingPackage(files[0]);
                        MainWindow.SetForeground();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                MessageBox.Show($"Error while reading Package: {ex.Message}", ex.GetType().ToString(), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ButtonOpenPackage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Debug($"Opening File Dialog");

                OpenFileDialog openFileDialog = new()
                {
                    Title = Localization.Translate("Open Profile Package ..."),
                    Filter = Localization.Translate("{0} (*{1})|*{1}|Zip File (*.zip)|*.zip|All files (*.*)|*.*", Parameters.PACKAGE_EXTENSION_NAME, Parameters.PACKAGE_EXTENSION)
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    SetStateLoadingPackage(openFileDialog.FileName);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                MessageBox.Show($"Error while reading Package: {ex.Message}", ex.GetType().ToString(), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ButtonConfirmation_Click(object sender, RoutedEventArgs e)
        {
            Logger.Debug($"Confirming Installation (action {ActionButtonConfirmation?.Method})");

            ActionButtonConfirmation?.Invoke(this);
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            Logger.Debug($"Cancel Installation");

            try
            {
                SetStateOpenPackage();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                MessageBox.Show($"Error resetting State: {ex.Message}", ex.GetType().ToString(), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void Dispose()
        {
            InstallWorker?.Dispose();
            AreaTaskStatus.Deactivate(true);
            TimerClearTasks.Stop();
        }

        private void CheckboxRemoveOld_Click(object sender, RoutedEventArgs e)
        {
            InstallWorker.OptionRemoveOldProfiles = !InstallWorker.OptionRemoveOldProfiles;
            CheckboxRemoveOld.IsChecked = InstallWorker.OptionRemoveOldProfiles;
        }

        private void CheckboxKeepContents_Click(object sender, RoutedEventArgs e)
        {
            InstallWorker.PackageFile.KeepPackageContents = !InstallWorker.PackageFile.KeepPackageContents;
            CheckboxKeepContents.IsChecked = InstallWorker.PackageFile.KeepPackageContents;
        }

        private void TreeFileContents_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = MouseWheelEvent,
                    Source = sender
                };
                var parent = ((Control)sender).Parent as UIElement;
                parent.RaiseEvent(eventArg);
            }
        }
    }
}
