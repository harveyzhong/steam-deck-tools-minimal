using CommonHelpers;
using ExternalHelpers;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;

namespace SteamController
{
    internal class Controller : IDisposable
    {
        public const String Title = "Steam Controller";
        public static readonly String TitleWithVersion = Title + " v" + Application.ProductVersion.ToString();

        public const int ControllerDelayAfterResumeMs = 1000;

        public static readonly Dictionary<String, Profiles.Profile> PreconfiguredUserProfiles = new Dictionary<String, Profiles.Profile>()
        {
            { "*.desktop.cs", new Profiles.Predefined.DesktopProfile() { Name = "Desktop" } },
            { "*.x360.cs", new Profiles.Predefined.X360HapticProfile() { Name = "X360" } }
        };

        Container components = new Container();
        NotifyIcon notifyIcon;
        StartupManager startupManager = new StartupManager(Title);

        Context context = new Context()
        {
            Profiles = {
                new Profiles.Predefined.DesktopProfile() { Name = "Desktop" },
                new Profiles.Predefined.X360HapticProfile() { Name = "X360", EmulateTouchPads = true },
            },
            Managers = {
                new Managers.ProcessManager(),
                new Managers.ProfileSwitcher(),
                new Managers.SharedDataManager(),
                new Managers.SASManager()
            }
        };

        static Controller()
        {
            Dependencies.ValidateHidapi(TitleWithVersion);
        }

        public Controller()
        {
            Instance.OnUninstall(() =>
            {
                startupManager.Startup = false;
            });

            Log.CleanupLogFiles(DateTime.UtcNow.AddDays(-7));
            Log.LogToFile = true;
            Log.LogToFileDebug = Settings.Default.EnableDebugLogging;

            Instance.RunOnce(TitleWithVersion, "Global\\SteamController");

            if (Instance.WantsRunOnStartup)
                startupManager.Startup = true;

            notifyIcon = new NotifyIcon(components);
            notifyIcon.Icon = WindowsDarkMode.IsDarkModeEnabled ? Resources.microsoft_xbox_controller_off_white : Resources.microsoft_xbox_controller_off;
            notifyIcon.Text = TitleWithVersion;
            notifyIcon.Visible = true;

#if DEBUG
            foreach (var profile in Profiles.Dynamic.RoslynDynamicProfile.GetUserProfiles(PreconfiguredUserProfiles))
            {
                profile.ErrorsChanged += (errors) =>
                {
                    notifyIcon.ShowBalloonTip(
                        3000, profile.Name,
                        String.Join("\n", errors),
                        ToolTipIcon.Error
                    );
                };
                profile.Compile();
                profile.Watch();
                context.Profiles.Add(profile);
            }
#endif

            // Set available profiles
            ProfilesSettings.Helpers.ProfileStringConverter.Profiles = context.Profiles;

            var contextMenu = new ContextMenuStrip(components);

            var enabledItem = new ToolStripMenuItem("&Enabled");
            enabledItem.Click += delegate { context.RequestEnable = !context.RequestEnable; };
            contextMenu.Opening += delegate { enabledItem.Checked = context.RequestEnable; };
            contextMenu.Items.Add(enabledItem);
            contextMenu.Items.Add(new ToolStripSeparator());

            foreach (var profile in context.Profiles)
            {
                if (profile.Name == "")
                    continue;

                var profileItem = new ToolStripMenuItem(profile.Name);
                profileItem.Click += delegate { context.SelectProfile(profile.Name); };
                contextMenu.Opening += delegate
                {
                    profileItem.Checked = context.CurrentProfile == profile;
                    profileItem.ToolTipText = String.Join("\n", profile.Errors ?? new string[0]);
                    profileItem.Enabled = profile.Errors is null;
                    profileItem.Visible = profile.Visible;
                };
                contextMenu.Items.Add(profileItem);
            }

            contextMenu.Items.Add(new ToolStripSeparator());

            var settingsItem = contextMenu.Items.Add("&Settings");
            settingsItem.Click += Settings_Click;

            var shortcutsItem = contextMenu.Items.Add("&Shortcuts");
            shortcutsItem.Click += delegate { Dependencies.OpenLink(Dependencies.SDTURL + "/shortcuts.html"); };

            contextMenu.Items.Add(new ToolStripSeparator());

            if (startupManager.IsAvailable)
            {
                var startupItem = new ToolStripMenuItem("Run On Startup");
                startupItem.Checked = startupManager.Startup;
                startupItem.Click += delegate { startupItem.Checked = startupManager.Startup = !startupManager.Startup; };
                contextMenu.Items.Add(startupItem);
            }

            var helpItem = contextMenu.Items.Add("&Help");
            helpItem.Click += delegate { Dependencies.OpenLink(Dependencies.SDTURL); };

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = contextMenu.Items.Add("&Exit");
            exitItem.Click += delegate { Application.Exit(); };

            notifyIcon.ContextMenuStrip = contextMenu;

            var contextStateUpdate = new System.Windows.Forms.Timer(components);
            contextStateUpdate.Interval = 250;
            contextStateUpdate.Enabled = true;
            contextStateUpdate.Tick += ContextStateUpdate_Tick;

            context.SelectDefault = () =>
            {
                if (!context.SelectProfile(Settings.Default.DefaultProfile, true))
                    context.SelectProfile(context.Profiles.First().Name, true);
            };
            context.BackToDefault();

            context.ProfileChanged += (profile) =>
            {
#if false
                notifyIcon.ShowBalloonTip(
                    1000,
                    TitleWithVersion,
                    String.Format("Selected profile: {0}", profile.Name),
                    ToolTipIcon.Info
                );
#endif
            };

            context.Start();

            Microsoft.Win32.SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            Log.TraceLine("SystemEvents_PowerModeChanged: {0}", e.Mode);

            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    context.Stop();
                    break;

                case PowerModes.Resume:
                    context.Start(ControllerDelayAfterResumeMs);
                    break;
            }
        }

        private void ContextStateUpdate_Tick(object? sender, EventArgs e)
        {
            context.Tick();

            var profile = context.CurrentProfile;

            if (!context.KeyboardMouseValid)
            {
                notifyIcon.Text = TitleWithVersion + ". Cannot send input.";
                if (WindowsDarkMode.IsDarkModeEnabled)
                    notifyIcon.Icon = Resources.monitor_off_white;
                else
                    notifyIcon.Icon = Resources.monitor_off;
            }
            else if (!context.X360.Valid)
            {
                notifyIcon.Text = TitleWithVersion + ". Missing ViGEm?";
                notifyIcon.Icon = Resources.microsoft_xbox_controller_red;
            }
            else if (profile is not null)
            {
                notifyIcon.Text = TitleWithVersion + ". Profile: " + profile.FullName;
                notifyIcon.Icon = profile.Icon;
            }
            else
            {
                notifyIcon.Text = TitleWithVersion + ". Disabled";
                if (WindowsDarkMode.IsDarkModeEnabled)
                    notifyIcon.Icon = Resources.microsoft_xbox_controller_off_white;
                else
                    notifyIcon.Icon = Resources.microsoft_xbox_controller_off;
            }

            notifyIcon.Text += String.Format(". Updates: {0}/s", context.UpdatesPerSec);
        }

        public void Dispose()
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            notifyIcon.Visible = false;
            context.Stop();
            using (context) { }
        }
        private void Settings_Click(object? sender, EventArgs e)
        {
            var form = new Form()
            {
                Text = TitleWithVersion + " Settings",
                StartPosition = FormStartPosition.CenterScreen,
                Size = new Size(400, 500),
                AutoScaleMode = AutoScaleMode.Font,
                AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F)
            };

            var propertyGrid = new PropertyGrid()
            {
                Dock = DockStyle.Fill,
                ToolbarVisible = false,
                LargeButtons = true,
                SelectedObject = new
                {
                    DesktopShortcuts = ProfilesSettings.DesktopPanelSettings.Default,
                    X360Shortcuts = ProfilesSettings.X360BackPanelSettings.Default,
                    X360Haptic = ProfilesSettings.HapticSettings.X360,
                    Application = Settings.Default,
#if DEBUG
                    DEBUG = SettingsDebug.Default
#endif
                }
            };

            var helpLabel = new Label()
            {
                Cursor = Cursors.Hand,
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold | FontStyle.Underline),
                ForeColor = SystemColors.HotTrack,
                Text = Dependencies.SDTURL,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var donateLabel = new Label()
            {
                Cursor = Cursors.Hand,
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Text = String.Join("\n",
                    "Consider donating if you are happy with this project."
                ),
                TextAlign = ContentAlignment.MiddleLeft,
                Height = 100
            };

            helpLabel.Click += delegate { Dependencies.OpenLink(Dependencies.SDTURL); };
            donateLabel.Click += delegate { Dependencies.OpenLink(Dependencies.SDTURL + "/#help-this-project"); };
            propertyGrid.ExpandAllGridItems();

            form.Controls.Add(propertyGrid);
            form.Controls.Add(donateLabel);
            form.Controls.Add(helpLabel);
            form.ShowDialog();
        }
    }
}
