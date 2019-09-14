namespace Meadow
{
    using EnvDTE;
    using EnvDTE80;
    using Meadow.Helpers;
    using MeadowCLI.DeviceManagement;
    using Microsoft.VisualStudio.Shell;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.Composition;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Windows;
    using System.Windows.Controls;

    /// <summary>
    /// Interaction logic for MeadowWindowControl.
    /// </summary>
    public partial class MeadowWindowControl : UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MeadowWindowControl"/> class.
        /// </summary>
        public MeadowWindowControl()
        {
            this.InitializeComponent();
        }

        //[SuppressMessage("Microsoft.Globalization", "CA1300:SpecifyMessageBoxOptions", Justification = "Sample code")]
        //[SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:ElementMustBeginWithUpperCaseLetter", Justification = "Default event handler naming pattern")]

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshDeviceList();
        }

        public void RefreshDeviceList()
        {
            MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath);

            Devices.Items.Clear();
            Devices.Items.Add("None");

            var devices = MeadowDeviceManager.FindSerialDevices();
            var selectedIndex = 0;

            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i] == settings.DeviceTarget)
                {
                    selectedIndex = i + 1;
                }
                Devices.Items.Add(devices[i]);
            }

            Devices.SelectedIndex = selectedIndex;
        }

        private void Devices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Devices.SelectedIndex == 0 || Devices.SelectedItem == null) return;
            
            MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath, false);
            settings.DeviceTarget = Devices.SelectedItem.ToString();
            settings.Save();
        }
    }
}