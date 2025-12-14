# RvtToGltfExporter

# RVT ➜ glTF Converter (Revit 2026 + ASP.NET Core API)

This application converts **Autodesk Revit (.rvt)** files into **glTF (.gltf)** format using a two-part system composed of an **ASP.NET Core API** and a **Revit 2026 Add-in**. The API exposes a Swagger endpoint where users upload an RVT file, while the Revit Add-in performs the actual conversion inside Revit and returns the generated glTF file back to the API.

---

## Requirements

- Windows 10 / 11  
- Autodesk Revit 2026 (installed and licensed)  
- Visual Studio 2022 (recommended)  
- .NET SDK (for the API project)  
- Write access to `C:\RvtToGltf\Jobs` and `C:\temp`

---

## How It Works (High-Level Flow)

1. User uploads an `.rvt` file via Swagger (`POST /convert`)
2. API saves the file into a job folder
3. API launches Revit 2026
4. Revit automatically loads the custom add-in
5. The add-in starts a local HTTP listener at `http://localhost:5005/convert/`
6. API sends a JSON request to this endpoint with input/output paths
7. The add-in opens the RVT safely, exports geometry, and creates a `.gltf`
8. API waits for the output file and returns it as the response

---

## Step-by-Step Setup

### 1) Build the Revit Add-in

- Open the solution in Visual Studio  
- Select the **Rvt2GltfConverter** project  
- Build in **Release** mode  

Output will be generated under:

Rvt2GltfConverter\bin\Release\

---

### 2) Deploy the Add-in to Revit

Create the following folder:

C:\ProgramData\Autodesk\Revit\Addins\2026\Rvt2GltfConverter\


Copy **all** required files from the Release output into this folder. At minimum, it must contain:
- `Rvt2GltfConverter.dll`
- `Newtonsoft.Json.dll`
- `SharpGLTF.Core.dll`
- `SharpGLTF.Runtime.dll`
- `SharpGLTF.Toolkit.dll`
- (optional) `.pdb` / `.xml` files

Missing DLLs here are the most common cause of runtime errors.

---

### 3) Create the `.addin` Loader File

Create this file:

C:\ProgramData\Autodesk\Revit\Addins\2026\Rvt2GltfConverter.addin


With the following content (ensure the Assembly path is correct):

```xml
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>RvtHttpService</Name>
    <Assembly>C:\ProgramData\Autodesk\Revit\Addins\2026\Rvt2GltfConverter\Rvt2GltfConverter.dll</Assembly>
    <FullClassName>Rvt2GltfConverter.RvtHttpServiceApp</FullClassName>
    <AddInId>8A2E01CB-AAAA-BBBB-CCCC-DDDDDDDDDDDD</AddInId>
    <VendorId>MYCO</VendorId>
    <VendorDescription>RVT to glTF HTTP service</VendorDescription>
  </AddIn>
</RevitAddIns>




