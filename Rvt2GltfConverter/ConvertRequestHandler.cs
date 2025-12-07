using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Rvt2GltfConverter
{
    public class ConvertRequestHandler : IExternalEventHandler
    {
        public ConvertMessage Request;

        public void Execute(UIApplication app)
        {
            try
            {
                if (Request == null) return;

                var input = Request.InputPath;
                var output = Request.OutputPath;

                if (string.IsNullOrEmpty(input) || !File.Exists(input))
                    return;

                // 1) RVT dokümanını aç
                var doc = app.Application.OpenDocumentFile(input);

                try
                {
                    // 2) GLTF'e ÇEVİR (ARTIK GERÇEK EXPORT)
                    RevitGltfExporter.ExportToGltf(doc, output);
                }
                finally
                {
                    // 3) Dokümanı kapat
                    doc.Close(false);
                }

                // 4) Revit'i kapat (Exit komutunu post et)
                var exitCommandId =
                    RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);
                app.PostCommand(exitCommandId);
            }
            catch (Exception ex)
            {
                File.AppendAllText(
                    @"C:\temp\revit_http_addin.log",
                    DateTime.Now.ToString("u") + " HATA: " + ex + Environment.NewLine);
            }
        }

        public string GetName()
        {
            return "GLTF Convert Handler";
        }
    }
}
