using System.Collections.Generic;
using System.IO;
using System.Linq;



namespace AgentsDashboard.TaskRuntime.Features.FileSystem.Services;

public sealed class TaskRuntimeFileSystemService(
    WorkspacePathGuard workspacePathGuard,
    TaskRuntimeOptions options)
{
    public ValueTask<ListRuntimeFilesResult> ListRuntimeFilesAsync(ListRuntimeFilesRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveWorkspacePath(request.RepositoryId, request.TaskId, out var workspacePath, out var scopeError))
        {
            return ValueTask.FromResult(new ListRuntimeFilesResult
            {
                Success = false,
                Found = false,
                IsDirectory = false,
                Reason = scopeError,
                ResolvedRelativePath = ".",
                Entries = [],
            });
        }

        if (!TryNormalizeRelativePath(request.RelativePath, allowCurrentDirectory: true, out var normalizedRelativePath, out var normalizeError))
        {
            return ValueTask.FromResult(new ListRuntimeFilesResult
            {
                Success = false,
                Found = false,
                IsDirectory = false,
                Reason = normalizeError,
                ResolvedRelativePath = ".",
                Entries = [],
            });
        }

        if (!TryResolveTargetPath(workspacePath, normalizedRelativePath, out var targetPath, out var pathError))
        {
            return ValueTask.FromResult(new ListRuntimeFilesResult
            {
                Success = false,
                Found = false,
                IsDirectory = false,
                Reason = pathError,
                ResolvedRelativePath = normalizedRelativePath,
                Entries = [],
            });
        }

        var resolvedRelativePath = ToRelativePath(workspacePath, targetPath);

        if (!Directory.Exists(workspacePath))
        {
            if (string.Equals(normalizedRelativePath, ".", StringComparison.Ordinal))
            {
                return ValueTask.FromResult(new ListRuntimeFilesResult
                {
                    Success = true,
                    Found = true,
                    IsDirectory = true,
                    Reason = null,
                    ResolvedRelativePath = ".",
                    Entries = [],
                });
            }

            return ValueTask.FromResult(new ListRuntimeFilesResult
            {
                Success = false,
                Found = false,
                IsDirectory = false,
                Reason = "not_found",
                ResolvedRelativePath = resolvedRelativePath,
                Entries = [],
            });
        }

        if (File.Exists(targetPath))
        {
            return ValueTask.FromResult(new ListRuntimeFilesResult
            {
                Success = false,
                Found = true,
                IsDirectory = false,
                Reason = "not_directory",
                ResolvedRelativePath = resolvedRelativePath,
                Entries = [],
            });
        }

        if (!Directory.Exists(targetPath))
        {
            return ValueTask.FromResult(new ListRuntimeFilesResult
            {
                Success = false,
                Found = false,
                IsDirectory = false,
                Reason = "not_found",
                ResolvedRelativePath = resolvedRelativePath,
                Entries = [],
            });
        }

        try
        {
            var directories = Directory.EnumerateDirectories(targetPath)
                .Select(static path => new DirectoryInfo(path))
                .Where(info => request.IncludeHidden || !IsHidden(info))
                .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                .Select(info => new RuntimeFileSystemEntry
                {
                    Name = info.Name,
                    RelativePath = ToRelativePath(workspacePath, info.FullName),
                    IsDirectory = true,
                    Length = 0,
                    LastModifiedUtc = info.LastWriteTimeUtc,
                });

            var files = Directory.EnumerateFiles(targetPath)
                .Select(static path => new FileInfo(path))
                .Where(info => request.IncludeHidden || !IsHidden(info))
                .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                .Select(info => new RuntimeFileSystemEntry
                {
                    Name = info.Name,
                    RelativePath = ToRelativePath(workspacePath, info.FullName),
                    IsDirectory = false,
                    Length = info.Length,
                    LastModifiedUtc = info.LastWriteTimeUtc,
                });

            var entries = directories.Concat(files).ToList();

            return ValueTask.FromResult(new ListRuntimeFilesResult
            {
                Success = true,
                Found = true,
                IsDirectory = true,
                Reason = null,
                ResolvedRelativePath = resolvedRelativePath,
                Entries = entries,
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(new ListRuntimeFilesResult
            {
                Success = false,
                Found = false,
                IsDirectory = true,
                Reason = ex.Message,
                ResolvedRelativePath = resolvedRelativePath,
                Entries = [],
            });
        }
    }

    public async ValueTask<CreateRuntimeFileResult> CreateRuntimeFileAsync(CreateRuntimeFileRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveWorkspacePath(request.RepositoryId, request.TaskId, out var workspacePath, out var scopeError))
        {
            return new CreateRuntimeFileResult
            {
                Success = false,
                Created = false,
                Reason = scopeError,
                RelativePath = string.Empty,
                ContentLength = 0,
            };
        }

        if (!TryNormalizeRelativePath(request.RelativePath, allowCurrentDirectory: false, out var normalizedRelativePath, out var normalizeError))
        {
            return new CreateRuntimeFileResult
            {
                Success = false,
                Created = false,
                Reason = normalizeError,
                RelativePath = string.Empty,
                ContentLength = 0,
            };
        }

        if (!TryResolveTargetPath(workspacePath, normalizedRelativePath, out var targetPath, out var pathError))
        {
            return new CreateRuntimeFileResult
            {
                Success = false,
                Created = false,
                Reason = pathError,
                RelativePath = normalizedRelativePath,
                ContentLength = 0,
            };
        }

        var resolvedRelativePath = ToRelativePath(workspacePath, targetPath);

        try
        {
            Directory.CreateDirectory(workspacePath);

            if (Directory.Exists(targetPath))
            {
                return new CreateRuntimeFileResult
                {
                    Success = false,
                    Created = false,
                    Reason = "target_is_directory",
                    RelativePath = resolvedRelativePath,
                    ContentLength = 0,
                };
            }

            var fileAlreadyExists = File.Exists(targetPath);
            if (fileAlreadyExists && !request.Overwrite)
            {
                return new CreateRuntimeFileResult
                {
                    Success = false,
                    Created = false,
                    Reason = "already_exists",
                    RelativePath = resolvedRelativePath,
                    ContentLength = 0,
                };
            }

            var parentPath = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(parentPath))
            {
                return new CreateRuntimeFileResult
                {
                    Success = false,
                    Created = false,
                    Reason = "invalid_parent_path",
                    RelativePath = resolvedRelativePath,
                    ContentLength = 0,
                };
            }

            var createParentDirectories = request.CreateParentDirectories ?? true;
            if (!Directory.Exists(parentPath))
            {
                if (!createParentDirectories)
                {
                    return new CreateRuntimeFileResult
                    {
                        Success = false,
                        Created = false,
                        Reason = "parent_not_found",
                        RelativePath = resolvedRelativePath,
                        ContentLength = 0,
                    };
                }

                Directory.CreateDirectory(parentPath);
            }

            var content = request.Content ?? [];
            var fileMode = request.Overwrite ? FileMode.Create : FileMode.CreateNew;

            await using (var stream = new FileStream(targetPath, fileMode, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await stream.WriteAsync(content, cancellationToken);
            }

            return new CreateRuntimeFileResult
            {
                Success = true,
                Created = !fileAlreadyExists,
                Reason = null,
                RelativePath = resolvedRelativePath,
                ContentLength = content.LongLength,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CreateRuntimeFileResult
            {
                Success = false,
                Created = false,
                Reason = ex.Message,
                RelativePath = resolvedRelativePath,
                ContentLength = 0,
            };
        }
    }

    public async ValueTask<ReadRuntimeFileResult> ReadRuntimeFileAsync(ReadRuntimeFileRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveWorkspacePath(request.RepositoryId, request.TaskId, out var workspacePath, out var scopeError))
        {
            return new ReadRuntimeFileResult
            {
                Found = false,
                IsDirectory = false,
                Truncated = false,
                ContentLength = 0,
                Content = null,
                ContentType = null,
                Reason = scopeError,
                RelativePath = string.Empty,
            };
        }

        if (!TryNormalizeRelativePath(request.RelativePath, allowCurrentDirectory: false, out var normalizedRelativePath, out var normalizeError))
        {
            return new ReadRuntimeFileResult
            {
                Found = false,
                IsDirectory = false,
                Truncated = false,
                ContentLength = 0,
                Content = null,
                ContentType = null,
                Reason = normalizeError,
                RelativePath = string.Empty,
            };
        }

        if (!TryResolveTargetPath(workspacePath, normalizedRelativePath, out var targetPath, out var pathError))
        {
            return new ReadRuntimeFileResult
            {
                Found = false,
                IsDirectory = false,
                Truncated = false,
                ContentLength = 0,
                Content = null,
                ContentType = null,
                Reason = pathError,
                RelativePath = normalizedRelativePath,
            };
        }

        var resolvedRelativePath = ToRelativePath(workspacePath, targetPath);

        if (!Directory.Exists(workspacePath))
        {
            return new ReadRuntimeFileResult
            {
                Found = false,
                IsDirectory = false,
                Truncated = false,
                ContentLength = 0,
                Content = null,
                ContentType = null,
                Reason = "not_found",
                RelativePath = resolvedRelativePath,
            };
        }

        if (Directory.Exists(targetPath))
        {
            return new ReadRuntimeFileResult
            {
                Found = true,
                IsDirectory = true,
                Truncated = false,
                ContentLength = 0,
                Content = null,
                ContentType = null,
                Reason = "is_directory",
                RelativePath = resolvedRelativePath,
            };
        }

        if (!File.Exists(targetPath))
        {
            return new ReadRuntimeFileResult
            {
                Found = false,
                IsDirectory = false,
                Truncated = false,
                ContentLength = 0,
                Content = null,
                ContentType = null,
                Reason = "not_found",
                RelativePath = resolvedRelativePath,
            };
        }

        try
        {
            var fileInfo = new FileInfo(targetPath);
            var contentLength = fileInfo.Length;
            var maxBytes = ResolveReadMaxBytes(request.MaxBytes);
            var bytesToRead = (int)Math.Min(contentLength, maxBytes);
            var buffer = new byte[bytesToRead];

            await using (var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true))
            {
                var totalRead = 0;
                while (totalRead < bytesToRead)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(totalRead, bytesToRead - totalRead), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    totalRead += read;
                }

                if (totalRead != buffer.Length)
                {
                    Array.Resize(ref buffer, totalRead);
                }

                return new ReadRuntimeFileResult
                {
                    Found = true,
                    IsDirectory = false,
                    Truncated = contentLength > totalRead,
                    ContentLength = contentLength,
                    Content = buffer,
                    ContentType = ResolveContentType(targetPath),
                    Reason = null,
                    RelativePath = resolvedRelativePath,
                };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ReadRuntimeFileResult
            {
                Found = false,
                IsDirectory = false,
                Truncated = false,
                ContentLength = 0,
                Content = null,
                ContentType = null,
                Reason = ex.Message,
                RelativePath = resolvedRelativePath,
            };
        }
    }

    public ValueTask<DeleteRuntimeFileResult> DeleteRuntimeFileAsync(DeleteRuntimeFileRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveWorkspacePath(request.RepositoryId, request.TaskId, out var workspacePath, out var scopeError))
        {
            return ValueTask.FromResult(new DeleteRuntimeFileResult
            {
                Success = false,
                Deleted = false,
                WasDirectory = false,
                Reason = scopeError,
                RelativePath = string.Empty,
            });
        }

        if (!TryNormalizeRelativePath(request.RelativePath, allowCurrentDirectory: false, out var normalizedRelativePath, out var normalizeError))
        {
            return ValueTask.FromResult(new DeleteRuntimeFileResult
            {
                Success = false,
                Deleted = false,
                WasDirectory = false,
                Reason = normalizeError,
                RelativePath = string.Empty,
            });
        }

        if (!TryResolveTargetPath(workspacePath, normalizedRelativePath, out var targetPath, out var pathError))
        {
            return ValueTask.FromResult(new DeleteRuntimeFileResult
            {
                Success = false,
                Deleted = false,
                WasDirectory = false,
                Reason = pathError,
                RelativePath = normalizedRelativePath,
            });
        }

        var resolvedRelativePath = ToRelativePath(workspacePath, targetPath);
        if (string.Equals(resolvedRelativePath, ".", StringComparison.Ordinal))
        {
            return ValueTask.FromResult(new DeleteRuntimeFileResult
            {
                Success = false,
                Deleted = false,
                WasDirectory = true,
                Reason = "cannot_delete_workspace_root",
                RelativePath = resolvedRelativePath,
            });
        }

        if (!Directory.Exists(workspacePath))
        {
            return ValueTask.FromResult(new DeleteRuntimeFileResult
            {
                Success = false,
                Deleted = false,
                WasDirectory = false,
                Reason = "not_found",
                RelativePath = resolvedRelativePath,
            });
        }

        try
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
                return ValueTask.FromResult(new DeleteRuntimeFileResult
                {
                    Success = true,
                    Deleted = true,
                    WasDirectory = false,
                    Reason = null,
                    RelativePath = resolvedRelativePath,
                });
            }

            if (Directory.Exists(targetPath))
            {
                if (!request.Recursive)
                {
                    return ValueTask.FromResult(new DeleteRuntimeFileResult
                    {
                        Success = false,
                        Deleted = false,
                        WasDirectory = true,
                        Reason = "is_directory",
                        RelativePath = resolvedRelativePath,
                    });
                }

                Directory.Delete(targetPath, recursive: true);
                return ValueTask.FromResult(new DeleteRuntimeFileResult
                {
                    Success = true,
                    Deleted = true,
                    WasDirectory = true,
                    Reason = null,
                    RelativePath = resolvedRelativePath,
                });
            }

            return ValueTask.FromResult(new DeleteRuntimeFileResult
            {
                Success = false,
                Deleted = false,
                WasDirectory = false,
                Reason = "not_found",
                RelativePath = resolvedRelativePath,
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(new DeleteRuntimeFileResult
            {
                Success = false,
                Deleted = false,
                WasDirectory = Directory.Exists(targetPath),
                Reason = ex.Message,
                RelativePath = resolvedRelativePath,
            });
        }
    }

    private bool TryResolveWorkspacePath(string? repositoryId, string? taskId, out string workspacePath, out string? error)
    {
        workspacePath = string.Empty;

        if (string.IsNullOrWhiteSpace(repositoryId) || string.IsNullOrWhiteSpace(taskId))
        {
            error = "repository_id_and_task_id_are_required";
            return false;
        }

        var candidatePath = Path.Combine(
            workspacePathGuard.WorkspaceRootPath,
            SanitizePathSegment(repositoryId),
            "tasks",
            SanitizePathSegment(taskId));

        try
        {
            workspacePath = workspacePathGuard.ResolvePath(candidatePath);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryNormalizeRelativePath(string? relativePath, bool allowCurrentDirectory, out string normalizedPath, out string? error)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            if (!allowCurrentDirectory)
            {
                normalizedPath = string.Empty;
                error = "relative_path_required";
                return false;
            }

            normalizedPath = ".";
            error = null;
            return true;
        }

        var normalized = relativePath.Trim().Replace('\\', '/');
        if (!allowCurrentDirectory && string.Equals(normalized, ".", StringComparison.Ordinal))
        {
            normalizedPath = string.Empty;
            error = "relative_path_required";
            return false;
        }

        normalizedPath = normalized;
        error = null;
        return true;
    }

    private static bool TryResolveTargetPath(string workspacePath, string relativePath, out string targetPath, out string? error)
    {
        targetPath = string.Empty;

        if (Path.IsPathRooted(relativePath))
        {
            error = "absolute_paths_are_not_allowed";
            return false;
        }

        try
        {
            var resolvedPath = Path.GetFullPath(Path.Combine(workspacePath, relativePath));
            if (!IsWithinRoot(resolvedPath, workspacePath))
            {
                error = "path_outside_workspace";
                return false;
            }

            targetPath = resolvedPath;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool IsWithinRoot(string path, string rootPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalizedRoot = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;

        return string.Equals(path, rootPath, comparison)
               || path.StartsWith(normalizedRoot, comparison)
               || path.StartsWith(rootPath + Path.AltDirectorySeparatorChar, comparison);
    }

    private static string ToRelativePath(string workspacePath, string path)
    {
        var relativePath = Path.GetRelativePath(workspacePath, path);
        if (string.Equals(relativePath, ".", StringComparison.Ordinal))
        {
            return ".";
        }

        return relativePath.Replace('\\', '/');
    }

    private int ResolveReadMaxBytes(int requestedMaxBytes)
    {
        var hardMax = options.FileReadHardMaxBytes > 0
            ? options.FileReadHardMaxBytes
            : 1_048_576;

        var configuredDefault = options.FileReadDefaultMaxBytes > 0
            ? options.FileReadDefaultMaxBytes
            : 262_144;

        var effectiveDefault = Math.Clamp(configuredDefault, 1, hardMax);
        var requested = requestedMaxBytes > 0 ? requestedMaxBytes : effectiveDefault;

        return Math.Clamp(requested, 1, hardMax);
    }

    private static bool IsHidden(FileSystemInfo info)
    {
        if (info.Name.StartsWith(".", StringComparison.Ordinal))
        {
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            return (info.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden;
        }

        return false;
    }

    private static string ResolveContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".ts" => "text/plain",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream",
        };
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return value.Trim().Replace('/', '-').Replace('\\', '-');
    }
}
