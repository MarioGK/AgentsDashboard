



namespace AgentsDashboard.UnitTests.TaskRuntime.Services;

public sealed class TaskRuntimeFileSystemServiceTests
{
    [Test]
    public async Task CreateThenRead_ReturnsSameBytes()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var service = CreateService(tempRoot);
            var content = new byte[] { 1, 2, 3, 4, 5 };

            var create = await service.CreateRuntimeFileAsync(
                new CreateRuntimeFileRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = "docs/result.bin",
                    Content = content,
                    CreateParentDirectories = true,
                    Overwrite = false,
                },
                CancellationToken.None);

            await Assert.That(create.Success).IsTrue();
            await Assert.That(create.Created).IsTrue();
            await Assert.That(create.ContentLength).IsEqualTo(content.LongLength);

            var read = await service.ReadRuntimeFileAsync(
                new ReadRuntimeFileRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = "docs/result.bin",
                    MaxBytes = 0,
                },
                CancellationToken.None);

            await Assert.That(read.Found).IsTrue();
            await Assert.That(read.IsDirectory).IsFalse();
            await Assert.That(read.Truncated).IsFalse();
            await Assert.That(read.ContentLength).IsEqualTo(content.LongLength);

            if (read.Content is null)
            {
                throw new InvalidOperationException("Expected file content.");
            }

            await Assert.That(read.Content.AsSpan().SequenceEqual(content)).IsTrue();
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task List_ReturnsDirectoryThenFileWithExpectedMetadata()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var service = CreateService(tempRoot);
            var workspacePath = BuildWorkspacePath(tempRoot, "repo-1", "task-1");
            Directory.CreateDirectory(Path.Combine(workspacePath, "b-folder"));
            Directory.CreateDirectory(Path.Combine(workspacePath, "a-folder"));
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "c-file.txt"), "one");
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "b-file.txt"), "two");
            await File.WriteAllTextAsync(Path.Combine(workspacePath, ".hidden.txt"), "hidden");

            var list = await service.ListRuntimeFilesAsync(
                new ListRuntimeFilesRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = ".",
                    IncludeHidden = false,
                },
                CancellationToken.None);

            await Assert.That(list.Success).IsTrue();
            await Assert.That(list.Found).IsTrue();
            await Assert.That(list.IsDirectory).IsTrue();
            await Assert.That(list.Entries.Count).IsEqualTo(4);

            await Assert.That(list.Entries[0].Name).IsEqualTo("a-folder");
            await Assert.That(list.Entries[0].IsDirectory).IsTrue();
            await Assert.That(list.Entries[0].RelativePath).IsEqualTo("a-folder");

            await Assert.That(list.Entries[1].Name).IsEqualTo("b-folder");
            await Assert.That(list.Entries[1].IsDirectory).IsTrue();
            await Assert.That(list.Entries[1].RelativePath).IsEqualTo("b-folder");

            await Assert.That(list.Entries[2].Name).IsEqualTo("b-file.txt");
            await Assert.That(list.Entries[2].IsDirectory).IsFalse();
            await Assert.That(list.Entries[2].RelativePath).IsEqualTo("b-file.txt");

            await Assert.That(list.Entries[3].Name).IsEqualTo("c-file.txt");
            await Assert.That(list.Entries[3].IsDirectory).IsFalse();
            await Assert.That(list.Entries[3].RelativePath).IsEqualTo("c-file.txt");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task Delete_FileThenSecondDelete_ReturnsNotFound()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var service = CreateService(tempRoot);

            await service.CreateRuntimeFileAsync(
                new CreateRuntimeFileRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = "out/result.txt",
                    Content = "ok"u8.ToArray(),
                    CreateParentDirectories = true,
                    Overwrite = false,
                },
                CancellationToken.None);

            var firstDelete = await service.DeleteRuntimeFileAsync(
                new DeleteRuntimeFileRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = "out/result.txt",
                    Recursive = false,
                },
                CancellationToken.None);

            await Assert.That(firstDelete.Success).IsTrue();
            await Assert.That(firstDelete.Deleted).IsTrue();
            await Assert.That(firstDelete.WasDirectory).IsFalse();

            var secondDelete = await service.DeleteRuntimeFileAsync(
                new DeleteRuntimeFileRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = "out/result.txt",
                    Recursive = false,
                },
                CancellationToken.None);

            await Assert.That(secondDelete.Success).IsFalse();
            await Assert.That(secondDelete.Deleted).IsFalse();
            await Assert.That(secondDelete.Reason).IsEqualTo("not_found");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task RejectsPathTraversalOutsideWorkspace()
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
                    RelativePath = "../escape.txt",
                    Content = "escape"u8.ToArray(),
                    CreateParentDirectories = true,
                    Overwrite = false,
                },
                CancellationToken.None);

            await Assert.That(create.Success).IsFalse();
            await Assert.That(create.Reason).IsEqualTo("path_outside_workspace");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task Read_TruncatesWhenFileExceedsMaxBytes()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var service = CreateService(tempRoot);
            var content = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

            await service.CreateRuntimeFileAsync(
                new CreateRuntimeFileRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = "trace.bin",
                    Content = content,
                    CreateParentDirectories = true,
                    Overwrite = false,
                },
                CancellationToken.None);

            var read = await service.ReadRuntimeFileAsync(
                new ReadRuntimeFileRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = "trace.bin",
                    MaxBytes = 4,
                },
                CancellationToken.None);

            await Assert.That(read.Found).IsTrue();
            await Assert.That(read.Truncated).IsTrue();
            await Assert.That(read.ContentLength).IsEqualTo(content.LongLength);

            if (read.Content is null)
            {
                throw new InvalidOperationException("Expected read content.");
            }

            await Assert.That(read.Content.Length).IsEqualTo(4);
            await Assert.That(read.Content.AsSpan().SequenceEqual(content.AsSpan(0, 4))).IsTrue();
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task DeleteDirectory_RequiresRecursiveFlag()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var service = CreateService(tempRoot);
            var workspacePath = BuildWorkspacePath(tempRoot, "repo-1", "task-1");
            var directoryToDelete = Path.Combine(workspacePath, "logs");
            Directory.CreateDirectory(directoryToDelete);
            await File.WriteAllTextAsync(Path.Combine(directoryToDelete, "part.log"), "line");

            var nonRecursive = await service.DeleteRuntimeFileAsync(
                new DeleteRuntimeFileRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = "logs",
                    Recursive = false,
                },
                CancellationToken.None);

            await Assert.That(nonRecursive.Success).IsFalse();
            await Assert.That(nonRecursive.Deleted).IsFalse();
            await Assert.That(nonRecursive.WasDirectory).IsTrue();
            await Assert.That(nonRecursive.Reason).IsEqualTo("is_directory");
            await Assert.That(Directory.Exists(directoryToDelete)).IsTrue();

            var recursive = await service.DeleteRuntimeFileAsync(
                new DeleteRuntimeFileRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = "logs",
                    Recursive = true,
                },
                CancellationToken.None);

            await Assert.That(recursive.Success).IsTrue();
            await Assert.That(recursive.Deleted).IsTrue();
            await Assert.That(recursive.WasDirectory).IsTrue();
            await Assert.That(Directory.Exists(directoryToDelete)).IsFalse();
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static TaskRuntimeFileSystemService CreateService(string tempRoot)
    {
        var options = new TaskRuntimeOptions
        {
            WorkspacesRootPath = tempRoot,
            FileReadDefaultMaxBytes = 16,
            FileReadHardMaxBytes = 1024,
        };

        var guard = new WorkspacePathGuard(options);
        return new TaskRuntimeFileSystemService(guard, options);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentsdashboard-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string BuildWorkspacePath(string rootPath, string repositoryId, string taskId)
    {
        return Path.Combine(rootPath, SanitizePathSegment(repositoryId), "tasks", SanitizePathSegment(taskId));
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return value.Trim().Replace('/', '-').Replace('\\', '-');
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
