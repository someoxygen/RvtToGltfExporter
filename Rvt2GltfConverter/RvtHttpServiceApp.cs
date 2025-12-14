using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Rvt2GltfConverter
{
    public class RvtHttpServiceApp : IExternalApplication
    {
        private static HttpListener _listener;
        internal static ExternalEvent ConvertEvent;
        internal static ConvertRequestHandler Handler;

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                Directory.CreateDirectory(@"C:\temp");
                File.AppendAllText(@"C:\temp\addin_startup.log",
                    DateTime.Now.ToString("u") + " OnStartup\n");
                File.AppendAllText(@"C:\temp\addin_startup.log",
                    DateTime.Now.ToString("u") + " Addin Location: " + Assembly.GetExecutingAssembly().Location + "\n");

                // Revit içerisinde halihazırda yüklü STJ var mı? logla
                var stj = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "System.Text.Json", StringComparison.OrdinalIgnoreCase));
                if (stj != null)
                {
                    File.AppendAllText(@"C:\temp\addin_startup.log",
                        DateTime.Now.ToString("u") + " Already loaded: System.Text.Json " + stj.GetName().Version + "\n");
                }
            }
            catch { }

            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromAddinFolderSafe;

            Handler = new ConvertRequestHandler();
            ConvertEvent = ExternalEvent.Create(Handler);

            Task.Run(StartHttpServer);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            try { if (_listener != null && _listener.IsListening) _listener.Stop(); } catch { }
            try { AppDomain.CurrentDomain.AssemblyResolve -= ResolveFromAddinFolderSafe; } catch { }
            return Result.Succeeded;
        }

        /// <summary>
        /// Revit içinde aynı assembly zaten yüklü olabiliyor.
        /// Bu yüzden özellikle System.Text.Json gibi kritik dll'lerde önce mevcut yüklüyü döndürüp,
        /// çakışma yüzünden LoadFrom patlarsa fallback yapıyoruz.
        /// </summary>
        private static Assembly ResolveFromAddinFolderSafe(object sender, ResolveEventArgs args)
        {
            string requestedSimpleName = null;

            try
            {
                var req = new AssemblyName(args.Name);
                requestedSimpleName = req.Name;

                var addinDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                // 1) System.Text.Json / Encodings.Web için önce Revit'in zaten yüklediğini kullan (çakışmayı bitirir)
                if (string.Equals(requestedSimpleName, "System.Text.Json", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(requestedSimpleName, "System.Text.Encodings.Web", StringComparison.OrdinalIgnoreCase))
                {
                    var already = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, requestedSimpleName, StringComparison.OrdinalIgnoreCase));

                    if (already != null)
                    {
                        TryLog($"RESOLVE (use already-loaded) {requestedSimpleName} {already.GetName().Version}");
                        return already;
                    }
                }

                // 2) Normal dll'ler için: addin klasöründen yükle
                var candidatePath = Path.Combine(addinDir, requestedSimpleName + ".dll");
                if (File.Exists(candidatePath))
                {
                    try
                    {
                        var loaded = Assembly.LoadFrom(candidatePath);
                        TryLog($"RESOLVE (LoadFrom) {requestedSimpleName} -> {candidatePath}");
                        return loaded;
                    }
                    catch (Exception loadEx)
                    {
                        // 3) Eğer LoadFrom çakışma yüzünden patladıysa ve bu System.Text.Json ise fallback
                        TryLog($"RESOLVE (LoadFrom FAIL) {requestedSimpleName} -> {candidatePath} | {loadEx.GetType().Name}: {loadEx.Message}");

                        if (string.Equals(requestedSimpleName, "System.Text.Json", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(requestedSimpleName, "System.Text.Encodings.Web", StringComparison.OrdinalIgnoreCase))
                        {
                            var already2 = AppDomain.CurrentDomain.GetAssemblies()
                                .FirstOrDefault(a => string.Equals(a.GetName().Name, requestedSimpleName, StringComparison.OrdinalIgnoreCase));

                            if (already2 != null)
                            {
                                TryLog($"RESOLVE (fallback already-loaded) {requestedSimpleName} {already2.GetName().Version}");
                                return already2;
                            }
                        }
                    }
                }
                else
                {
                    // Autodesk.* resource dll'leri vs burada görünür; bunları yüklemiyoruz.
                    // İstersen log spam olmasın diye Autodesk.* için hiç yazma da diyebiliriz.
                    TryLog($"RESOLVE FAIL {requestedSimpleName}.dll (not found in {addinDir})");
                }
            }
            catch (Exception ex)
            {
                TryLog($"RESOLVE EXCEPTION {requestedSimpleName} | {ex.GetType().Name}: {ex.Message}");
            }

            return null;
        }

        private static void TryLog(string text)
        {
            try
            {
                File.AppendAllText(@"C:\temp\addin_startup.log",
                    DateTime.Now.ToString("u") + " " + text + "\n");
            }
            catch { }
        }

        private static async Task StartHttpServer()
        {
            try
            {
                Directory.CreateDirectory(@"C:\temp");
                File.AppendAllText(@"C:\temp\addin_startup.log",
                    DateTime.Now.ToString("u") + " StartHttpServer\n");

                _listener = new HttpListener();
                _listener.Prefixes.Add("http://localhost:5005/convert/");
                _listener.Start();

                File.AppendAllText(@"C:\temp\addin_startup.log",
                    DateTime.Now.ToString("u") + " Listener START 5005\n");

                while (true)
                {
                    var ctx = await _listener.GetContextAsync().ConfigureAwait(false);

                    if (ctx.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
                    {
                        var ok = Encoding.UTF8.GetBytes("{\"status\":\"ready\"}");
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentType = "application/json";
                        ctx.Response.OutputStream.Write(ok, 0, ok.Length);
                        ctx.Response.OutputStream.Close();
                        continue;
                    }

                    ConvertMessage req = null;
                    using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                    {
                        var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                        req = JsonConvert.DeserializeObject<ConvertMessage>(body);
                    }

                    if (req != null && Handler != null && ConvertEvent != null)
                    {
                        Handler.Request = req;
                        ConvertEvent.Raise();
                    }

                    var buf = Encoding.UTF8.GetBytes("{\"status\":\"accepted\"}");
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                    ctx.Response.OutputStream.Close();
                }
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(@"C:\temp");
                    File.AppendAllText(@"C:\temp\revit_http_listener_start.log",
                        DateTime.Now.ToString("u") + " LISTENER FAIL: " + ex + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}
