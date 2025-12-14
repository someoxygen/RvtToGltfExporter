using RvtToGltfExporter.Interfaces;
using System.Diagnostics;

namespace RvtToGltfExporter.Services
{
    public class RevitCoreConsoleRunner : IRevitCoreConsoleRunner
    {
        private readonly IConfiguration _config;
        private readonly ILogger<RevitCoreConsoleRunner> _logger;

        public RevitCoreConsoleRunner(IConfiguration config, ILogger<RevitCoreConsoleRunner> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<string> ConvertAsync(string inputRvtPath, string outputGltfPath, CancellationToken ct)
        {
            var exePath = _config["RevitCoreConsole:ExePath"];
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                throw new InvalidOperationException("RevitCoreConsole.exe yolu geçersiz.");

            // argüman: /rvt2gltf "input.rvt" "output.gltf"
            var args = $"/rvt2gltf \"{inputRvtPath}\" \"{outputGltfPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _logger.LogInformation("Starting RevitCoreConsole: {FileName} {Args}", exePath, args);

            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            var timeout = TimeSpan.FromMinutes(10);

            // ✅ DOĞRU WAIT/TIMEOUT KONTROLÜ
            var waitTask = process.WaitForExitAsync(ct);
            var finished = await Task.WhenAny(waitTask, Task.Delay(timeout, ct));

            if (finished != waitTask)
            {
                try { process.Kill(true); } catch { }
                throw new TimeoutException("RevitCoreConsole zaman aşımına uğradı.");
            }

            await waitTask;

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            _logger.LogInformation("RevitCoreConsole exit code: {ExitCode}", process.ExitCode);
            if (!string.IsNullOrWhiteSpace(stdout))
                _logger.LogDebug("RevitCoreConsole stdout: {Stdout}", stdout);
            if (!string.IsNullOrWhiteSpace(stderr))
                _logger.LogWarning("RevitCoreConsole stderr: {Stderr}", stderr);

            if (process.ExitCode != 0)
                throw new Exception($"RevitCoreConsole hata kodu: {process.ExitCode}");

            if (!File.Exists(outputGltfPath))
                throw new FileNotFoundException("GLTF çıkışı bulunamadı.", outputGltfPath);

            return outputGltfPath;
        }
    }
}
