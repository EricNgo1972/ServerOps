using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Entities;
using ServerOps.Domain.Enums;

namespace ServerOps.Infrastructure.CloudflareTunnel;

public sealed class CloudflaredService : ICloudflaredService
{
    private const string WindowsDownloadUrl = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";
    private const string LinuxDownloadUrl = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64";

    private readonly ICommandRunner _commandRunner;
    private readonly IFileSystem _fileSystem;
    private readonly IRuntimeEnvironment _runtimeEnvironment;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOperationLogger _operationLogger;
    private readonly IEndpointRegistry _endpointRegistry;
    private readonly ICloudflareDnsService _cloudflareDnsService;
    private readonly IServiceRegistrationService _serviceRegistrationService;

    public CloudflaredService(
        ICommandRunner commandRunner,
        IFileSystem fileSystem,
        IRuntimeEnvironment runtimeEnvironment,
        IHttpClientFactory httpClientFactory,
        IOperationLogger operationLogger,
        IEndpointRegistry endpointRegistry,
        ICloudflareDnsService cloudflareDnsService,
        IServiceRegistrationService serviceRegistrationService)
    {
        _commandRunner = commandRunner;
        _fileSystem = fileSystem;
        _runtimeEnvironment = runtimeEnvironment;
        _httpClientFactory = httpClientFactory;
        _operationLogger = operationLogger;
        _endpointRegistry = endpointRegistry;
        _cloudflareDnsService = cloudflareDnsService;
        _serviceRegistrationService = serviceRegistrationService;
    }

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        var binaryPath = await GetBinaryPathAsync(cancellationToken);
        if (_fileSystem.FileExists(binaryPath))
        {
            return true;
        }

        var result = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = await GetBinaryCommandAsync(cancellationToken),
            Arguments = ["--version"]
        }, cancellationToken);

        return result.Succeeded;
    }

    public async Task<bool> IsRunningAsync(CancellationToken cancellationToken = default)
    {
        var result = _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? await _commandRunner.RunAsync(new CommandRequest { Command = "sc", Arguments = ["query", "cloudflared"] }, cancellationToken)
            : await _commandRunner.RunAsync(new CommandRequest { Command = "systemctl", Arguments = ["status", "cloudflared", "--no-pager"] }, cancellationToken);

        return result.StdOut.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ||
               result.StdOut.Contains("active (running)", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<TunnelInfo> GetTunnelInfoAsync(CancellationToken cancellationToken = default)
    {
        var configPath = _runtimeEnvironment.GetCloudflaredConfigPath();
        var isInstalled = await IsInstalledAsync(cancellationToken);
        var isRunning = await IsRunningAsync(cancellationToken);
        var isServiceInstalled = await IsServiceInstalledAsync(cancellationToken);
        var metadataPath = GetMetadataPath();
        var parsedConfig = await ReadConfigAsync(configPath, cancellationToken);
        RemoteTunnelMetadata? metadata = null;

        if (_fileSystem.FileExists(metadataPath))
        {
            metadata = await ReadMetadataAsync(metadataPath, cancellationToken);
        }

        return new TunnelInfo
        {
            TunnelId = string.IsNullOrWhiteSpace(metadata?.TunnelId) ? parsedConfig.TunnelId : metadata!.TunnelId,
            TunnelName = metadata?.TunnelName ?? string.Empty,
            IsInstalled = isInstalled,
            IsServiceInstalled = isServiceInstalled,
            IsConfigured = !string.IsNullOrWhiteSpace(parsedConfig.TunnelId) && !string.IsNullOrWhiteSpace(parsedConfig.CredentialsFile),
            IsRemotelyManaged = false,
            IsRunning = isRunning,
            ConfigPath = configPath,
            IngressRules = parsedConfig.IngressRules
        };
    }

    public async Task<CommandResult> InstallAsync(string? operationId = null, CancellationToken cancellationToken = default)
    {
        var resolvedOperationId = string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId.Trim();
        if (await IsInstalledAsync(cancellationToken))
        {
            var installedBinaryPath = await GetBinaryPathAsync(cancellationToken);
            return new CommandResult
            {
                OperationId = resolvedOperationId,
                ExitCode = 0,
                StdOut = $"cloudflared already installed at {installedBinaryPath}."
            };
        }

        var binaryPath = await GetBinaryPathAsync(cancellationToken);
        await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Install started path={binaryPath}", cancellationToken);
        var binaryDirectory = Path.GetDirectoryName(binaryPath);
        if (!string.IsNullOrWhiteSpace(binaryDirectory))
        {
            _fileSystem.CreateDirectory(binaryDirectory);
        }

        var client = _httpClientFactory.CreateClient();
        var bytes = await client.GetByteArrayAsync(GetDownloadUrl(), cancellationToken);
        await _fileSystem.WriteAllBytesAsync(binaryPath, bytes, cancellationToken);

        if (_runtimeEnvironment.GetCurrentOs() == OsType.Linux)
        {
            var chmodResult = await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "chmod",
                Arguments = ["+x", binaryPath]
            }, cancellationToken);

            if (!chmodResult.Succeeded)
            {
                return WrapResult(chmodResult, resolvedOperationId);
            }
        }

        await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Install completed path={binaryPath}", cancellationToken);
        return new CommandResult
        {
            OperationId = resolvedOperationId,
            ExitCode = 0,
            StdOut = $"cloudflared installed at {binaryPath}."
        };
    }

    public async Task<CommandResult> CreateTunnelAsync(string? operationId = null, CancellationToken cancellationToken = default)
    {
        var resolvedOperationId = string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId.Trim();
        if (!await IsInstalledAsync(cancellationToken))
        {
            return new CommandResult
            {
                OperationId = resolvedOperationId,
                ExitCode = -1,
                StdErr = "cloudflared is not installed."
            };
        }

        try
        {
            var tunnelName = GetTunnelName();
            var binaryCommand = await GetBinaryCommandAsync(cancellationToken);
            await _operationLogger.LogAsync(
                resolvedOperationId,
                "Tunnel",
                $"Create started name={tunnelName}, binary={binaryCommand}, config={_runtimeEnvironment.GetCloudflaredConfigPath()}",
                cancellationToken);

            var configDirectory = Path.GetDirectoryName(_runtimeEnvironment.GetCloudflaredConfigPath());
            if (!string.IsNullOrWhiteSpace(configDirectory))
            {
                _fileSystem.CreateDirectory(configDirectory);
            }

            var temporaryCredentialsPath = GetManagedCredentialsPathForNewTunnel();
            string? existingTempCredentialsContents = null;
            if (_fileSystem.FileExists(temporaryCredentialsPath))
            {
                try
                {
                    existingTempCredentialsContents = await _fileSystem.ReadAllTextAsync(temporaryCredentialsPath, cancellationToken);
                    _fileSystem.DeleteFile(temporaryCredentialsPath);
                    await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Create removed stale tempCredentials={temporaryCredentialsPath}", cancellationToken);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Unable to remove stale temporary tunnel credentials file '{temporaryCredentialsPath}'. {ex.Message}");
                }
            }
            await _operationLogger.LogAsync(
                resolvedOperationId,
                "Tunnel",
                $"Create paths configDirectory={configDirectory}, tempCredentials={temporaryCredentialsPath}",
                cancellationToken);

            var createResult = await _commandRunner.RunAsync(new CommandRequest
            {
                Command = binaryCommand,
                Arguments = ["tunnel", "create", "--credentials-file", temporaryCredentialsPath, tunnelName]
            }, cancellationToken);
            await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Create command {DescribeCommandResult(createResult)}", cancellationToken);

            var tunnelId = ExtractTunnelId(createResult.StdOut, createResult.StdErr);
            await _operationLogger.LogAsync(
                resolvedOperationId,
                "Tunnel",
                $"Create parsed tunnelId={(string.IsNullOrWhiteSpace(tunnelId) ? "-" : tunnelId)}, tempCredentialsExists={_fileSystem.FileExists(temporaryCredentialsPath)}",
                cancellationToken);

            if (!createResult.Succeeded && string.IsNullOrWhiteSpace(tunnelId))
            {
                if (IsTunnelAlreadyExistsError(createResult))
                {
                    tunnelId = await FindTunnelIdByNameAsync(tunnelName, cancellationToken);
                    if (string.IsNullOrWhiteSpace(tunnelId))
                    {
                        throw new InvalidOperationException($"Tunnel '{tunnelName}' already exists, but its id could not be resolved.");
                    }

                    await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Create adopting existing tunnel name={tunnelName}, id={tunnelId}", cancellationToken);
                }
            }

            if (!createResult.Succeeded && string.IsNullOrWhiteSpace(tunnelId))
            {
                var details = GetFirstRelevantLine(string.IsNullOrWhiteSpace(createResult.StdErr) ? createResult.StdOut : createResult.StdErr);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(details)
                    ? "Failed to create cloudflared tunnel. Make sure cloudflared is authenticated and cert.pem is available."
                    : details);
            }

            if (string.IsNullOrWhiteSpace(tunnelId))
            {
                throw new InvalidOperationException("cloudflared tunnel was created but the tunnel id could not be determined.");
            }

            if (!_fileSystem.FileExists(temporaryCredentialsPath))
            {
                if (!string.IsNullOrWhiteSpace(existingTempCredentialsContents))
                {
                    await _fileSystem.WriteAllBytesAsync(temporaryCredentialsPath, Encoding.UTF8.GetBytes(existingTempCredentialsContents), cancellationToken);
                    await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Create restored preserved temp credentials for existing tunnel id={tunnelId}", cancellationToken);
                }
                else
                {
                    var existingManagedCredentialsPath = GetManagedCredentialsPath(tunnelId);
                    if (_fileSystem.FileExists(existingManagedCredentialsPath))
                    {
                        var managedContents = await _fileSystem.ReadAllTextAsync(existingManagedCredentialsPath, cancellationToken);
                        await _fileSystem.WriteAllBytesAsync(temporaryCredentialsPath, Encoding.UTF8.GetBytes(managedContents), cancellationToken);
                        await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Create reused existing managed credentials from {existingManagedCredentialsPath}", cancellationToken);
                    }
                    else
                    {
                    var legacyCredentialsPath = GetLegacyCredentialsPath(tunnelId);
                    if (_fileSystem.FileExists(legacyCredentialsPath))
                    {
                        var legacyContents = await _fileSystem.ReadAllTextAsync(legacyCredentialsPath, cancellationToken);
                        await _fileSystem.WriteAllBytesAsync(temporaryCredentialsPath, Encoding.UTF8.GetBytes(legacyContents), cancellationToken);
                        await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Create migrated legacy credentials from {legacyCredentialsPath}", cancellationToken);
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Tunnel '{tunnelName}' already exists, but credentials were not found in the managed path. Expected temp '{temporaryCredentialsPath}', managed '{existingManagedCredentialsPath}', or legacy path '{legacyCredentialsPath}'.");
                    }
                    }
                }
            }

            var managedCredentialsPath = GetManagedCredentialsPath(tunnelId);
            var credentialsContents = await _fileSystem.ReadAllTextAsync(temporaryCredentialsPath, cancellationToken);
            await _fileSystem.WriteAllBytesAsync(managedCredentialsPath, Encoding.UTF8.GetBytes(credentialsContents), cancellationToken);
            await _operationLogger.LogAsync(
                resolvedOperationId,
                "Tunnel",
                $"Create credentials copied temp={temporaryCredentialsPath}, managed={managedCredentialsPath}, managedExists={_fileSystem.FileExists(managedCredentialsPath)}",
                cancellationToken);

            if (!string.Equals(temporaryCredentialsPath, managedCredentialsPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _fileSystem.DeleteFile(temporaryCredentialsPath);
                }
                catch (UnauthorizedAccessException)
                {
                    await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Create cleanup skipped lockedTempCredentials={temporaryCredentialsPath}", cancellationToken);
                }
            }

            await WriteConfigAsync(tunnelId, managedCredentialsPath, cancellationToken);

            await WriteMetadataAsync(new RemoteTunnelMetadata
            {
                TunnelId = tunnelId,
                TunnelName = tunnelName
            }, cancellationToken);

            await _operationLogger.LogAsync(
                resolvedOperationId,
                "Tunnel",
                $"Created id={tunnelId}, name={tunnelName}, config={_runtimeEnvironment.GetCloudflaredConfigPath()}, credentials={managedCredentialsPath}",
                cancellationToken);
            return new CommandResult
            {
                OperationId = resolvedOperationId,
                ExitCode = 0,
                StdOut = $"Created tunnel '{tunnelName}' ({tunnelId}) with config '{_runtimeEnvironment.GetCloudflaredConfigPath()}'."
            };
        }
        catch (Exception ex)
        {
            await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Create failed error={ex.Message}", cancellationToken);
            return new CommandResult
            {
                OperationId = resolvedOperationId,
                ExitCode = -1,
                StdErr = ex.Message
            };
        }
    }

    public async Task<CommandResult> StartAsync(string? operationId = null, CancellationToken cancellationToken = default)
    {
        var resolvedOperationId = string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId.Trim();
        var tunnelInfo = await GetTunnelInfoAsync(cancellationToken);
        if (!tunnelInfo.IsConfigured || string.IsNullOrWhiteSpace(tunnelInfo.TunnelId))
        {
            return new CommandResult
            {
                OperationId = resolvedOperationId,
                ExitCode = -1,
                StdErr = "cloudflared tunnel is not configured."
            };
        }

        if (!await IsInstalledAsync(cancellationToken))
        {
            return new CommandResult
            {
                OperationId = resolvedOperationId,
                ExitCode = -1,
                StdErr = "cloudflared is not installed."
            };
        }

        var configPath = _runtimeEnvironment.GetCloudflaredConfigPath();
        var credentialsPath = await ReadCredentialsPathAsync(configPath, cancellationToken);
        var credentialsExists = !string.IsNullOrWhiteSpace(credentialsPath) && _fileSystem.FileExists(credentialsPath);
        await _operationLogger.LogAsync(
            resolvedOperationId,
            "Tunnel",
            $"Start started tunnelId={tunnelInfo.TunnelId}, config={configPath}, configExists={_fileSystem.FileExists(configPath)}, credentialsPath={credentialsPath}, credentialsExists={credentialsExists}",
            cancellationToken);

        var ensureServiceResult = await EnsureConfigDrivenServiceAsync(tunnelInfo.TunnelId, cancellationToken);
        if (!ensureServiceResult.Succeeded)
        {
            await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Start ensureService {DescribeCommandResult(ensureServiceResult)}", cancellationToken);
            return WrapResult(ensureServiceResult, resolvedOperationId);
        }

        var startResult = _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? await StartWindowsServiceAsync("cloudflared", cancellationToken)
            : await _commandRunner.RunAsync(new CommandRequest { Command = "systemctl", Arguments = ["start", "cloudflared"] }, cancellationToken);
        await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Start {DescribeCommandResult(startResult)}", cancellationToken);
        return WrapResult(startResult, resolvedOperationId);
    }

    public async Task<CommandResult> RestartAsync(string? operationId = null, CancellationToken cancellationToken = default)
    {
        var resolvedOperationId = string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId.Trim();
        await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", "Restart started", cancellationToken);

        var tunnelInfo = await GetTunnelInfoAsync(cancellationToken);
        if (!tunnelInfo.IsConfigured || string.IsNullOrWhiteSpace(tunnelInfo.TunnelId))
        {
            return new CommandResult
            {
                OperationId = resolvedOperationId,
                ExitCode = -1,
                StdErr = "cloudflared tunnel is not configured."
            };
        }

        var ensureServiceResult = await EnsureConfigDrivenServiceAsync(tunnelInfo.TunnelId, cancellationToken);
        if (!ensureServiceResult.Succeeded)
        {
            await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Restart ensureService {DescribeCommandResult(ensureServiceResult)}", cancellationToken);
            return WrapResult(ensureServiceResult, resolvedOperationId);
        }

        if (_runtimeEnvironment.GetCurrentOs() != OsType.Windows)
        {
            var linuxResult = await _commandRunner.RunAsync(
                new CommandRequest { Command = "systemctl", Arguments = ["restart", "cloudflared"] },
                cancellationToken);
            await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Restart {DescribeCommandResult(linuxResult)}", cancellationToken);
            return WrapResult(linuxResult, resolvedOperationId);
        }

        var stopResult = await _commandRunner.RunAsync(
            new CommandRequest { Command = "sc", Arguments = ["stop", "cloudflared"] },
            cancellationToken);

        if (!stopResult.Succeeded && !IsAlreadyStopped(stopResult))
        {
            await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Restart {DescribeCommandResult(stopResult)}", cancellationToken);
            return WrapResult(stopResult, resolvedOperationId);
        }

        var stopped = await WaitForWindowsServiceStateAsync("cloudflared", expectRunning: false, cancellationToken);
        await _operationLogger.LogAsync(
            resolvedOperationId,
            "Tunnel",
            $"Restart waitForStop={(stopped ? "completed" : "timed_out")}",
            cancellationToken);

        var restartResult = await StartWindowsServiceAsync("cloudflared", cancellationToken);
        await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Restart {DescribeCommandResult(restartResult)}", cancellationToken);
        return WrapResult(restartResult, resolvedOperationId);
    }

    public async Task<CommandResult> DeleteTunnelAsync(string? operationId = null, CancellationToken cancellationToken = default)
    {
        var resolvedOperationId = string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId.Trim();

        try
        {
            var tunnelInfo = await GetTunnelInfoAsync(cancellationToken);
            await _operationLogger.LogAsync(
                resolvedOperationId,
                "Tunnel",
                $"Delete started tunnelId={tunnelInfo.TunnelId ?? "-"}, configured={tunnelInfo.IsConfigured}, running={tunnelInfo.IsRunning}, serviceInstalled={tunnelInfo.IsServiceInstalled}",
                cancellationToken);

            var mappings = await _endpointRegistry.GetMappingsAsync(cancellationToken);
            await RemoveExposureMappingsAsync(mappings, resolvedOperationId, cancellationToken);
            await RemoveLocalTunnelServiceAsync(tunnelInfo, resolvedOperationId, cancellationToken);
            await DeleteLocalTunnelFilesAsync(tunnelInfo.TunnelId, resolvedOperationId, cancellationToken);
            await DeleteRemoteTunnelAsync(tunnelInfo.TunnelId, resolvedOperationId, cancellationToken);
            await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", "Delete completed", cancellationToken);

            return new CommandResult
            {
                OperationId = resolvedOperationId,
                ExitCode = 0,
                StdOut = "Tunnel deleted successfully."
            };
        }
        catch (Exception ex)
        {
            await _operationLogger.LogAsync(resolvedOperationId, "Tunnel", $"Delete failed error={ex.Message}", cancellationToken);
            return new CommandResult
            {
                OperationId = resolvedOperationId,
                ExitCode = -1,
                StdErr = ex.Message
            };
        }
    }

    private async Task<CommandResult> StartWindowsServiceAsync(string serviceName, CancellationToken cancellationToken)
    {
        var startResult = await _commandRunner.RunAsync(
            new CommandRequest { Command = "sc", Arguments = ["start", serviceName] },
            cancellationToken);

        if (startResult.Succeeded || IsAlreadyRunning(startResult))
        {
            var running = await WaitForWindowsServiceStateAsync(serviceName, expectRunning: true, cancellationToken);
            if (running)
            {
                return new CommandResult
                {
                    ExitCode = 0,
                    StdOut = startResult.Succeeded
                        ? startResult.StdOut
                        : $"{startResult.StdErr}{Environment.NewLine}Service is already running."
                };
            }

            var finalQuery = await _commandRunner.RunAsync(
                new CommandRequest { Command = "sc", Arguments = ["query", serviceName] },
                cancellationToken);
            var startDetails = string.IsNullOrWhiteSpace(startResult.StdErr) ? startResult.StdOut : startResult.StdErr;
            var queryDetails = string.IsNullOrWhiteSpace(finalQuery.StdErr) ? finalQuery.StdOut : finalQuery.StdErr;

            return new CommandResult
            {
                ExitCode = -1,
                StdErr = $"Service '{serviceName}' did not reach RUNNING state after start. start={startDetails.Trim()} query={queryDetails.Trim()}".Trim()
            };
        }

        return startResult;
    }

    private async Task RemoveExposureMappingsAsync(IReadOnlyList<Application.Models.EndpointMapping> mappings, string operationId, CancellationToken cancellationToken)
    {
        if (mappings.Count == 0)
        {
            await _operationLogger.LogAsync(operationId, "Tunnel", "Delete mappings skipped count=0", cancellationToken);
            return;
        }

        await _operationLogger.LogAsync(operationId, "Tunnel", $"Delete mappings started count={mappings.Count}", cancellationToken);
        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.ServiceName) || string.IsNullOrWhiteSpace(mapping.Hostname))
            {
                continue;
            }

            var serviceName = mapping.ServiceName.Trim();
            var hostname = mapping.Hostname.Trim();
            await _cloudflareDnsService.DeleteAsync(hostname, cancellationToken);
            await _operationLogger.LogAsync(operationId, "Tunnel", $"Delete CNAME hostname={hostname}", cancellationToken);
            await _endpointRegistry.RemoveAsync(serviceName, cancellationToken);
            await _operationLogger.LogAsync(operationId, "Tunnel", $"Delete mapping service={serviceName}, hostname={hostname}", cancellationToken);
        }
    }

    private async Task RemoveLocalTunnelServiceAsync(TunnelInfo tunnelInfo, string operationId, CancellationToken cancellationToken)
    {
        if (!tunnelInfo.IsServiceInstalled)
        {
            await _operationLogger.LogAsync(operationId, "Tunnel", "Delete local service skipped reason=not installed", cancellationToken);
            return;
        }

        await _operationLogger.LogAsync(operationId, "Tunnel", "Delete local service started", cancellationToken);

        if (_runtimeEnvironment.GetCurrentOs() == OsType.Windows)
        {
            var stopResult = await _commandRunner.RunAsync(
                new CommandRequest { Command = "sc", Arguments = ["stop", "cloudflared"] },
                cancellationToken);

            if (!stopResult.Succeeded && !IsAlreadyStopped(stopResult) && !IsServiceMissing(stopResult))
            {
                throw new InvalidOperationException(GetFailureMessage("Failed to stop cloudflared service.", stopResult));
            }

            await _operationLogger.LogAsync(operationId, "Tunnel", $"Delete stop {DescribeCommandResult(stopResult)}", cancellationToken);
            var stopped = await WaitForWindowsServiceStateAsync("cloudflared", expectRunning: false, cancellationToken);
            await _operationLogger.LogAsync(
                operationId,
                "Tunnel",
                $"Delete waitForStop={(stopped ? "completed" : "timed_out")}",
                cancellationToken);

            var unregisterResult = await _serviceRegistrationService.UnregisterAsync("cloudflared", cancellationToken);
            if (!unregisterResult.Succeeded && !IsServiceMissing(unregisterResult))
            {
                throw new InvalidOperationException(GetFailureMessage("Failed to delete cloudflared service.", unregisterResult));
            }

            await _operationLogger.LogAsync(operationId, "Tunnel", $"Delete unregister {DescribeCommandResult(unregisterResult)}", cancellationToken);
            return;
        }

        var linuxStopResult = await _commandRunner.RunAsync(
            new CommandRequest { Command = "systemctl", Arguments = ["stop", "cloudflared"] },
            cancellationToken);

        if (!linuxStopResult.Succeeded && !IsLinuxServiceMissing(linuxStopResult))
        {
            throw new InvalidOperationException(GetFailureMessage("Failed to stop cloudflared service.", linuxStopResult));
        }

        await _operationLogger.LogAsync(operationId, "Tunnel", $"Delete stop {DescribeCommandResult(linuxStopResult)}", cancellationToken);
        var linuxUnregisterResult = await _serviceRegistrationService.UnregisterAsync("cloudflared", cancellationToken);
        if (!linuxUnregisterResult.Succeeded && !IsLinuxServiceMissing(linuxUnregisterResult))
        {
            throw new InvalidOperationException(GetFailureMessage("Failed to delete cloudflared service.", linuxUnregisterResult));
        }

        await _operationLogger.LogAsync(operationId, "Tunnel", $"Delete unregister {DescribeCommandResult(linuxUnregisterResult)}", cancellationToken);
    }

    private async Task DeleteLocalTunnelFilesAsync(string? tunnelId, string operationId, CancellationToken cancellationToken)
    {
        var deletedPaths = new List<string>();
        var managedCredentialsPath = string.IsNullOrWhiteSpace(tunnelId) ? string.Empty : GetManagedCredentialsPath(tunnelId.Trim());
        var temporaryCredentialsPath = GetManagedCredentialsPathForNewTunnel();
        foreach (var path in new[] { GetMetadataPath(), _runtimeEnvironment.GetCloudflaredConfigPath(), managedCredentialsPath, temporaryCredentialsPath })
        {
            if (string.IsNullOrWhiteSpace(path) || !_fileSystem.FileExists(path))
            {
                continue;
            }

            _fileSystem.DeleteFile(path);
            deletedPaths.Add(path);
        }

        await _operationLogger.LogAsync(
            operationId,
            "Tunnel",
            deletedPaths.Count == 0
                ? "Delete local config skipped count=0"
                : $"Delete local config completed paths={string.Join(", ", deletedPaths)}",
            cancellationToken);
    }

    private async Task DeleteRemoteTunnelAsync(string? tunnelId, string operationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tunnelId))
        {
            await _operationLogger.LogAsync(operationId, "Tunnel", "Delete remote skipped reason=no tunnel id", cancellationToken);
            return;
        }

        var deleteResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = await GetBinaryCommandAsync(cancellationToken),
            Arguments = ["tunnel", "delete", tunnelId.Trim()]
        }, cancellationToken);

        if (deleteResult.Succeeded)
        {
            await _operationLogger.LogAsync(
                operationId,
                "Tunnel",
                $"Delete remote completed tunnelId={tunnelId.Trim()}",
                cancellationToken);
            return;
        }

        var details = GetFirstRelevantLine(string.IsNullOrWhiteSpace(deleteResult.StdErr) ? deleteResult.StdOut : deleteResult.StdErr);
        if (string.IsNullOrWhiteSpace(details))
        {
            await _operationLogger.LogAsync(
                operationId,
                "Tunnel",
                $"Delete remote completed with warningsIgnored tunnelId={tunnelId.Trim()}",
                cancellationToken);
            return;
        }

        if (details.Contains("already been deleted", StringComparison.OrdinalIgnoreCase))
        {
            await _operationLogger.LogAsync(
                operationId,
                "Tunnel",
                $"Delete remote skipped alreadyDeleted tunnelId={tunnelId.Trim()}",
                cancellationToken);
            return;
        }

        throw new InvalidOperationException($"Failed to delete remote tunnel. {details}");
    }

    private static CommandResult WrapResult(CommandResult result, string operationId)
    {
        return new CommandResult
        {
            OperationId = operationId,
            ExitCode = result.ExitCode,
            StdOut = result.StdOut,
            StdErr = result.StdErr
        };
    }

    private static string DescribeCommandResult(CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return string.IsNullOrWhiteSpace(details)
            ? $"exitCode={result.ExitCode}, succeeded={result.Succeeded}"
            : $"exitCode={result.ExitCode}, succeeded={result.Succeeded}, details={details.ReplaceLineEndings(" ").Trim()}";
    }

    private async Task<bool> WaitForWindowsServiceStateAsync(string serviceName, bool expectRunning, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var queryResult = await _commandRunner.RunAsync(
                new CommandRequest { Command = "sc", Arguments = ["query", serviceName] },
                cancellationToken);

            var details = string.IsNullOrWhiteSpace(queryResult.StdErr) ? queryResult.StdOut : queryResult.StdErr;
            if (expectRunning)
            {
                if (details.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else
            {
                if (details.Contains("STOPPED", StringComparison.OrdinalIgnoreCase) ||
                    details.Contains("FAILED 1060", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (attempt < 9)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        return false;
    }

    private static bool IsAlreadyStopped(CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return details.Contains("FAILED 1062", StringComparison.OrdinalIgnoreCase) ||
               details.Contains("not been started", StringComparison.OrdinalIgnoreCase) ||
               details.Contains("not running", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAlreadyRunning(CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return details.Contains("FAILED 1056", StringComparison.OrdinalIgnoreCase) ||
               details.Contains("already running", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsServiceMissing(CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return details.Contains("FAILED 1060", StringComparison.OrdinalIgnoreCase) ||
               details.Contains("does not exist as an installed service", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLinuxServiceMissing(CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return details.Contains("could not be found", StringComparison.OrdinalIgnoreCase) ||
               details.Contains("not loaded", StringComparison.OrdinalIgnoreCase) ||
               details.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFailureMessage(string prefix, CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return string.IsNullOrWhiteSpace(details) ? prefix : $"{prefix} {details.Trim()}";
    }

    private async Task<CommandResult> EnsureConfigDrivenServiceAsync(string tunnelId, CancellationToken cancellationToken)
    {
        return _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? await EnsureWindowsServiceAsync(tunnelId, cancellationToken)
            : await EnsureLinuxServiceAsync(tunnelId, cancellationToken);
    }

    private async Task<CommandResult> EnsureWindowsServiceAsync(string tunnelId, CancellationToken cancellationToken)
    {
        var serviceDefinition = await _commandRunner.RunAsync(
            new CommandRequest { Command = "sc", Arguments = ["qc", "cloudflared"] },
            cancellationToken);
        if (serviceDefinition.Succeeded &&
            serviceDefinition.StdOut.Contains("--config", StringComparison.OrdinalIgnoreCase) &&
            serviceDefinition.StdOut.Contains(_runtimeEnvironment.GetCloudflaredConfigPath(), StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult { ExitCode = 0 };
        }

        if (serviceDefinition.Succeeded)
        {
            var stopResult = await _commandRunner.RunAsync(
                new CommandRequest { Command = "sc", Arguments = ["stop", "cloudflared"] },
                cancellationToken);

            if (!stopResult.Succeeded && !IsAlreadyStopped(stopResult))
            {
                return stopResult;
            }

            await WaitForWindowsServiceStateAsync("cloudflared", expectRunning: false, cancellationToken);
            var unregisterResult = await _serviceRegistrationService.UnregisterAsync("cloudflared", cancellationToken);
            if (!unregisterResult.Succeeded && !IsServiceMissing(unregisterResult))
            {
                return unregisterResult;
            }
        }

        var binaryPath = await GetBinaryPathAsync(cancellationToken);
        var configPath = _runtimeEnvironment.GetCloudflaredConfigPath();
        var binPath = $"\"{binaryPath}\" tunnel --config \"{configPath}\" run {tunnelId}";
        var configExists = _fileSystem.FileExists(configPath);
        var credentialsPath = await ReadCredentialsPathAsync(configPath, cancellationToken);
        var credentialsExists = !string.IsNullOrWhiteSpace(credentialsPath) && _fileSystem.FileExists(credentialsPath);

        var createResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "sc",
            Arguments = ["create", "cloudflared", "binPath=", binPath, "start=", "auto", "obj=", "LocalSystem"]
        }, cancellationToken);
        return new CommandResult
        {
            ExitCode = createResult.ExitCode,
            StdOut = AppendDiagnosticDetails(createResult.StdOut, $"binaryPath={binaryPath}; configPath={configPath}; configExists={configExists}; credentialsPath={credentialsPath}; credentialsExists={credentialsExists}; binPath={binPath}"),
            StdErr = createResult.StdErr
        };
    }

    private async Task<CommandResult> EnsureLinuxServiceAsync(string tunnelId, CancellationToken cancellationToken)
    {
        var unitPath = _fileSystem.Combine(_runtimeEnvironment.GetSystemdServiceDirectory(), "cloudflared.service");
        var expectedContents = BuildLinuxCloudflaredUnit(tunnelId);

        if (_fileSystem.FileExists(unitPath))
        {
            var currentContents = await _fileSystem.ReadAllTextAsync(unitPath, cancellationToken);
            if (string.Equals(currentContents, expectedContents, StringComparison.Ordinal))
            {
                return new CommandResult { ExitCode = 0 };
            }
        }

        await _fileSystem.WriteAllBytesAsync(unitPath, Encoding.UTF8.GetBytes(expectedContents), cancellationToken);

        var reloadResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "systemctl",
            Arguments = ["daemon-reload"]
        }, cancellationToken);

        if (!reloadResult.Succeeded)
        {
            return reloadResult;
        }

        var enableResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "systemctl",
            Arguments = ["enable", "cloudflared"]
        }, cancellationToken);

        return enableResult.Succeeded
            ? new CommandResult { ExitCode = 0 }
            : enableResult;
    }

    private string BuildLinuxCloudflaredUnit(string tunnelId)
    {
        var binaryPath = GetBinaryPathForLinux();
        var configPath = _runtimeEnvironment.GetCloudflaredConfigPath();
        return $$"""
[Unit]
Description=cloudflared tunnel
After=network-online.target
Wants=network-online.target

[Service]
TimeoutStartSec=0
Type=notify
ExecStart={{binaryPath}} tunnel --config {{configPath}} run {{tunnelId}}
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
""";
    }

    private async Task<bool> IsServiceInstalledAsync(CancellationToken cancellationToken)
    {
        var result = _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? await _commandRunner.RunAsync(new CommandRequest { Command = "sc", Arguments = ["query", "cloudflared"] }, cancellationToken)
            : await _commandRunner.RunAsync(new CommandRequest { Command = "systemctl", Arguments = ["show", "cloudflared", "--property", "LoadState"] }, cancellationToken);

        if (_runtimeEnvironment.GetCurrentOs() == OsType.Windows)
        {
            return result.Succeeded;
        }

        return result.Succeeded &&
               result.StdOut.Contains("LoadState=loaded", StringComparison.OrdinalIgnoreCase);
    }

    private Task<string> GetBinaryCommandAsync(CancellationToken cancellationToken)
        => _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? GetWindowsBinaryPathAsync(cancellationToken)
            : Task.FromResult("cloudflared");

    private Task<string> GetBinaryPathAsync(CancellationToken cancellationToken)
        => _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? GetWindowsBinaryPathAsync(cancellationToken)
            : Task.FromResult(GetBinaryPathForLinux());

    private string GetDownloadUrl()
        => _runtimeEnvironment.GetCurrentOs() == OsType.Windows ? WindowsDownloadUrl : LinuxDownloadUrl;

    private string GetBinaryPathForLinux() => "/usr/local/bin/cloudflared";

    private async Task<string> GetWindowsBinaryPathAsync(CancellationToken cancellationToken)
    {
        var resolvedPath = await TryResolveWindowsBinaryPathAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(resolvedPath) ? @"C:\Cloudflared\bin\cloudflared.exe" : resolvedPath;
    }

    private async Task<string> TryResolveWindowsBinaryPathAsync(CancellationToken cancellationToken)
    {
        try
        {
            var whereResult = await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "where",
                Arguments = ["cloudflared"]
            }, cancellationToken);

            if (whereResult.Succeeded)
            {
                var lines = whereResult.StdOut
                    .ReplaceLineEndings("\n")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var line in lines)
                {
                    if (_fileSystem.FileExists(line))
                    {
                        return line;
                    }
                }
            }
        }
        catch
        {
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in pathEntries)
        {
            try
            {
                var candidate = Path.Combine(entry, "cloudflared.exe");
                if (_fileSystem.FileExists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        var fallback = @"C:\Cloudflared\bin\cloudflared.exe";
        return _fileSystem.FileExists(fallback) ? fallback : string.Empty;
    }

    private string GetMetadataPath()
    {
        var configPath = _runtimeEnvironment.GetCloudflaredConfigPath();
        var configDirectory = Path.GetDirectoryName(configPath) ?? Path.GetTempPath();
        return Path.Combine(configDirectory, "serverops-tunnel.json");
    }

    private string GetManagedCredentialsPath(string tunnelId)
    {
        var configDirectory = Path.GetDirectoryName(_runtimeEnvironment.GetCloudflaredConfigPath()) ?? Path.GetTempPath();
        return Path.Combine(configDirectory, $"{tunnelId}.json");
    }

    private string GetManagedCredentialsPathForNewTunnel()
    {
        var configDirectory = Path.GetDirectoryName(_runtimeEnvironment.GetCloudflaredConfigPath()) ?? Path.GetTempPath();
        return Path.Combine(configDirectory, "tunnel-credentials.json");
    }

    private string GetTunnelName() => Environment.MachineName.Trim().ToLowerInvariant();

    private async Task<RemoteTunnelMetadata> ReadMetadataAsync(string metadataPath, CancellationToken cancellationToken)
    {
        var content = await _fileSystem.ReadAllTextAsync(metadataPath, cancellationToken);
        var metadata = JsonSerializer.Deserialize<RemoteTunnelMetadata>(content);
        return metadata ?? new RemoteTunnelMetadata();
    }

    private async Task WriteMetadataAsync(RemoteTunnelMetadata metadata, CancellationToken cancellationToken)
    {
        var content = JsonSerializer.Serialize(metadata);
        await _fileSystem.WriteAllBytesAsync(GetMetadataPath(), Encoding.UTF8.GetBytes(content), cancellationToken);
    }

    private async Task WriteConfigAsync(string tunnelId, string credentialsPath, CancellationToken cancellationToken)
    {
        var configPath = _runtimeEnvironment.GetCloudflaredConfigPath();
        var configDirectory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(configDirectory))
        {
            _fileSystem.CreateDirectory(configDirectory);
        }

        var contents = $$"""
tunnel: {{tunnelId}}
credentials-file: {{FormatYamlPath(credentialsPath)}}
ingress:
  - service: http_status:404
""";

        await _fileSystem.WriteAllBytesAsync(configPath, Encoding.UTF8.GetBytes(contents + Environment.NewLine), cancellationToken);
    }

    private async Task<ParsedTunnelConfig> ReadConfigAsync(string configPath, CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(configPath))
        {
            return new ParsedTunnelConfig();
        }

        var contents = await _fileSystem.ReadAllTextAsync(configPath, cancellationToken);
        var insideIngress = false;
        var tunnelId = string.Empty;
        var credentialsFile = string.Empty;
        var ingressRules = new List<string>();

        foreach (var rawLine in contents.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("tunnel:", StringComparison.OrdinalIgnoreCase))
            {
                tunnelId = line["tunnel:".Length..].Trim().Trim('"');
                continue;
            }

            if (line.StartsWith("credentials-file:", StringComparison.OrdinalIgnoreCase))
            {
                credentialsFile = line["credentials-file:".Length..].Trim().Trim('"');
                continue;
            }

            if (line.StartsWith("ingress:", StringComparison.OrdinalIgnoreCase))
            {
                insideIngress = true;
                continue;
            }

            if (!insideIngress)
            {
                continue;
            }

            if (line.StartsWith("- hostname:", StringComparison.OrdinalIgnoreCase))
            {
                ingressRules.Add(line["- hostname:".Length..].Trim().Trim('"'));
            }
            else if (line.StartsWith("service:", StringComparison.OrdinalIgnoreCase))
            {
                ingressRules.Add(line["service:".Length..].Trim().Trim('"'));
            }
        }

        return new ParsedTunnelConfig
        {
            TunnelId = tunnelId,
            CredentialsFile = credentialsFile,
            IngressRules = ingressRules
        };
    }

    private static string FormatYamlPath(string path)
        => $"\"{path.Replace("\\", "/", StringComparison.Ordinal)}\"";

    private static string Quote(string value) => $"\"{value}\"";

    private static string ExtractTunnelId(params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var match = Regex.Match(value, @"\b[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}\b", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Value;
            }
        }

        return string.Empty;
    }

    private async Task<string> FindTunnelIdByNameAsync(string tunnelName, CancellationToken cancellationToken)
    {
        var listResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = await GetBinaryCommandAsync(cancellationToken),
            Arguments = ["tunnel", "list", "--output", "json"]
        }, cancellationToken);

        if (!listResult.Succeeded || string.IsNullOrWhiteSpace(listResult.StdOut))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(listResult.StdOut);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : string.Empty;
                if (!string.Equals(name, tunnelName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return item.TryGetProperty("id", out var idProperty) ? idProperty.GetString() ?? string.Empty : string.Empty;
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static bool IsTunnelAlreadyExistsError(CommandResult result)
    {
        var combined = string.Join(
            Environment.NewLine,
            new[] { result.StdOut, result.StdErr }.Where(static value => !string.IsNullOrWhiteSpace(value)));

        return combined.Contains("already exists", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFirstRelevantLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lines = value
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            if (line.Contains("outdated", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("WRN ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return line;
        }

        return lines.FirstOrDefault() ?? string.Empty;
    }

    private async Task<string> ReadCredentialsPathAsync(string configPath, CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(configPath))
        {
            return string.Empty;
        }

        var parsedConfig = await ReadConfigAsync(configPath, cancellationToken);
        return parsedConfig.CredentialsFile;
    }

    private static string AppendDiagnosticDetails(string text, string details)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return details;
        }

        return $"{text}{Environment.NewLine}{details}";
    }

    private string GetLegacyCredentialsPath(string tunnelId)
    {
        if (_runtimeEnvironment.GetCurrentOs() == OsType.Windows)
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, ".cloudflared", $"{tunnelId}.json");
        }

        return Path.Combine("/root/.cloudflared", $"{tunnelId}.json");
    }

    private sealed class RemoteTunnelMetadata
    {
        public string TunnelId { get; init; } = string.Empty;
        public string TunnelName { get; init; } = string.Empty;
    }

    private sealed class ParsedTunnelConfig
    {
        public string TunnelId { get; init; } = string.Empty;
        public string CredentialsFile { get; init; } = string.Empty;
        public IReadOnlyList<string> IngressRules { get; init; } = Array.Empty<string>();
    }
}
