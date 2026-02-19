using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.TaskRuntime.Configuration;
using AgentsDashboard.TaskRuntime.MagicOnion;
using AgentsDashboard.TaskRuntime.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.TaskRuntimeGateway.MagicOnion;

public sealed class TaskRuntimeServiceFileMethodsTests
{
    [Test]
    public async Task FileRpcMethods_CreateReadListDelete_SucceedThroughService()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var service = CreateService(tempRoot);

            var create = await service.CreateRuntimeFileAsync(
                new CreateRuntimeFileRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = "artifacts/output.json",
                    Content = "{}"u8.ToArray(),
                    CreateParentDirectories = true,
                    Overwrite = false,
                },
                CancellationToken.None);

            await Assert.That(create.Success).IsTrue();

            var read = await service.ReadRuntimeFileAsync(
                new ReadRuntimeFileRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = "artifacts/output.json",
                    MaxBytes = 0,
                },
                CancellationToken.None);

            await Assert.That(read.Found).IsTrue();
            await Assert.That(read.IsDirectory).IsFalse();

            var list = await service.ListRuntimeFilesAsync(
                new ListRuntimeFilesRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = "artifacts",
                    IncludeHidden = false,
                },
                CancellationToken.None);

            await Assert.That(list.Success).IsTrue();
            await Assert.That(list.Entries.Count).IsEqualTo(1);
            await Assert.That(list.Entries[0].Name).IsEqualTo("output.json");

            var delete = await service.DeleteRuntimeFileAsync(
                new DeleteRuntimeFileRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = "artifacts/output.json",
                    Recursive = false,
                },
                CancellationToken.None);

            await Assert.That(delete.Success).IsTrue();
            await Assert.That(delete.Deleted).IsTrue();
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task FileRpcMethods_PropagateCancellation()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var service = CreateService(tempRoot);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Func<Task> action = async () =>
            {
                await service.ListRuntimeFilesAsync(
                    new ListRuntimeFilesRequest
                    {
                        RepositoryId = "repo-1",
                        TaskId = "task-1",
                        RelativePath = ".",
                        IncludeHidden = false,
                    },
                    cts.Token);
            };

            await Assert.That(action).Throws<OperationCanceledException>();
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static TaskRuntimeService CreateService(string tempRoot)
    {
        var options = new TaskRuntimeOptions
        {
            MaxSlots = 1,
            WorkspacesRootPath = tempRoot,
            CommandDefaultTimeoutSeconds = 30,
            CommandMaxTimeoutSeconds = 120,
            CommandMaxOutputBytes = 1_048_576,
            FileReadDefaultMaxBytes = 128,
            FileReadHardMaxBytes = 1_048_576,
        };

        var queue = new TaskRuntimeQueue(options);
        var eventBus = new TaskRuntimeEventBus();
        var commandService = new TaskRuntimeCommandService(
            eventBus,
            Options.Create(options),
            NullLogger<TaskRuntimeCommandService>.Instance);
        var fileSystemService = new TaskRuntimeFileSystemService(new WorkspacePathGuard(options), options);

        return new TaskRuntimeService(
            queue,
            commandService,
            fileSystemService,
            NullLogger<TaskRuntimeService>.Instance);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentsdashboard-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
