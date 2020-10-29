using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Meadow.Helpers;
using Meadow.Utility;
using MeadowCLI.DeviceManagement;
using MeadowCLI.Hcom;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace Meadow
{
    [Export(typeof(IDeployProvider))]
    [AppliesTo("Meadow")]
    internal class DeployProvider : IDeployProvider
    {
        /// <summary>
        /// Provides access to the project's properties.
        /// </summary>
        [Import]
        private ProjectProperties Properties { get; set; }

        private string _outputPath { get; set; }
        private readonly string _systemHttpNetDllName = "System.Net.Http.dll";

        public async Task DeployAsync(CancellationToken cts, TextWriter outputPaneWriter)
        {
            var generalProperties = await this.Properties.GetConfigurationGeneralPropertiesAsync();
            var projectDir = await generalProperties.Rule.GetPropertyValueAsync("ProjectDir");
            _outputPath = Path.Combine(projectDir, await generalProperties.Rule.GetPropertyValueAsync("OutputPath"));

            MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath);

            if (string.IsNullOrEmpty(settings.DeviceTarget))
            {
                throw new Exception("Device has not been selected. Hit Ctrl+Shift+M to access the Device list.");
            }

            var attachedDevices = MeadowDeviceManager.FindSerialDevices();
            if (!attachedDevices.Contains(settings.DeviceTarget))
            {
                throw new Exception($"Device on '{settings.DeviceTarget}' is not connected or busy.");
            }

            await DeployAppAsync(settings.DeviceTarget, Path.Combine(projectDir, _outputPath), new OutputPaneWriter(outputPaneWriter), cts).ConfigureAwait(false);
        }

        async Task DeployAppAsync(string target, string folder, IOutputPaneWriter outputPaneWriter, CancellationToken cts)
        {
            Stopwatch sw = Stopwatch.StartNew();
            await outputPaneWriter.WriteAsync($"Deploying to Meadow on {target}...");

            try
            {
                var meadow = await MeadowDeviceManager.GetMeadowForSerialPort(target).ConfigureAwait(false);

                if (meadow == null)
                {
                    await outputPaneWriter.WriteAsync($"Device connection error, please disconnect device and try again.");
                    return;
                }

                await MeadowDeviceManager.ResetMeadow(meadow).ConfigureAwait(false);
                await Task.Delay(1000);
                meadow = await MeadowDeviceManager.GetMeadowForSerialPort(target).ConfigureAwait(false);
                await Task.Delay(1000);

                await MeadowDeviceManager.MonoDisable(meadow).ConfigureAwait(false);
                
                var meadowFiles = await GetFilesOnDevice(meadow, outputPaneWriter, cts).ConfigureAwait(false);
                var localFiles = await GetLocalFiles(outputPaneWriter, cts, folder).ConfigureAwait(false);
                await DeleteUnusedFiles(meadow, outputPaneWriter, cts, meadowFiles, localFiles).ConfigureAwait(false);
                await DeployApp(meadow, outputPaneWriter, cts, folder, meadowFiles, localFiles).ConfigureAwait(false);
                await MeadowDeviceManager.MonoEnable(meadow).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw ex;
            }

            sw.Stop();
            await outputPaneWriter.WriteAsync($"Deployment Duration: {sw.Elapsed}");
        }

        async Task<(List<string> files, List<UInt32> crcs)> GetFilesOnDevice(MeadowSerialDevice meadow, IOutputPaneWriter outputPaneWriter, CancellationToken cts)
        {
            if (cts.IsCancellationRequested) { return (new List<string>(), new List<UInt32>()); }

            await outputPaneWriter.WriteAsync("Checking files on device (may take several seconds)");

            var meadowFiles = await meadow.GetFilesAndCrcs().ConfigureAwait(false);

            foreach (var f in meadowFiles.files)
            {
                if (cts.IsCancellationRequested) break;
                await outputPaneWriter.WriteAsync($"Found {f}");
            }

            if (meadowFiles.files.Count == 0)
            {
                await outputPaneWriter.WriteAsync($"Deploying for the first time may take several minutes.");
            }

            return meadowFiles;
        }

        async Task<(List<string> files, List<UInt32> crcs)> GetLocalFiles(IOutputPaneWriter outputPaneWriter, CancellationToken cts, string folder)
        {
            // get list of files in folder
            // var files = Directory.GetFiles(folder, "*.dll");

            CopySystemNetHttpDll();

            var extensions = new List<string> { ".exe", ".bmp", ".jpg", ".jpeg", ".json", ".xml", ".yml", ".txt" };

            var paths = Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
            .Where(s => extensions.Contains(new FileInfo(s).Extension));

            var files = new List<string>();
            var crcs = new List<UInt32>();

            foreach (var file in paths)
            {
                if (cts.IsCancellationRequested) break;

                using (FileStream fs = File.Open(file, FileMode.Open))
                {
                    var len = (int)fs.Length;
                    var bytes = new byte[len];

                    fs.Read(bytes, 0, len);

                    //0x
                    var crc = CrcTools.Crc32part(bytes, len, 0);// 0x04C11DB7);

                    Console.WriteLine($"{file} crc is {crc}");
                    files.Add(Path.GetFileName(file));
                    crcs.Add(crc);
                }
            }

            var dependences = AssemblyManager.GetDependencies("App.exe", folder);

            //crawl dependences
            foreach (var file in dependences)
            {
                if (cts.IsCancellationRequested) { break; }

                using (FileStream fs = File.Open(Path.Combine(folder, file), FileMode.Open))
                {
                    var len = (int)fs.Length;
                    var bytes = new byte[len];

                    fs.Read(bytes, 0, len);

                    //0x
                    var crc = CrcTools.Crc32part(bytes, len, 0);// 0x04C11DB7);

                    Console.WriteLine($"{file} crc is {crc}");
                    files.Add(Path.GetFileName(file));
                    crcs.Add(crc);
                }
            }

            return (files, crcs);
        }

        async Task DeleteUnusedFiles(MeadowSerialDevice meadow, IOutputPaneWriter outputPaneWriter, CancellationToken cts,
            (List<string> files, List<UInt32> crcs) meadowFiles, (List<string> files, List<UInt32> crcs) localFiles)
        {
            if (cts.IsCancellationRequested)
                return;

            foreach (var file in meadowFiles.files)
            {
                if (cts.IsCancellationRequested) { break; }

                if (localFiles.files.Contains(file) == false)
                {
                    await meadow.DeleteFile(file).ConfigureAwait(false);
                    await outputPaneWriter.WriteAsync($"Removing {file}").ConfigureAwait(false);
                }
            }
        }

        async Task DeployApp(MeadowSerialDevice meadow, IOutputPaneWriter outputPaneWriter, CancellationToken cts, string folder,
            (List<string> files, List<UInt32> crcs) meadowFiles, (List<string> files, List<UInt32> crcs) localFiles)
        {
            if (cts.IsCancellationRequested)
                return;

            for (int i = 0; i < localFiles.files.Count; i++)
            {
                if (meadowFiles.crcs.Contains(localFiles.crcs[i])) continue;

                await WriteFileToMeadowAsync(meadow, outputPaneWriter, cts, folder, localFiles.files[i], true).ConfigureAwait(false);
            }
        }

        async Task WriteFileToMeadowAsync(MeadowSerialDevice meadow, IOutputPaneWriter outputPaneWriter, CancellationToken cts, string folder, string file, bool overwrite = false)
        {
            if (cts.IsCancellationRequested) { return; }

            if (overwrite || await meadow.IsFileOnDevice(file).ConfigureAwait(false) == false)
            {
                await outputPaneWriter.WriteAsync($"Writing {file}").ConfigureAwait(false);
                await meadow.WriteFile(file, folder).ConfigureAwait(false);
            }
        }

        private void CopySystemNetHttpDll()
        {
            try
            {
                var bclNugetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", "wildernesslabs.meadow.assemblies");

                if (Directory.Exists(bclNugetPath))
                {
                    List<Version> versions = new List<Version>();

                    var versionFolders = Directory.EnumerateDirectories(bclNugetPath);
                    foreach (var versionFolder in versionFolders)
                    {
                        var di = new DirectoryInfo(versionFolder);
                        Version outVersion;
                        if (Version.TryParse(di.Name, out outVersion))
                        {
                            versions.Add(outVersion);
                        }
                    }

                    if (versions.Any())
                    {
                        versions.Sort();

                        var sourcePath = Path.Combine(bclNugetPath, versions.Last().ToString(), "lib", "net472");
                        if (Directory.Exists(sourcePath))
                        {
                            if (File.Exists(Path.Combine(sourcePath, _systemHttpNetDllName)))
                            {
                                File.Copy(Path.Combine(sourcePath, _systemHttpNetDllName), Path.Combine(_outputPath, _systemHttpNetDllName));
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // eat this for now
            }
        }

        public bool IsDeploySupported
        {
            get { return true; }
        }

        public void Commit()
        {
            IVsOutputWindow outWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            Guid generalPaneGuid = VSConstants.GUID_OutWindowDebugPane; // P.S. There's also the GUID_OutWindowDebugPane available.
            IVsOutputWindowPane generalPane;
            outWindow.GetPane(ref generalPaneGuid, out generalPane);
            generalPane.Activate();
            generalPane.Clear();

            generalPane.OutputString(" Launching application..." + Environment.NewLine);

            var meadow = MeadowDeviceManager.CurrentDevice;
            if (meadow?.OnMeadowMessage == null)
            {
                meadow.OnMeadowMessage += (s, e) =>
                {
                    generalPane.OutputString(" " + e.Message);
                };
            }
        }

        public void Rollback()
        {
        }
    }
}