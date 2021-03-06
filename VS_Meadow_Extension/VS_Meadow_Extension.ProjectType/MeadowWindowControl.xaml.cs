﻿namespace Meadow
{
    using Meadow.Helpers;
    using MeadowCLI.DeviceManagement;
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Interaction logic for MeadowWindowControl.
    /// </summary>
    public partial class MeadowWindowControl : UserControl
    {
        static Guid windowGuid = new Guid("AD01DF73-6990-4361-8587-4FC3CB91A65F");

        /// <summary>
        /// Initializes a new instance of the <see cref="MeadowWindowControl"/> class.
        /// </summary>
        public MeadowWindowControl()
        {
            this.InitializeComponent();
            Devices.DisplayMemberPath = "Caption";
            Devices.SelectedValuePath = "Port";
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshDeviceList();
        }

        public void RefreshDeviceList()
        {
            MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath);

            Devices.Items.Clear();
            Devices.Items.Add(new SerialDevice { Caption = "Select Target Device Port" });
            Devices.SelectedIndex = 0;

            var index = 1;
            var captions = MeadowDeviceManager.GetSerialDeviceCaptions();
            foreach (var c in captions.Distinct())
            {
                var port = Regex.Match(c, @"(?<=\().+?(?=\))").Value;
                Devices.Items.Add(new SerialDevice()
                {
                    Caption = c,
                    Port = port
                });

                if (port == settings.DeviceTarget) { Devices.SelectedIndex = index; }
                index++;
            }
        }

        private void Devices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Devices.SelectedIndex <= 0) return;

            MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath, false);
            settings.DeviceTarget = Devices.SelectedValue.ToString();
            settings.Save();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }
    }
}
