using System.Text;
using AgentsDashboard.Contracts.TaskRuntime;

namespace AgentsDashboard.TaskRuntime.Services;

public sealed class TaskRuntimeFileService(
    WorkspacePathGuard workspacePathGuard,
    ILogger<TaskRuntimeFileService> logger)
{
    public ValueTask<DirectoryListingDto> ListDirectoryAsync(FileSystemRequest request, CancellationToken cancellationToken)
    {
        var directoryPath = workspacePathGuard.ResolvePath(request.Path);
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory '{request.Path}' was not found.");
        }

        var entries = new List<DirectoryEntryDto>();
        var searchOption = request.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var directory in Directory.EnumerateDirectories(directoryPath, "*", searchOption))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new DirectoryInfo(directory);
            if (!ShouldInclude(info, request.IncludeHidden))
            {
                continue;
            }

            entries.Add(CreateDirectoryEntry(info));
        }

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", searchOption))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            if (!ShouldInclude(info, request.IncludeHidden))
            {
                continue;
            }

            entries.Add(CreateFileEntry(info));
        }

        entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));

        return ValueTask.FromResult(new DirectoryListingDto
        {
            Path = workspacePathGuard.ToRelativePath(directoryPath),
            Entries = entries,
        });
    }

    public async ValueTask<FileContentDto> ReadFileAsync(FileReadRequest request, CancellationToken cancellationToken)
    {
        var filePath = workspacePathGuard.ResolvePath(request.Path);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File '{request.Path}' was not found.", request.Path);
        }

        var encoding = ResolveEncoding(request.Encoding);
        var content = await File.ReadAllTextAsync(filePath, encoding, cancellationToken);
        var fileInfo = new FileInfo(filePath);

        return new FileContentDto
        {
            Path = workspacePathGuard.ToRelativePath(filePath),
            Encoding = encoding.WebName,
            Content = content,
            SizeBytes = fileInfo.Length,
        };
    }

    public async ValueTask<WriteFileResult> WriteFileAsync(WriteFileRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var filePath = workspacePathGuard.ResolvePath(request.Path);
            if (workspacePathGuard.IsWorkspaceRoot(filePath))
            {
                return new WriteFileResult
                {
                    Success = false,
                    ErrorMessage = "Workspace root cannot be overwritten.",
                    BytesWritten = 0,
                };
            }

            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath) && !Directory.Exists(directoryPath))
            {
                if (!request.CreateDirectories)
                {
                    return new WriteFileResult
                    {
                        Success = false,
                        ErrorMessage = $"Directory '{directoryPath}' does not exist.",
                        BytesWritten = 0,
                    };
                }

                Directory.CreateDirectory(directoryPath);
            }

            var encoding = ResolveEncoding(request.Encoding);
            var content = request.Content;
            await File.WriteAllTextAsync(filePath, content, encoding, cancellationToken);

            return new WriteFileResult
            {
                Success = true,
                ErrorMessage = null,
                BytesWritten = encoding.GetByteCount(content),
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write file path {Path}", request.Path);
            return new WriteFileResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                BytesWritten = 0,
            };
        }
    }

    public ValueTask<DeletePathResult> DeletePathAsync(DeletePathRequest request, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullPath = workspacePathGuard.ResolvePath(request.Path);
            if (workspacePathGuard.IsWorkspaceRoot(fullPath))
            {
                return ValueTask.FromResult(new DeletePathResult
                {
                    Success = false,
                    ErrorMessage = "Workspace root cannot be deleted.",
                    Deleted = false,
                });
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return ValueTask.FromResult(new DeletePathResult
                {
                    Success = true,
                    ErrorMessage = null,
                    Deleted = true,
                });
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, request.Recursive);
                return ValueTask.FromResult(new DeletePathResult
                {
                    Success = true,
                    ErrorMessage = null,
                    Deleted = true,
                });
            }

            return ValueTask.FromResult(new DeletePathResult
            {
                Success = true,
                ErrorMessage = null,
                Deleted = false,
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete path {Path}", request.Path);
            return ValueTask.FromResult(new DeletePathResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Deleted = false,
            });
        }
    }

    private static bool ShouldInclude(FileSystemInfo entry, bool includeHidden)
    {
        if (includeHidden)
        {
            return true;
        }

        if (entry.Name.StartsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        return (entry.Attributes & FileAttributes.Hidden) == 0;
    }

    private DirectoryEntryDto CreateDirectoryEntry(DirectoryInfo directoryInfo)
    {
        return new DirectoryEntryDto
        {
            Name = directoryInfo.Name,
            Path = workspacePathGuard.ToRelativePath(directoryInfo.FullName),
            IsDirectory = true,
            SizeBytes = 0,
            LastModifiedAt = new DateTimeOffset(directoryInfo.LastWriteTimeUtc),
        };
    }

    private DirectoryEntryDto CreateFileEntry(FileInfo fileInfo)
    {
        return new DirectoryEntryDto
        {
            Name = fileInfo.Name,
            Path = workspacePathGuard.ToRelativePath(fileInfo.FullName),
            IsDirectory = false,
            SizeBytes = fileInfo.Length,
            LastModifiedAt = new DateTimeOffset(fileInfo.LastWriteTimeUtc),
        };
    }

    private static Encoding ResolveEncoding(string? encodingName)
    {
        if (string.IsNullOrWhiteSpace(encodingName))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(encodingName);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}
