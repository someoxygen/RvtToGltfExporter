using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 1024L * 1024L * 1024L;
});

// HttpClient
builder.Services.AddHttpClient();

builder.Services.AddAntiforgery();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "RVT to glTF Converter API",
        Version = "v1",
        Description = "Revit 2026'yi açýp kapatarak RVT dosyalarýný glTF'e dönüþtüren API."
    });
});

var app = builder.Build();

app.UseAntiforgery();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "RVT to glTF Converter API v1");
});

app.MapPost("/convert", async (
        IFormFile rvtFile,
        IConfiguration config,
        CancellationToken cancellationToken) =>
{
    if (rvtFile == null || rvtFile.Length == 0)
        return Results.BadRequest("RVT dosyasý boþ.");

    var jobsRoot = config["RvtToGltf:JobsRoot"] ?? @"C:\RvtToGltf\Jobs";
    Directory.CreateDirectory(jobsRoot);

    var jobId = Guid.NewGuid().ToString("N");
    var jobFolder = Path.Combine(jobsRoot, jobId);
    Directory.CreateDirectory(jobFolder);

    var inputRvtPath = Path.Combine(jobFolder, "input.rvt");
    var outputGltfPath = Path.Combine(jobFolder, "output.gltf");

    using (var fs = new FileStream(inputRvtPath, FileMode.Create, FileAccess.Write))
    {
        await rvtFile.CopyToAsync(fs, cancellationToken);
    }

    var revitExePath = config["Revit:ExePath"] ?? @"C:\Program Files\Autodesk\Revit 2026\Revit.exe";
    if (!File.Exists(revitExePath))
    {
        return Results.Problem($"Revit.exe yolu geçersiz: {revitExePath}");
    }

    var psi = new ProcessStartInfo
    {
        FileName = revitExePath,
        Arguments = "/nosplash",
        UseShellExecute = true
    };

    var revitProcess = Process.Start(psi);

    if (revitProcess == null)
    {
        return Results.Problem("Revit.exe baþlatýlamadý.");
    }

    var payload = new
    {
        inputPath = inputRvtPath,
        outputPath = outputGltfPath
    };

    HttpResponseMessage response = null;
    Exception lastError = null;

    using (var http = new HttpClient())
    {
        var start = DateTime.UtcNow;
        var timeoutConnect = TimeSpan.FromMinutes(2);

        while (DateTime.UtcNow - start < timeoutConnect)
        {
            try
            {
                response = await http.PostAsJsonAsync("http://localhost:5005/convert/", payload, cancellationToken);
                if (response.IsSuccessStatusCode)
                    break; // artýk istek gitti
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
            }

            await Task.Delay(2000, cancellationToken);
        }
    }

    if (response == null)
    {
        return Results.Problem(
            "Revit HTTP servisine baðlanýlamadý (localhost:5005). " +
            $"Muhtemelen addin yüklenmedi. Detay: {lastError?.Message}");
    }

    if (!response.IsSuccessStatusCode)
    {
        var txt = await response.Content.ReadAsStringAsync(cancellationToken);
        return Results.Problem(
            $"Revit addin isteði baþarýsýz. Status: {(int)response.StatusCode}, Body: {txt}");
    }

    var timeout = DateTime.UtcNow.AddMinutes(5);
    while (!File.Exists(outputGltfPath))
    {
        if (DateTime.UtcNow > timeout)
            return Results.Problem("GLTF oluþturma zaman aþýmýna uðradý.");

        await Task.Delay(1000, cancellationToken);
    }

    // --- GLTF dosyasýný oku ---
    var gltfBytes = await File.ReadAllBytesAsync(outputGltfPath, cancellationToken);
    var downloadName = Path.GetFileNameWithoutExtension(rvtFile.FileName) + ".gltf";

    try
    {
        if (!revitProcess.HasExited)
        {
            if (!revitProcess.WaitForExit(30000))
            {
                revitProcess.Kill();
            }
        }
    }
    catch
    {
    }

    return Results.File(
        fileContents: gltfBytes,
        contentType: "model/gltf+json",
        fileDownloadName: downloadName
    );
})
    .DisableAntiforgery()
    .Accepts<IFormFile>("multipart/form-data")
    .Produces<FileContentResult>(StatusCodes.Status200OK, "model/gltf+json")
    .Produces(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

app.Run();
