namespace RvtToGltfExporter.Interfaces
{
    public interface IRevitCoreConsoleRunner
    {
        Task<string> ConvertAsync(string inputRvtPath, string outputGltfPath, CancellationToken ct);
    }
}
