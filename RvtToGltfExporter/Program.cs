using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 1024L * 1024L * 1024L;
});

builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 1024L * 1024L * 1024L;
});

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

builder.Services.AddCors(opt =>
{
    opt.AddPolicy("ui", p =>
        p.WithOrigins("http://localhost:4200")
         .AllowAnyHeader()
         .AllowAnyMethod()
         .WithExposedHeaders("Content-Disposition"));
});

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    var feature = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (feature != null && !feature.IsReadOnly)
        feature.MaxRequestBodySize = 1024L * 1024L * 1024L;

    await next();
});

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "RVT to glTF Converter API v1");
});

app.UseCors("ui");

var jobsRoot = app.Configuration["RvtToGltf:JobsRoot"] ?? @"C:\RvtToGltf\Jobs";
Directory.CreateDirectory(jobsRoot);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(jobsRoot),
    RequestPath = "/jobs",
    ServeUnknownFileTypes = true
});

static string GetBaseUrl(HttpRequest req) => $"{req.Scheme}://{req.Host}";

app.MapPost("/convert", async (
        HttpRequest request,
        IFormFile rvtFile,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken) =>
{
    if (rvtFile == null || rvtFile.Length == 0)
        return Results.BadRequest(new { title = "Invalid file", detail = "RVT dosyasý boþ." });

    var jobsRootLocal = config["RvtToGltf:JobsRoot"] ?? @"C:\RvtToGltf\Jobs";
    Directory.CreateDirectory(jobsRootLocal);

    var jobId = Guid.NewGuid().ToString("N");
    var jobFolder = Path.Combine(jobsRootLocal, jobId);
    Directory.CreateDirectory(jobFolder);

    var inputRvtPath = Path.Combine(jobFolder, "input.rvt");
    var outputGltfPath = Path.Combine(jobFolder, "output.gltf");
    var outputBinPath = Path.Combine(jobFolder, "output.bin");
    var outputZipPath = Path.Combine(jobFolder, "output.zip");
    var errorPath = outputGltfPath + ".error.txt";

    await using (var fs = new FileStream(inputRvtPath, FileMode.Create, FileAccess.Write, FileShare.Read))
    {
        await rvtFile.CopyToAsync(fs, cancellationToken);
    }

    var revitExePath = config["Revit:ExePath"] ?? @"C:\Program Files\Autodesk\Revit 2026\Revit.exe";
    if (!File.Exists(revitExePath))
        return Results.Problem($"Revit.exe yolu geçersiz: {revitExePath}");

    // Revit baþlat
    var psi = new ProcessStartInfo
    {
        FileName = revitExePath,
        Arguments = "/nosplash",
        UseShellExecute = true
    };

    var revitProcess = Process.Start(psi);
    if (revitProcess == null)
        return Results.Problem("Revit.exe baþlatýlamadý.");

    var http = httpClientFactory.CreateClient();

    var readyUntil = DateTime.UtcNow.AddMinutes(2);
    bool ready = false;

    while (DateTime.UtcNow < readyUntil)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var ping = new HttpRequestMessage(HttpMethod.Get, "http://localhost:5005/convert/");
            var pingRes = await http.SendAsync(ping, cancellationToken);
            if (pingRes.IsSuccessStatusCode)
            {
                ready = true;
                break;
            }
        }
        catch { }

        await Task.Delay(1000, cancellationToken);
    }

    if (!ready)
        return Results.Problem("Revit addin HTTP servisi (localhost:5005) hazýr olmadý. Addin yüklenmiyor olabilir.");

    var payload = new { inputPath = inputRvtPath, outputPath = outputGltfPath };

    HttpResponseMessage? response = null;
    Exception? lastError = null;

    var start = DateTime.UtcNow;
    var timeoutConnect = TimeSpan.FromMinutes(2);

    while (DateTime.UtcNow - start < timeoutConnect)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            response = await http.PostAsJsonAsync("http://localhost:5005/convert/", payload, cancellationToken);
            if (response.IsSuccessStatusCode)
                break;
        }
        catch (HttpRequestException ex)
        {
            lastError = ex;
        }

        await Task.Delay(2000, cancellationToken);
    }

    if (response == null)
        return Results.Problem("Revit HTTP servisine baðlanýlamadý (localhost:5005). Detay: " + lastError?.Message);

    if (!response.IsSuccessStatusCode)
    {
        var txt = await response.Content.ReadAsStringAsync(cancellationToken);
        return Results.Problem($"Revit addin isteði baþarýsýz. Status: {(int)response.StatusCode}, Body: {txt}");
    }

    var deadline = DateTime.UtcNow.AddMinutes(10);

    while (!File.Exists(outputGltfPath))
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(errorPath))
        {
            var err = await File.ReadAllTextAsync(errorPath, cancellationToken);
            return Results.Problem("Revit addin conversion hatasý:\n" + err);
        }

        if (DateTime.UtcNow > deadline)
            return Results.Problem("GLTF oluþturma zaman aþýmýna uðradý.");

        await Task.Delay(1000, cancellationToken);
    }

    var baseUrl = GetBaseUrl(request);
    var gltfUrl = $"{baseUrl}/jobs/{jobId}/output.gltf";
    string? binUrl = File.Exists(outputBinPath) ? $"{baseUrl}/jobs/{jobId}/output.bin" : null;
    string? zipUrl = File.Exists(outputZipPath) ? $"{baseUrl}/jobs/{jobId}/output.zip" : null;

    _ = Task.Run(() =>
    {
        try
        {
            if (!revitProcess.HasExited)
            {
                if (!revitProcess.WaitForExit(5000))
                    revitProcess.Kill();
            }
        }
        catch { }

        // ZIP yoksa üretmeyi burada da yapabilirsin
        try
        {
            if (!File.Exists(outputZipPath))
            {
                using var zip = ZipFile.Open(outputZipPath, ZipArchiveMode.Create);
                zip.CreateEntryFromFile(outputGltfPath, "output.gltf");
                if (File.Exists(outputBinPath))
                    zip.CreateEntryFromFile(outputBinPath, "output.bin");
            }
        }
        catch { }
    });

    return Results.Ok(new
    {
        jobId,
        gltfUrl,
        binUrl,
        zipUrl
    });
})
.DisableAntiforgery()
.Accepts<IFormFile>("multipart/form-data")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status500InternalServerError);

app.Run();
