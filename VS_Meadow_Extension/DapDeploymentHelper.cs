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

        private class DeploymentState
        {
            public bool LaunchSucceeded { get; set; }
            public bool DeploymentComplete { get; set; }
        }

        private async Task<bool> MonitorDeploymentAsync(Process process, CancellationToken cancellationToken)
        {
            const int DeploymentTimeoutSeconds = 30;
            var deploymentState = new DeploymentState();
            bool deploymentSuccess = false;
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(DeploymentTimeoutSeconds));
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                // Monitor both stdout and stderr
                var stdoutTask = MonitorStdoutAsync(process, linkedCts.Token, deploymentState);
                var stderrTask = MonitorStderrAsync(process, linkedCts.Token);

                await Task.WhenAny(stdoutTask, stderrTask);

                // If stdout completed normally, deploymentComplete flag was set
                if (deploymentState.DeploymentComplete)
                {
                    deploymentSuccess = deploymentState.LaunchSucceeded;
                }
                else if (timeoutCts.Token.IsCancellationRequested)
                {
                    _logger?.Log($"ERROR: Deployment timed out after {DeploymentTimeoutSeconds} seconds. Adapter did not complete.");
                    deploymentSuccess = false;
                }
                else if (cancellationToken.IsCancellationRequested)
                {
                    _logger?.Log("ERROR: Deployment was cancelled by user.");
                    deploymentSuccess = false;
                }

                return deploymentSuccess;
            }
            finally
            {
                timeoutCts?.Dispose();
                linkedCts?.Dispose();
            }
        }

        private async Task MonitorStdoutAsync(Process process, CancellationToken cancellationToken, DeploymentState deploymentState)
        {
            try
            {
                while (!process.HasExited && !cancellationToken.IsCancellationRequested)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (line == null) break;

                    // DAP messages are preceded by Content-Length header
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // Skip blank line after header
                            await process.StandardOutput.ReadLineAsync();
                            var contentLength = int.Parse(line.Substring(15).Trim());

                            if (contentLength <= 0 || contentLength > 1024 * 1024)
                            {
                                _logger?.Log($"WARNING: Invalid DAP message length: {contentLength}");
                                continue;
                            }

                            // Read JSON message
                            var messageBuffer = new char[contentLength];
                            int bytesRead = await process.StandardOutput.ReadAsync(messageBuffer, 0, contentLength);
                            if (bytesRead != contentLength)
                            {
                                _logger?.Log($"WARNING: Expected {contentLength} bytes, got {bytesRead}");
                                continue;
                            }

                            var messageJson = new string(messageBuffer);

                            try
                            {
                                var message = JObject.Parse(messageJson);
                                var messageType = message["type"]?.ToString();

                                if (messageType == "event")
                                {
                                    var eventType = message["event"]?.ToString();
                                    HandleDapEvent(eventType, message["body"] as JObject);

                                    // Check for deployment completion (terminated = success, exited = check code)
                                    if (eventType == "terminated")
                                    {
                                        deploymentState.DeploymentComplete = true;
                                        deploymentState.LaunchSucceeded = true;
                                        break;
                                    }
                                    else if (eventType == "exited")
                                    {
                                        var code = message["body"]?["exitCode"]?.ToObject<int>() ?? -1;
                                        if (code == 0)
                                        {
                                            deploymentState.DeploymentComplete = true;
                                            deploymentState.LaunchSucceeded = true;
                                        }
                                        else
                                        {
                                            _logger?.Log($"ERROR: Adapter exited with code {code}");
                                            deploymentState.DeploymentComplete = true;
                                            deploymentState.LaunchSucceeded = false;
                                        }
                                        break;
                                    }
                                }
                                else if (messageType == "response")
                                {
                                    var command = message["command"]?.ToString();
                                    var success = message["success"]?.ToObject<bool>() ?? false;

                                    if (command == "launch")
                                    {
                                        if (success)
                                        {
                                            _logger?.Log("Launch request accepted by adapter");
                                            deploymentState.LaunchSucceeded = true;
                                        }
                                        else
                                        {
                                            var errorMsg = message["message"]?.ToString() ?? "Unknown error";
                                            _logger?.Log($"ERROR: Launch failed: {errorMsg}");
                                            deploymentState.LaunchSucceeded = false;
                                            deploymentState.DeploymentComplete = true;
                                            break;
                                        }
                                    }
                                }
                            }
                            catch (JsonException ex)
                            {
                                _logger?.Log($"WARNING: Failed to parse DAP message: {ex.Message}");
                            }
                        }
                        catch (FormatException ex)
                        {
                            _logger?.Log($"WARNING: Invalid Content-Length header: {ex.Message}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout or cancellation
            }
            catch (Exception ex)
            {
                _logger?.Log($"ERROR: Exception while monitoring stdout: {ex.Message}");
            }
        }

        private async Task MonitorStderrAsync(Process process, CancellationToken cancellationToken)
        {
            try
            {
                while (!process.HasExited && !cancellationToken.IsCancellationRequested)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (line == null) break;

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _logger?.Log($"[Adapter] {line}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout or cancellation
            }
            catch (Exception ex)
            {
                _logger?.Log($"WARNING: Exception while monitoring stderr: {ex.Message}");
            }
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

        public static string GetAdapterPath()
        {
            // Get path to DAP adapter bundled in the VSIX (same location as debug sessions use)
            var assemblyPath = Path.GetDirectoryName(typeof(DapDeploymentHelper).Assembly.Location);
            var adapterPath = Path.Combine(assemblyPath, "DapAdapter", "meadow-debugging.exe");
            return adapterPath;
        }
    }
}
