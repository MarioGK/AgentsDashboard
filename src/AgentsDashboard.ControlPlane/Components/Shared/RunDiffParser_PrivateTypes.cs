using System.Text;
using System.Text.RegularExpressions;

namespace AgentsDashboard.ControlPlane.Components.Shared;

public static partial class RunDiffParser
{
    private sealed class DiffFileAccumulator
    {
        private readonly StringBuilder _patch = new();
        private readonly StringBuilder _original = new();
        private readonly StringBuilder _modified = new();
        private readonly List<RunDiffHunk> _hunks = [];
        private string _hunkPreview = string.Empty;
        private int _hunkOldStart;
        private int _hunkOldCount;
        private int _hunkNewStart;
        private int _hunkNewCount;
        private string _hunkHeader = string.Empty;
        private int _hunkIndex;

        public string OldPath { get; set; } = string.Empty;
        public string NewPath { get; set; } = string.Empty;
        public int AddedLines { get; private set; }
        public int RemovedLines { get; private set; }

        public void AppendPatchLine(string line)
        {
            _patch.AppendLine(line);
        }

        public void SetPathsFromDiffHeader(string line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                OldPath = NormalizePath(parts[2]);
                NewPath = NormalizePath(parts[3]);
            }
        }

        public void StartHunk(string line)
        {
            CommitHunk();

            var match = HunkRegex.Match(line);
            if (!match.Success)
            {
                _hunkIndex++;
                _hunkHeader = line;
                _hunkOldStart = 1;
                _hunkOldCount = 0;
                _hunkNewStart = 1;
                _hunkNewCount = 0;
                _hunkPreview = string.Empty;
                return;
            }

            _hunkIndex++;
            _hunkHeader = line;
            _hunkOldStart = ParseInt(match, "oldStart", 1);
            _hunkOldCount = ParseInt(match, "oldCount", 1);
            _hunkNewStart = ParseInt(match, "newStart", 1);
            _hunkNewCount = ParseInt(match, "newCount", 1);
            _hunkPreview = match.Groups["header"].Value.Trim();
        }

        public void ProcessHunkLine(string line)
        {
            if (_hunkIndex == 0)
            {
                return;
            }

            if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                AddedLines++;
                _modified.AppendLine(line.Length > 1 ? line[1..] : string.Empty);
                CapturePreview(line);
                return;
            }

            if (line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
            {
                RemovedLines++;
                _original.AppendLine(line.Length > 1 ? line[1..] : string.Empty);
                CapturePreview(line);
                return;
            }

            if (line.StartsWith(' '))
            {
                var text = line.Length > 1 ? line[1..] : string.Empty;
                _original.AppendLine(text);
                _modified.AppendLine(text);
                return;
            }

            if (line.StartsWith("\\", StringComparison.Ordinal))
            {
                return;
            }

            _original.AppendLine(line);
            _modified.AppendLine(line);
            CapturePreview(line);
        }

        public RunDiffFileView ToView(int fallbackIndex)
        {
            CommitHunk();

            var displayPath = ResolveDisplayPath(fallbackIndex);
            var normalizedOldPath = OldPath.Length == 0 ? displayPath : OldPath;
            var normalizedNewPath = NewPath.Length == 0 ? displayPath : NewPath;

            return new RunDiffFileView(
                displayPath,
                normalizedOldPath,
                normalizedNewPath,
                AddedLines,
                RemovedLines,
                _hunks.ToList(),
                _patch.ToString().TrimEnd(),
                _original.ToString().TrimEnd(),
                _modified.ToString().TrimEnd());
        }

        private void CapturePreview(string line)
        {
            if (_hunkPreview.Length > 0)
            {
                return;
            }

            var text = line;
            if (text.Length > 0 && (text[0] == '+' || text[0] == '-' || text[0] == ' '))
            {
                text = text.Length > 1 ? text[1..] : string.Empty;
            }

            _hunkPreview = text.Trim();
        }

        private void CommitHunk()
        {
            if (_hunkIndex == 0)
            {
                return;
            }

            _hunks.Add(new RunDiffHunk(
                _hunkIndex,
                _hunkOldStart,
                _hunkOldCount,
                _hunkNewStart,
                _hunkNewCount,
                _hunkHeader,
                _hunkPreview));

            _hunkPreview = string.Empty;
            _hunkOldStart = 0;
            _hunkOldCount = 0;
            _hunkNewStart = 0;
            _hunkNewCount = 0;
            _hunkHeader = string.Empty;
        }

        private string ResolveDisplayPath(int fallbackIndex)
        {
            if (!string.IsNullOrWhiteSpace(NewPath) && !string.Equals(NewPath, "/dev/null", StringComparison.Ordinal))
            {
                return NewPath;
            }

            if (!string.IsNullOrWhiteSpace(OldPath) && !string.Equals(OldPath, "/dev/null", StringComparison.Ordinal))
            {
                return OldPath;
            }

            return $"diff-{fallbackIndex}";
        }

        private static int ParseInt(Match match, string groupName, int fallback)
        {
            var value = match.Groups[groupName].Value;
            return int.TryParse(value, out var parsed) && parsed > 0
                ? parsed
                : fallback;
        }
    }
}
