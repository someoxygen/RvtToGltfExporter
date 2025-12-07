using System;
using System.IO;
using System.Numerics;
using Autodesk.Revit.DB;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace Rvt2GltfConverter
{
    public static class RevitGltfExporter
    {
        public static void ExportToGltf(Document doc, string outputPath)
        {
            // 1) Sahne oluştur
            var scene = new SceneBuilder();

            // 2) Basit bir materyal (renk vs. şimdilik boş, default kalsın)
            var defaultMaterial = new MaterialBuilder("DefaultMaterial");

            // 3) Revit geometrisi için seçenekler
            var opts = new Options();
            opts.DetailLevel = ViewDetailLevel.Fine;
            opts.IncludeNonVisibleObjects = false;

            // 4) Tüm elementleri dolaş
            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();

            foreach (Element elem in collector)
            {
                try
                {
                    var geom = elem.get_Geometry(opts);
                    if (geom == null) continue;

                    var meshBuilder = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty>("Mesh_" + elem.Id.ToString());

                    var prim = meshBuilder.UsePrimitive(defaultMaterial);

                    // Revit koordinat sistemi Z-up, glTF Y-up.
                    foreach (GeometryObject gobj in geom)
                    {
                        var ginst = gobj as GeometryInstance;
                        if (ginst != null)
                        {
                            var instGeom = ginst.GetInstanceGeometry();
                            AddGeometry(instGeom, prim);
                        }
                        else
                        {
                            AddGeometry(geom, prim);
                            break;
                        }
                    }

                    if (meshBuilder.Primitives.Count > 0)
                    {
                        // Sahneye rigid mesh olarak ekle (transform yok varsayıyoruz)
                        scene.AddRigidMesh(meshBuilder, Matrix4x4.Identity);
                    }
                }
                catch
                {
                    // Bazı elementler geometri vermez, sessiz geçiyoruz
                }
            }

            // 5) glTF modeli oluştur ve kaydet
            var model = scene.ToGltf2();

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            model.SaveGLTF(outputPath);
        }

        private static void AddGeometry(
            GeometryElement geom,
            PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty> prim)
        {
            foreach (GeometryObject gobj in geom)
            {
                var solid = gobj as Solid;
                if (solid != null && solid.Faces.Size > 0 && solid.Edges.Size > 0)
                {
                    AddSolid(solid, prim);
                }

                var mesh = gobj as Mesh;
                if (mesh != null && mesh.NumTriangles > 0)
                {
                    AddMesh(mesh, prim);
                }
            }
        }

        private static void AddSolid(
            Solid solid,
            PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty> prim)
        {
            foreach (Face face in solid.Faces)
            {
                var mesh = face.Triangulate();
                AddMesh(mesh, prim);
            }
        }

        private static void AddMesh(
            Mesh mesh,
            PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty> prim)
        {
            for (int i = 0; i < mesh.NumTriangles; i++)
            {
                var tri = mesh.get_Triangle(i);

                var v1 = ToVertex(tri.get_Vertex(0));
                var v2 = ToVertex(tri.get_Vertex(1));
                var v3 = ToVertex(tri.get_Vertex(2));

                prim.AddTriangle(v1, v2, v3);
            }
        }

        private static VertexPositionNormal ToVertex(XYZ p)
        {
            // Revit (X,Y,Z) → glTF (X,Z,Y)
            var pos = new Vector3(
                (float)p.X,
                (float)p.Z,
                (float)p.Y);

            // Normal yok, şimdilik (0,1,0) veriyoruz
            var nrm = Vector3.UnitY;

            return new VertexPositionNormal(pos, nrm);
        }
    }
}
