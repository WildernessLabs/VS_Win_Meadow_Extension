using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meadow
{
    /// <summary>
    /// Helper to launch the DAP adapter for deployment-only scenarios.
    /// Communicates via DAP protocol over stdin/stdout to deploy without debugging.
    /// </summary>
    internal class DapDeploymentHelper
    {
        private readonly OutputLogger _logger;
        private readonly string _adapterPath;
        private int _sequence = 1;

        public DapDeploymentHelper(OutputLogger logger, string adapterPath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _adapterPath = adapterPath ?? throw new ArgumentNullException(nameof(adapterPath));
        }

        /// <summary>
        /// Launch DAP adapter to deploy without debugging (debugPort: 0).
        /// </summary>
        public async Task<bool> DeployAsync(
            string projectPath,
            string configuration,
            string serial,
            string msbuildPropertyFile,
            CancellationToken cancellationToken)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = _adapterPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(processInfo))
            {
                if (process == null)
                {
                    _logger?.Log("ERROR: Failed to start DAP adapter process");
                    return false;
                }

                try
                {
                    // Initialize DAP protocol
                    await SendDapRequestAsync(process.StandardInput, "initialize", new
                    {
                        clientID = "visualstudio",
                        adapterID = "meadow",
                        linesStartAt1 = true,
                        columnsStartAt1 = true,
                        pathFormat = "path"
                    });

                    // Launch with debugPort: 0 for deploy-only
                    await SendDapRequestAsync(process.StandardInput, "launch", new
                    {
                        type = "meadow",
                        request = "launch",
                        projectPath = projectPath,
                        projectConfiguration = configuration,
                        serial = serial,
                        msbuildPropertyFile = msbuildPropertyFile,
                        debugPort = 0  // Deploy without debugging
                    });

                    // Monitor output for progress events and completion
                    bool deploymentSuccess = await MonitorDeploymentAsync(process, cancellationToken);

                    // Disconnect
                    await SendDapRequestAsync(process.StandardInput, "disconnect", new { });

                    // Wait for process to exit
                    if (!process.HasExited)
                    {
                        await Task.Run(() => process.WaitForExit(5000), cancellationToken);
                    }

                    return deploymentSuccess;
                }
                catch (Exception ex)
                {
                    _logger?.Log($"ERROR: DAP deployment failed: {ex.Message}");
                    return false;
                }
            }
        }

        private async Task SendDapRequestAsync(StreamWriter stdin, string command, object arguments)
        {
            var request = new
            {
                seq = _sequence++,
                type = "request",
                command = command,
                arguments = arguments
            };

            var json = JsonConvert.SerializeObject(request);
            var content = Encoding.UTF8.GetBytes(json);

            var header = $"Content-Length: {content.Length}\r\n\r\n";
            await stdin.WriteAsync(header);
            await stdin.WriteAsync(json);
            await stdin.FlushAsync();
        }

        private async Task<bool> MonitorDeploymentAsync(Process process, CancellationToken cancellationToken)
        {
            bool deploymentComplete = false;
            bool deploymentSuccess = true;
            var buffer = new StringBuilder();

            while (!process.HasExited && !cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (line == null) break;

                // DAP messages are preceded by Content-Length header
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    // Skip header line and blank line
                    await process.StandardOutput.ReadLineAsync();
                    var contentLength = int.Parse(line.Substring(15).Trim());

                    // Read JSON message
                    var messageBuffer = new char[contentLength];
                    await process.StandardOutput.ReadAsync(messageBuffer, 0, contentLength);
                    var messageJson = new string(messageBuffer);

                    try
                    {
                        var message = JObject.Parse(messageJson);
                        var messageType = message["type"]?.ToString();

                        if (messageType == "event")
                        {
                            var eventType = message["event"]?.ToString();
                            HandleDapEvent(eventType, message["body"] as JObject);

                            // Check for deployment completion
                            if (eventType == "terminated" || eventType == "exited")
                            {
                                deploymentComplete = true;
                                break;
                            }
                        }
                        else if (messageType == "response")
                        {
                            var command = message["command"]?.ToString();
                            var success = message["success"]?.ToObject<bool>() ?? false;

                            if (command == "launch" && !success)
                            {
                                var errorMsg = message["message"]?.ToString() ?? "Unknown error";
                                _logger?.Log($"ERROR: Launch failed: {errorMsg}");
                                deploymentSuccess = false;
                                break;
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger?.Log($"WARNING: Failed to parse DAP message: {ex.Message}");
                    }
                }
            }

            return deploymentSuccess && deploymentComplete;
        }

        private void HandleDapEvent(string eventType, JObject body)
        {
            switch (eventType)
            {
                case "output":
                    var output = body?["output"]?.ToString();
                    if (!string.IsNullOrEmpty(output))
                    {
                        _logger?.Log(output);
                    }
                    break;

                case "progressStart":
                    var title = body?["title"]?.ToString();
                    _logger?.Log($"[Progress] {title}");
                    break;

                case "progressUpdate":
                    var message = body?["message"]?.ToString();
                    var percentage = body?["percentage"]?.ToObject<int>();
                    if (percentage.HasValue)
                    {
                        _logger?.Log($"[Progress] {percentage}% {message}");
                        // Note: outputLogger.ReportFileProgress() is async and requires UI thread
                        // For now, just log progress - visual progress won't appear for standalone deploy
                        // until VS provides a way to show DAP progress outside debug sessions
                    }
                    break;

                case "progressEnd":
                    var endMessage = body?["message"]?.ToString();
                    _logger?.Log($"[Progress] {endMessage}");
                    break;
            }
        }
    }
}
