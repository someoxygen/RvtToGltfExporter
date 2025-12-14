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
                    throw new FileNotFoundException("Input RVT bulunamadı.", input);

                Document doc = null;

                try
                {
                    // ✅ Dialog/Worksharing sorunlarını azaltan güvenli açılış
                    var openOpt = new OpenOptions
                    {
                        Audit = true
                    };

                    // Central / Workshared dosyalarda otomasyon için en güvenlisi:
                    openOpt.DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets;

                    // Worksetleri kapalı aç (performans + popup azaltır)
                    var wsConfig = new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets);
                    openOpt.SetOpenWorksetsConfiguration(wsConfig);

                    var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(input);
                    doc = app.Application.OpenDocumentFile(modelPath, openOpt);

                    // ✅ Export
                    RevitGltfExporter.ExportToGltf(doc, output);
                }
                finally
                {
                    if (doc != null)
                    {
                        try { doc.Close(false); } catch { }
                    }
                }

                // ✅ Revit'i kapat
                var exitCommandId =
                    RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);
                app.PostCommand(exitCommandId);
            }
            catch (Exception ex)
            {
                // ✅ API tarafı da anlayabilsin diye error dosyası üret
                try
                {
                    var outp = Request?.OutputPath;
                    if (!string.IsNullOrEmpty(outp))
                    {
                        File.WriteAllText(outp + ".error.txt", ex.ToString());
                    }
                }
                catch { }

                // Mevcut log
                try
                {
                    Directory.CreateDirectory(@"C:\temp");
                    File.AppendAllText(
                        @"C:\temp\revit_http_addin.log",
                        DateTime.Now.ToString("u") + " HATA: " + ex + Environment.NewLine);
                }
                catch { }
            }
        }

        public string GetName()
        {
            return "GLTF Convert Handler";
        }
    }
}
