namespace Hack13.Orchestrator;

public static class WorkflowPathResolver
{
    public static string? ResolveById(string workflowsDirectory, string workflowId)
    {
        if (!IsValidWorkflowId(workflowId))
            return null;

        var direct = Path.Combine(workflowsDirectory, $"{workflowId}.json");
        if (File.Exists(direct))
            return direct;

        var candidates = Directory.Exists(workflowsDirectory)
            ? Directory.EnumerateFiles(workflowsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            : Enumerable.Empty<string>();

        return candidates.FirstOrDefault(path =>
            string.Equals(
                Path.GetFileNameWithoutExtension(path),
                workflowId,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsValidWorkflowId(string workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
            return false;

        if (workflowId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        if (workflowId.Contains(Path.DirectorySeparatorChar)
            || workflowId.Contains(Path.AltDirectorySeparatorChar))
            return false;

        if (workflowId.Contains("..", StringComparison.Ordinal))
            return false;

        return true;
    }
}
