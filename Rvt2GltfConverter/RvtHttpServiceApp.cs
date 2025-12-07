using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
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
            // ExternalEvent + handler
            Handler = new ConvertRequestHandler();
            ConvertEvent = ExternalEvent.Create(Handler);

            // HTTP server'ı arka planda başlat
            Task.Run(new Func<Task>(StartHttpServer));

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
            }
            return Result.Succeeded;
        }

        private static async Task StartHttpServer()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://localhost:5005/convert/");
            _listener.Start();

            while (true)
            {
                var ctx = await _listener.GetContextAsync().ConfigureAwait(false);

                ConvertMessage req = null;

                // C# 7.3 uyumlu using
                using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                {
                    var body = await reader.ReadToEndAsync().ConfigureAwait(false);

                    // JSON: { "inputPath": "...", "outputPath": "..." }
                    req = JsonConvert.DeserializeObject<ConvertMessage>(body);
                }

                if (req != null && Handler != null && ConvertEvent != null)
                {
                    Handler.Request = req;
                    ConvertEvent.Raise();
                }

                // Basit response
                var buf = Encoding.UTF8.GetBytes("{\"status\":\"accepted\"}");
                ctx.Response.ContentType = "application/json";
                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                ctx.Response.OutputStream.Close();
            }
        }
    }
}
