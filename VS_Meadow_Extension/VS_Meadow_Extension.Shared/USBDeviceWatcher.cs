using System;
using System.Collections.Generic;
using System.Management;
using System.Text;

namespace Meadow
{
	public class USBDeviceWatcher
	{
		private ManagementEventWatcher deviceInsertedWatcher;
		private ManagementEventWatcher deviceRemovedWatcher;

		public event EventHandler DeviceInserted;
		public event EventHandler DeviceRemoved;

		public USBDeviceWatcher()
		{
			StartListening();
		}

		public void StartListening()
		{
			// Listen for USB device insertion
			var insertQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2");
			deviceInsertedWatcher = new ManagementEventWatcher(insertQuery);
			deviceInsertedWatcher.EventArrived += OnDeviceInserted;
			deviceInsertedWatcher.Start();

			// Listen for USB device removal
			var removeQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3");
			deviceRemovedWatcher = new ManagementEventWatcher(removeQuery);
			deviceRemovedWatcher.EventArrived += OnDeviceRemoved;
			deviceRemovedWatcher.Start();
		}

		public void StopListening()
		{
			deviceInsertedWatcher.Stop();
			deviceRemovedWatcher.Stop();
		}

		private void OnDeviceInserted(object sender, EventArrivedEventArgs e)
		{
			DeviceInserted?.Invoke(this, EventArgs.Empty);
		}

		private void OnDeviceRemoved(object sender, EventArrivedEventArgs e)
		{
			DeviceRemoved?.Invoke(this, EventArgs.Empty);
		}
	}
}