// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ExternalHelpers
{
    /// <summary>
    /// Taken and adapter from: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LibreHardwareMonitor/UI/StartupManager.cs
    /// </summary>
    public class StartupManager
    {
        private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private bool _startup;

        public string NameOf { get; set; }
        public string Description { get; set; }

        public StartupManager(string name, string description = null)
        {
            NameOf = name;
            if (description != null)
                Description = description;
            else
                Description = string.Format("Starts {0} on Windows startup", name);

            if (Environment.OSVersion.Platform >= PlatformID.Unix)
            {
                IsAvailable = false;
                return;
            }


            try
            {
                using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    string value = (string)registryKey?.GetValue(NameOf);

                    if (value != null)
                        _startup = value == Application.ExecutablePath;
                }

                IsAvailable = true;
            }
            catch (SecurityException)
            {
                IsAvailable = false;
            }
        }

        public bool IsAvailable { get; }

        public bool Startup
        {
            get { return _startup; }
            set
            {
                if (_startup != value)
                {
                    if (IsAvailable)
                    {
                        try
                        {
                            if (value)
                                CreateRegistryKey();
                            else
                                DeleteRegistryKey();

                            _startup = value;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            throw new InvalidOperationException();
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException();
                    }
                }
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);

                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private void CreateRegistryKey()
        {
            RegistryKey registryKey = Registry.CurrentUser.CreateSubKey(RegistryPath);
            registryKey?.SetValue(NameOf, Application.ExecutablePath);
        }

        private void DeleteRegistryKey()
        {
            RegistryKey registryKey = Registry.CurrentUser.CreateSubKey(RegistryPath);
            registryKey?.DeleteValue(NameOf);
        }
    }
}
