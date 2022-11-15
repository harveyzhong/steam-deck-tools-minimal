﻿using CommonHelpers;
using CommonHelpers.FromLibreHardwareMonitor;
using Microsoft.VisualBasic.Logging;
using PowerControl.External;
using RTSSSharedMemoryNET;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Windows.Forms.AxHost;

namespace PowerControl
{
    internal class Controller : IDisposable
    {
        public const String Title = "Power Control";
        public readonly String TitleWithVersion = Title + " v" + Application.ProductVersion.ToString();
        public const int KeyPressRepeatTime = 300;
        public const int KeyPressNextRepeatTime = 150;

        Container components = new Container();
        System.Windows.Forms.NotifyIcon notifyIcon;
        StartupManager startupManager = new StartupManager(Title);

        Menu.MenuRoot rootMenu = MenuStack.Root;
        OSD osd;
        System.Windows.Forms.Timer osdDismissTimer;

        hidapi.HidDevice neptuneDevice = new hidapi.HidDevice(0x28de, 0x1205, 64);
        SDCInput neptuneDeviceState = new SDCInput();
        DateTime? neptuneDeviceNextKey;
        System.Windows.Forms.Timer neptuneTimer;

        public Controller()
        {
            Instance.RunOnce(TitleWithVersion, "Global\\PowerControl");

            var contextMenu = new System.Windows.Forms.ContextMenuStrip(components);

            contextMenu.Opening += delegate (object? sender, CancelEventArgs e)
            {
                rootMenu.Update();
            };

            rootMenu.Visible = false;
            rootMenu.Update();
            rootMenu.CreateMenu(contextMenu.Items);
            rootMenu.VisibleChanged = delegate ()
            {
                updateOSD();
            };
            contextMenu.Items.Add(new ToolStripSeparator());

            if (startupManager.IsAvailable)
            {
                var startupItem = new ToolStripMenuItem("Run On Startup");
                startupItem.Checked = startupManager.Startup;
                startupItem.Click += delegate
                {
                    startupManager.Startup = !startupManager.Startup;
                    startupItem.Checked = startupManager.Startup;
                };
                contextMenu.Items.Add(startupItem);
            }

            var helpItem = contextMenu.Items.Add("&Help");
            helpItem.Click += delegate
            {
                System.Diagnostics.Process.Start("explorer.exe", "http://github.com/ayufan-research/steam-deck-tools");
            };

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = contextMenu.Items.Add("&Exit");
            exitItem.Click += ExitItem_Click;

            notifyIcon = new System.Windows.Forms.NotifyIcon(components);
            notifyIcon.Icon = Resources.poll;
            notifyIcon.Text = TitleWithVersion;
            notifyIcon.Visible = true;
            notifyIcon.ContextMenuStrip = contextMenu;

            osdDismissTimer = new System.Windows.Forms.Timer(components);
            osdDismissTimer.Interval = 3000;
            osdDismissTimer.Tick += delegate(object ? sender, EventArgs e)
            {
                hideOSD();
            };

            var osdTimer = new System.Windows.Forms.Timer(components);
            osdTimer.Tick += OsdTimer_Tick;
            osdTimer.Interval = 250;
            osdTimer.Enabled = true;

            GlobalHotKey.RegisterHotKey(Settings.Default.MenuUpKey, () =>
            {
                rootMenu.Prev();
                setDismissTimer();
            });

            GlobalHotKey.RegisterHotKey(Settings.Default.MenuDownKey, () =>
            {
                rootMenu.Next();
                setDismissTimer();
            });

            GlobalHotKey.RegisterHotKey(Settings.Default.MenuLeftKey, () =>
            {
                rootMenu.SelectPrev();
                setDismissTimer();
            });

            GlobalHotKey.RegisterHotKey(Settings.Default.MenuRightKey, () =>
            {
                rootMenu.SelectNext();
                setDismissTimer();
            });

            if (Settings.Default.EnableNeptuneController)
            {
                neptuneTimer = new System.Windows.Forms.Timer(components);
                neptuneTimer.Interval = 1000 / 30;
                neptuneTimer.Tick += NeptuneTimer_Tick;
                neptuneTimer.Enabled = true;

                neptuneDevice.OnInputReceived += NeptuneDevice_OnInputReceived;
                neptuneDevice.OpenDevice();
                neptuneDevice.BeginRead();
            }
        }

        private bool isForeground()
        {
            try
            {
                var processId = Helpers.TopLevelWindow.GetTopLevelProcessId();
                if (processId is null)
                    return true;

                foreach (var app in OSD.GetAppEntries(AppFlags.MASK))
                {
                    if (app.ProcessId == processId)
                        return true;
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private void OsdTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                notifyIcon.Text = TitleWithVersion + ". RTSS Version: " + OSD.Version;
                notifyIcon.Icon = Resources.poll;
            }
            catch
            {
                notifyIcon.Text = TitleWithVersion + ". RTSS Not Available.";
                notifyIcon.Icon = Resources.poll_red;
            }

            updateOSD();
        }

        private void NeptuneDevice_OnInputReceived(object? sender, hidapi.HidDeviceInputReceivedEventArgs e)
        {
            var input = SDCInput.FromBuffer(e.Buffer);

            var filteredInput = new SDCInput()
            {
                buttons0 = input.buttons0,
                buttons1 = input.buttons1,
                buttons2 = input.buttons2,
                buttons3 = input.buttons3,
                buttons4 = input.buttons4,
                buttons5 = input.buttons5
            };

            if (!neptuneDeviceState.Equals(filteredInput))
            {
                neptuneDeviceState = filteredInput;
                neptuneDeviceNextKey = null;
            }

            // Consume only some events to avoid under-running SWICD
            if ((input.buttons5 & (byte)SDCButton5.BTN_QUICK_ACCESS) != 0)
                Thread.Sleep(1000 / 30);
            else
                Thread.Sleep(250);
        }

        private void NeptuneTimer_Tick(object? sender, EventArgs e)
        {
            var input = neptuneDeviceState;

            if (neptuneDeviceNextKey == null)
                neptuneDeviceNextKey = DateTime.UtcNow.AddMilliseconds(KeyPressRepeatTime);
            else if (neptuneDeviceNextKey < DateTime.UtcNow)
                neptuneDeviceNextKey = DateTime.UtcNow.AddMilliseconds(KeyPressNextRepeatTime);
            else
                return; // otherwise it did not yet trigger

            if ((input.buttons5 & (byte)SDCButton5.BTN_QUICK_ACCESS) == 0 || !isForeground())
            {
                hideOSD();
                return;
            }

            rootMenu.Show();
            setDismissTimer(false);

            if (input.buttons1 != 0 || input.buttons2 != 0 || input.buttons3 != 0 || input.buttons4 != 0)
            {
                return;
            }
            else if (input.buttons0 == (ushort)SDCButton0.BTN_DPAD_LEFT)
            {
                rootMenu.SelectPrev();
            }
            else if (input.buttons0 == (ushort)SDCButton0.BTN_DPAD_RIGHT)
            {
                rootMenu.SelectNext();
            }
            else if (input.buttons0 == (ushort)SDCButton0.BTN_DPAD_UP)
            {
                rootMenu.Prev();
            }
            else if (input.buttons0 == (ushort)SDCButton0.BTN_DPAD_DOWN)
            {
                rootMenu.Next();
            }
        }

        private void setDismissTimer(bool enabled = true)
        {
            osdDismissTimer.Stop();
            if (enabled)
                osdDismissTimer.Start();
        }

        private void hideOSD()
        {
            if (!rootMenu.Visible)
                return;

            rootMenu.Visible = false;
            osdDismissTimer.Stop();
            updateOSD();
        }

        public void updateOSD()
        {
            if (!rootMenu.Visible)
            {
                osdClose();
                return;
            }

            try
            {
                // recreate OSD if not index 0
                if (osd != null && osd.OSDIndex() == 0)
                    osdClose();
                if (osd == null)
                    osd = new OSD("Power Control");
                osd.Update(rootMenu.Render(null));
            }
            catch (SystemException)
            {
            }
        }

        private void ExitItem_Click(object? sender, EventArgs e)
        {
            Application.Exit();
        }

        public void Dispose()
        {
            components.Dispose();
            osdClose();
        }

        private void osdClose()
        {
            try
            {
                if (osd != null)
                    osd.Dispose();
                osd = null;
            }
            catch (SystemException)
            {
            }
        }
    }
}