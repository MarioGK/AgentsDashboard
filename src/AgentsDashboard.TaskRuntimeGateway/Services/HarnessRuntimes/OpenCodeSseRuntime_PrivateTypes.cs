using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.TaskRuntime;

namespace AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;

public sealed partial class OpenCodeSseRuntime
    : IHarnessRuntime
{
    private sealed class OpenCodeServerHandle : IAsyncDisposable
    {
        private readonly Process? _process;
        private readonly Task _stdoutPump;
        private readonly Task _stderrPump;

        public OpenCodeServerHandle(
            OpenCodeApiClient client,
            Process? process = null,
            Task? stdoutPump = null,
            Task? stderrPump = null)
        {
            Client = client;
            _process = process;
            _stdoutPump = stdoutPump ?? Task.CompletedTask;
            _stderrPump = stderrPump ?? Task.CompletedTask;
        }

        public OpenCodeApiClient Client { get; }

        public async ValueTask DisposeAsync()
        {
            if (_process is not null)
            {
                await StopProcessAsync(_process);
            }

            try
            {
                await Task.WhenAll(_stdoutPump, _stderrPump);
            }
            catch
            {
            }

            Client.Dispose();
            _process?.Dispose();
        }
    }
}
