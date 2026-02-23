


using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.TaskRuntime.Services;

public sealed class TaskRuntimeCommandServiceTests
{
    [Test]
    public async Task StartCommandAsync_CompletesAndReportsStatus()
    {
        var service = CreateService();

        var start = await service.StartCommandAsync(
            new StartRuntimeCommandRequest
            {
                RunId = "run-1",
                TaskId = "task-1",
                ExecutionToken = "exec-1",
                Command = "sh",
                Arguments = ["-c", "printf 'hello-from-runtime-command'"],
            },
            CancellationToken.None);

        await Assert.That(start.Success).IsTrue();
        await Assert.That(string.IsNullOrWhiteSpace(start.CommandId)).IsFalse();

        var status = await WaitForTerminalStatusAsync(service, start.CommandId, CancellationToken.None);
        await Assert.That(status.Found).IsTrue();
        await Assert.That(status.Status).IsEqualTo(RuntimeCommandStatusValue.Completed);
        await Assert.That(status.ExitCode).IsEqualTo(0);
        await Assert.That(status.StandardOutputBytes > 0).IsTrue();
        await Assert.That(status.TimedOut).IsFalse();
        await Assert.That(status.Canceled).IsFalse();
    }

    [Test]
    public async Task StartCommandAsync_RejectsMissingCommand()
    {
        var service = CreateService();

        var start = await service.StartCommandAsync(
            new StartRuntimeCommandRequest
            {
                RunId = "run-2",
                TaskId = "task-2",
                ExecutionToken = "exec-2",
                Command = string.Empty,
            },
            CancellationToken.None);

        await Assert.That(start.Success).IsFalse();
        await Assert.That(start.ErrorMessage).Contains("required");
    }

    private static TaskRuntimeCommandService CreateService()
    {
        var eventBus = new TaskRuntimeEventBus();
        var options = Options.Create(new TaskRuntimeOptions
        {
            CommandDefaultTimeoutSeconds = 30,
            CommandMaxTimeoutSeconds = 120,
            CommandMaxOutputBytes = 1_048_576,
        });
        return new TaskRuntimeCommandService(
            eventBus,
            options,
            NullLogger<TaskRuntimeCommandService>.Instance);
    }

    private static async Task<RuntimeCommandStatusResult> WaitForTerminalStatusAsync(
        TaskRuntimeCommandService service,
        string commandId,
        CancellationToken cancellationToken)
    {
        RuntimeCommandStatusResult? latest = null;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            latest = await service.GetCommandStatusAsync(
                new GetRuntimeCommandStatusRequest { CommandId = commandId },
                cancellationToken);

            if (latest.Found &&
                latest.Status is RuntimeCommandStatusValue.Completed or RuntimeCommandStatusValue.Failed or RuntimeCommandStatusValue.Canceled or RuntimeCommandStatusValue.TimedOut)
            {
                return latest;
            }

            await Task.Delay(25, cancellationToken);
        }

        return latest ?? new RuntimeCommandStatusResult { Found = false, CommandId = commandId };
    }
}
