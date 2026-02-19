namespace Hack13.Orchestrator;

public static class WorkflowPathResolver
{
    public static string? ResolveById(string workflowsDirectory, string workflowId)
    {
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
}
