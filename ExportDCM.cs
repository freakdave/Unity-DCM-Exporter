/* 
 * The MIT License (MIT)
 * Copyright © 2026 David Reichelt, Luke Benstead, Ross Kilgariff
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 *
 *
 * Based on the DCM specification from https://gitlab.com/simulant/community/dcm
 * 
 * This Unity Editor extension provides functionality to export 3D models 
 * from Unity into the DCM (Dreamcast Mesh) format, which is used for Sega Dreamcast game development.
 * Tested with Unity 6000.0.39f1
 * 
 * - Exports mesh data with support for positions, UVs, colors, and normals
 * - Applies necessary mesh transformations for compatibility with the Dreamcast renderer
 * - Supports proper coordinate system conversion between Unity and Dreamcast
 * 
 * TODO:
 * - Handle materials and textures for the Dreamcast platform
 * - Refine options for texture format selection and layer filtering
 */

#if UNITY_EDITOR

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;


namespace DCM
{
    enum DataFlags
    {
        DATA_FLAG_INTERNAL = 0x0,
        DATA_FLAG_EXTERNAL_LINK = 0x1,
    };
    

    enum PositionFormat
    {
        POSITION_FORMAT_NONE = 0,
        POSITION_FORMAT_2F,
        POSITION_FORMAT_3F,
        POSITION_FORMAT_4F,
    };


    enum RotationFormat
    {
        ROTATION_FORMAT_NONE = 0,
        ROTATION_FORMAT_QUAT_4F,
    };


    enum ScaleFormat
    {
        SCALE_FORMAT_NONE = 0,
        SCALE_FORMAT_3F,
    };


    enum TexCoordFormat
    {
        TEX_COORD_FORMAT_NONE = 0,
        TEX_COORD_FORMAT_2F,
        TEX_COORD_FORMAT_2US,
    };


    enum ColorFormat
    {
        COLOR_FORMAT_NONE = 0,
        COLOR_FORMAT_4UB,
        COLOR_FORMAT_3F,
        COLOR_FORMAT_4F,
    };


    enum NormalFormat
    {
        NORMAL_FORMAT_NONE = 0,
        NORMAL_FORMAT_3F,
    };


    enum BoneWeightsFormat
    {
        BONE_WEIGHTS_FORMAT_NONE = 0,
        BONE_WEIGHTS_FORMAT_3_UI_F,
    };


    enum KeyframeFormat
    {
        KEYFRAME_FORMAT_NONE = 0,
        KEYFRAME_FORMAT_2F = 1
    };


    enum SubMeshArrangement
    {
        SUB_MESH_ARRANGEMENT_NONE = 0,
        SUB_MESH_ARRANGEMENT_TRIANGLE_STRIP,
        SUB_MESH_ARRANGEMENT_TRIANGLES,
    };


    enum SubMeshType
    {
        SUB_MESH_TYPE_NONE = 0,
        SUB_MESH_TYPE_RANGED,
        SUB_MESH_TYPE_INDEXED
    };


    enum ChannelType
    {
        CHANNEL_TYPE_NONE = 0,
        CHANNEL_TYPE_POSITION_X = 1,
        CHANNEL_TYPE_POSITION_Y = 2,
        CHANNEL_TYPE_POSITION_Z = 3,
        CHANNEL_TYPE_ROTATION_QUAT_X = 4,
        CHANNEL_TYPE_ROTATION_QUAT_Y = 5,
        CHANNEL_TYPE_ROTATION_QUAT_Z = 6,
        CHANNEL_TYPE_ROTATION_QUAT_W = 7,
        CHANNEL_TYPE_SCALE_X = 8,
        CHANNEL_TYPE_SCALE_Y = 9,
        CHANNEL_TYPE_SCALE_Z = 10,

        CHANNEL_TYPE_USER_START, /* Types from here to CHANNEL_TYPE_LAST are user-defined */
CHANNEL_TYPE_LAST = 255
    };


    enum ChannelFlags
    {
        CHANNEL_FLAG_INTERP_CONSTANT = 0x01,
        CHANNEL_FLAG_INTERP_LINEAR = 0x02,

        /* Remaining flags are reserved */
        CHANNEL_FLAG_LAST = 0xFF
    };


    [System.Serializable]
    public class FileHeader
    {
        public byte[] id = new byte[3];  // 'D', 'C', 'M'
        public byte version;
        public byte material_count;
        public byte mesh_count;
        public byte armature_count;
        public byte animation_count;
        public byte pos_format;
        public byte tex0_format;
        public byte tex1_format;
        public byte color_format;
        public byte offset_colour_format;
        public byte normal_format;
        public byte index_size;
        public byte bone_weights_format;
    }


    [System.Serializable]
    public class DataHeader
    {
        public byte flags; /* DataFlags affecting this data */
        public byte local_id; /* Local ID for this data */

        /**
         * Path for this data. Can be either internal ("my_data_item") or external ("other_file.dcmesh@my_data_item").
         * In cases where nested data should be accessed such as a bone within an armature, a
         * forward slash separates parent from child data ("other_file.dcmesh@my_data_item/my_data_part").
         * If DATA_FLAG_EXTERNAL_LINK is set, the path will be external - otherwise, it's internal.
         * Path refers to the data item within a dcmesh and is *not* a filesystem path.
         */
        public char[] path = new char[128];
    };


    [System.Serializable]
    class DCMMaterial
    {
        public char[] name = new char[32];
        public float[] ambient = { 0, 0, 0, 1 };
        public float[] diffuse = { 0, 0, 0, 1 };  /* Diffuse in RGBA order */
        public float[] specular = { 0, 0, 0, 1 };
        public float[] emission = { 0, 0, 0, 1 };
        public float[] shininess;
        public char[] diffuse_map = new char[32];  /* Filename of the diffuse map, if byte zero is \0 then there is no diffuse map */
        public char[] light_map = new char[32]; /* Filename of the light map, if byte zero is \0 then there is no light map */
        public char[] normal_map = new char[32]; /* Filename of the normal map, if byte zero is \0 then there is no normal map */
        public char[] specular_map = new char[32]; /* Filename of the specular map, if byte zero is \0 then there is no specular map */
    };


    [System.Serializable]
    class MeshHeader
    {
        public char[] name = new char[32];
        public byte submesh_count; /* Number of submeshes that follow the vertex data */
        public byte[] reserved = new byte[3];  /* Potentially bone count etc. */
        public uint vertex_count; /* Number of vertices that follow this header */
        public uint first_submesh_offset; /* Offset from the start of the file to the first submesh */
        public uint next_mesh_offset; /* Offset from the start of the file to the next mesh */
    };


    [System.Serializable]
    class SubMeshHeader
    {
        public byte material_id; /* Index to the materials list */
        public byte arrangement; /* Strips or triangles */
        public byte type; /* Whether vertex ranges, or indexes follow this struct */
        public ushort num_ranges_or_indices; /* Number of submesh vertex ranges or indices that follow */
        public uint next_submesh_offset; /* Offset frmo the start of the file to the next submesh */
    };


    [System.Serializable]
    class SubMeshVertexRange
    {
        public uint start;  /* Index in the mesh's vertex list to start from */
        public uint count;  /* Number of vertices to render from the start of the list */
    };


    public class ExportDCM : ScriptableWizard
    {
        public static readonly byte DCM_CURRENT_VERSION = 1;

        [SerializeField]
        private string _version = "1.0";

        public enum ExportTextureFormat
        {
            JPG,
            PNG
        }

        public enum DialogSeverity
        {
            Information,
            Warning,
            Error
        }

        public enum ExportScope
        {
            ActiveGameObjectOnly,      // Export only the active selected GameObject
            // SelectedGameObjectsOnly,   // Export only the selected GameObjects without their children
            // SelectedGameObjectsWithChildren // Export selected GameObjects and their children
        }

        [SerializeField]
        private ExportScope _exportScope = ExportScope.ActiveGameObjectOnly;

        [SerializeField]
        private ExportTextureFormat _exportTextureFormat = ExportTextureFormat.PNG;

        [SerializeField]
        private LayerMask _layerMask = 1 << 6;

        [SerializeField]
        private bool _treatTexturesAsDTEX = true;

        [SerializeField]
        private bool _forceTexturesReadWrite = true;


        [MenuItem("File/Export/DCM (Dreamcast Mesh)")]
        static void CreateWizard()
        {
            ScriptableWizard.DisplayWizard("Export DCM (Dreamcast Mesh)", typeof(ExportDCM), "Export DCM");
        }


        private void OnWizardCreate()
        {
            string prevFolderPath = EditorPrefs.GetString("DCM_prevFolderPath", "");
            string prevFileName = EditorPrefs.GetString("DCM_prevFileName", "mesh.dcm");

            /* Make sure we do not allow exporting when the application is playing */
            if (Application.isPlaying == true)
            {
                DisplayDialogOK(DialogSeverity.Error, "Exporting is not allowed when the application is playing. Please stop the application and try again.");
                return;
            }

            /* We also need to make sure that the textures are actually readable. This can be enforced for all affected textures */
            if (_forceTexturesReadWrite == true)
            {
                if (DisplayDialogOKCancel(DialogSeverity.Warning, "Unity will set all affected textures to Read/Write (Advanced). You can undo this manually for each texture after exporting. Proceed?") == false)
                {
                    return;
                }
            }

            /* Choose a location where we want to export our DCM file */
            string dcmSaveFile = EditorUtility.SaveFilePanel("Export DCM", prevFolderPath, prevFileName, "dcm");

            if (dcmSaveFile.Length > 0)
            {
                FileInfo fileInfo = new FileInfo(dcmSaveFile);
                EditorPrefs.SetString("DCM_prevFolderPath", fileInfo.Directory.FullName);
                EditorPrefs.SetString("DCM_prevFileName", fileInfo.Name);

                // Get appropriate GameObjects based on export scope
                List<GameObject> gameObjectsToExport = GetGameObjectsForExport();

                if (gameObjectsToExport.Count == 0)
                {
                    DisplayDialogOK(DialogSeverity.Warning, "No GameObjects found to export with current settings.");
                    return;
                }

                OnDCMExport(dcmSaveFile, gameObjectsToExport);
            }
        }


        private List<GameObject> GetGameObjectsForExport()
        {
            List<GameObject> result = new List<GameObject>();

            switch (_exportScope)
            {
                case ExportScope.ActiveGameObjectOnly:
                    if (Selection.activeGameObject != null)
                    {
                        result.Add(Selection.activeGameObject);
                    }
                    break;

#if NOT_IMPLEMENTED
                case ExportScope.SelectedGameObjectsOnly:
                    if (Selection.gameObjects != null && Selection.gameObjects.Length > 0)
                    {
                        result.AddRange(Selection.gameObjects);
                    }
                    break;

                case ExportScope.SelectedGameObjectsWithChildren:
                    if (Selection.gameObjects != null && Selection.gameObjects.Length > 0)
                    {
                        foreach (GameObject go in Selection.gameObjects)
                        {
                            result.Add(go);

                            // Add all children with MeshFilter component
                            MeshFilter[] childFilters = go.GetComponentsInChildren<MeshFilter>(true);
                            foreach (MeshFilter filter in childFilters)
                            {
                                if (!result.Contains(filter.gameObject))
                                {
                                    result.Add(filter.gameObject);
                                }
                            }

                            // Also add SkinnedMeshRenderers
                            SkinnedMeshRenderer[] skinRenderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                            foreach (SkinnedMeshRenderer renderer in skinRenderers)
                            {
                                if (!result.Contains(renderer.gameObject))
                                {
                                    result.Add(renderer.gameObject);
                                }
                            }
                        }
                    }
                    break;
#endif
            }

            // Filter out GameObjects without mesh components
            return result.Where(go => go.GetComponent<MeshFilter>()?.sharedMesh != null ||
                                      go.GetComponent<SkinnedMeshRenderer>()?.sharedMesh != null).ToList();
        }


        private void OnWizardUpdate()
        {
            helpString = "DCM (Dreamcast Mesh) exporter version: " + _version;

            // Add some more helpful information based on export scope
            switch (_exportScope)
            {
                case ExportScope.ActiveGameObjectOnly:
                    helpString += "\nWill export only the active selected GameObject.";
                    break;
#if NOT_IMPLEMENTED
                case ExportScope.SelectedGameObjectsOnly:
                    helpString += "\nWill export all selected GameObjects (without their children).";
                    break;

                case ExportScope.SelectedGameObjectsWithChildren:
                    helpString += "\nWill export all selected GameObjects and their children.";
                    break;
#endif
            }
        }


        /// <summary>
        /// Recursively gathers all unique materials from the provided GameObject and its children.
        /// </summary>
        private List<Material> GetUniqueMaterials(List<GameObject> gameObjects)
        {
            // Use a HashSet to avoid duplicates.
            HashSet<Material> uniqueMaterials = new HashSet<Material>();

            foreach (GameObject go in gameObjects)
            {
                Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
                foreach (Renderer rend in renderers)
                {
                    foreach (Material mat in rend.sharedMaterials)
                    {
                        if (mat != null)
                        {
                            uniqueMaterials.Add(mat);
                        }
                    }
                }
            }

            return uniqueMaterials.ToList();
        }


        /// <summary>
        /// Writes a fixed-length string to the writer, padding with 0's if needed.
        /// </summary>
        private void WriteFixedString(BinaryWriter writer, string str, int fixedLength)
        {
            byte[] bytes = new byte[fixedLength];
            byte[] strBytes = System.Text.Encoding.ASCII.GetBytes(str);
            int count = Mathf.Min(strBytes.Length, fixedLength);
            System.Array.Copy(strBytes, bytes, count);
            writer.Write(bytes);
        }


        /// <summary>
        /// Writes material data in the expected binary format.
        /// Writes:
        ///   - data header (flags, local ID),
        ///   - a fixed-length texture name (32 bytes),
        ///   - ambient, diffuse, specular, emission colors (each 4 floats),
        ///   - a shininess float,
        ///   - and 4 fixed-length strings (32 bytes each) for texture maps.
        /// </summary>
        private void WriteMaterialData(BinaryWriter writer, Material unityMaterial)
        {
            // Check if a valid material is provided; if not, warn and skip.
            if (unityMaterial == null)
            {
                Debug.LogWarning("No material provided for export; skipping material data.");
                return;
            }

            // Write Data Header for material:
            // Flags: (for example, DATA_FLAG_EXTERNAL_LINK; using 1 as in the Python version)
            writer.Write((byte)1);

            // Local ID for this material (using 1, but you could track unique IDs if needed)
            writer.Write((byte)1);

            // Retrieve texture name from the material's main texture (or use default "material")
            string textureName = (unityMaterial.mainTexture != null) ? unityMaterial.mainTexture.name : "material";

            if (_treatTexturesAsDTEX)
            {
                textureName += ".dtex";
            }
            else
            {
                textureName += ".png";
            }

            // IMPORTANT: The C++ structure expects a 128-byte fixed-length string for 'path' (was 32) for some reason)
            // That would correspond to the "path" field in the DataHeader struct.
            WriteFixedString(writer, textureName, 128);

            // Write ambient color ([1, 1, 1, 1] as default)
            Color ambientColor = Color.white;
            if (unityMaterial.HasProperty("_Ambient"))
            {
                ambientColor = unityMaterial.GetColor("_Ambient");
            }
            writer.Write(ambientColor.r);
            writer.Write(ambientColor.g);
            writer.Write(ambientColor.b);
            writer.Write(ambientColor.a);

            // Write diffuse color ([1, 1, 1, 1])
            Color diffuseColor = Color.white;
            if (unityMaterial.HasProperty("_Color"))
            {
                diffuseColor = unityMaterial.GetColor("_Color");
            }
            writer.Write(diffuseColor.r);
            writer.Write(diffuseColor.g);
            writer.Write(diffuseColor.b);
            writer.Write(diffuseColor.a);

            // Write specular color ([0, 0, 0, 1])
            Color specularColor = Color.black;
            if (unityMaterial.HasProperty("_SpecColor"))
            {
                specularColor = unityMaterial.GetColor("_SpecColor");
            }
            writer.Write(specularColor.r);
            writer.Write(specularColor.g);
            writer.Write(specularColor.b);
            writer.Write(specularColor.a);

            // Write emission color ([0, 0, 0, 1])
            Color emissiveColor = Color.black;
            if (unityMaterial.HasProperty("_ColorEmissive"))
            {
                emissiveColor = unityMaterial.GetColor("_ColorEmissive");
            }
            writer.Write(emissiveColor.r);
            writer.Write(emissiveColor.g);
            writer.Write(emissiveColor.b);
            writer.Write(emissiveColor.a);

            // Write shininess value (default 0)
            float shininess = 0;
            if (unityMaterial.HasProperty("_Shininess"))
            {
                shininess = unityMaterial.GetFloat("_Shininess");
            }
            writer.Write(shininess);

            // Write texture map names:
            // Diffuse map uses the texture name; the others are empty.
            WriteFixedString(writer, textureName, 32); // Diffuse map
            WriteFixedString(writer, "", 32);          // Light map
            WriteFixedString(writer, "", 32);          // Normal map
            WriteFixedString(writer, "", 32);          // Specular map
        }


        /// <summary>
        /// Writes the mesh header data to the binary writer.
        /// This method writes:
        ///   - A data header for the mesh (flags, local ID, and a fixed-length mesh name).
        ///   - The mesh header itself, including:
        ///       • The submesh count (as one byte).
        ///       • The vertex count (as an unsigned 32-bit integer).
        /// 
        /// Adjust the implementation if additional fields (like offsets) are needed.
        /// </summary>
        /// <param name="writer">BinaryWriter for the output file.</param>
        /// <param name="unityMesh">The Unity mesh whose data is being exported.</param>
        /// <param name="meshName">
        /// The name to assign to this mesh (typically using the Unity mesh’s name or its parent GameObject's name).
        /// </param>
        /// <param name="submeshCount">The number of submeshes present in the mesh.</param>
        /// <param name="vertexCount">
        /// The total number of vertices to be exported. Note: if you de-index or triangulate, this might differ from unityMesh.vertexCount.
        /// </param>
        private void WriteMeshHeader(BinaryWriter writer, Mesh unityMesh, string meshName, int submeshCount, uint vertexCount)
        {
            // Write Data Header for the mesh.
            // Flags: 0 (no special flags in this example).
            writer.Write((byte)0);
            // Local ID for this mesh (using 1 for now; increase if multiple meshes are exported).
            writer.Write((byte)1);
            // Write mesh name as a fixed-length string (32 bytes).
            WriteFixedString(writer, meshName, 128);

            // Now write the mesh header data:
            // Submesh count (1 byte)
            writer.Write((byte)submeshCount);
            // Vertex count (4 bytes, unsigned).
            writer.Write(vertexCount);

            // Optional: Add reserved fields or offsets for submesh data if the format requires it.
            // For example, you might later write:
            // writer.Write((uint)firstSubMeshOffset);
            // writer.Write((uint)nextMeshOffset);
        }


        /// <summary>
        /// Writes the mesh vertex data in a de-indexed manner.
        /// For each triangle index in the mesh, this method writes:
        ///   - Position (3 floats), applying conversion: from Unity’s (x, y, z)
        ///     to DCM’s expected ordering (x, z, y). This mimics the Blender-to-DCM 
        ///     conversion from the Python exporter.
        ///   - UV (2 floats), where the v-coordinate is inverted (1.0 - v)
        ///   - Color (4 bytes), converting each color component from 0–1 float to 0–255 byte
        ///   - Normal (3 floats), with the same coordinate reordering: (x, z, y)
        /// </summary>
        /// <param name="writer">BinaryWriter for the output file.</param>
        /// <param name="unityMesh">The Unity mesh to export.</param>
        private void WriteMeshVertices(BinaryWriter writer, Mesh unityMesh, GameObject go)
        {
            // Retrieve arrays from the Unity mesh.
            int[] triangles = unityMesh.triangles;
            Vector3[] vertices = unityMesh.vertices;
            Vector3[] normals = unityMesh.normals;
            Vector2[] uvs = unityMesh.uv;
            Color[] colors = unityMesh.colors; // May be empty if no vertex colors are assigned.

            Vector3 lossyScale = go.transform.lossyScale;
            Quaternion rotation = go.transform.rotation;// * Quaternion.Euler(0, 180, 0);

            Vector3 position = go.transform.position;

            // Iterate over each triangle
            for (int i = 0; i < triangles.Length; i += 3)
            {
                // Reverse the triangle winding when writing indices
                // Instead of 0,1,2 use 0,2,1 for each triangle
                int idx1 = triangles[i];
                int idx2 = triangles[i + 2]; // Swapped from i+1
                int idx3 = triangles[i + 1]; // Swapped from i+2

                // Then process each vertex with mirrored X
                ProcessVertex(writer, vertices, normals, uvs, colors, lossyScale, rotation, position, idx1, true);
                ProcessVertex(writer, vertices, normals, uvs, colors, lossyScale, rotation, position, idx2, true);
                ProcessVertex(writer, vertices, normals, uvs, colors, lossyScale, rotation, position, idx3, true);
            }
        }


        // Helper method to process a vertex with optional mirroring
        private void ProcessVertex(BinaryWriter writer, Vector3[] vertices, Vector3[] normals, Vector2[] uvs,
                                  Color[] colors, Vector3 lossyScale, Quaternion rotation, Vector3 position,
                                  int idx, bool mirrorX)
        {
            Vector3 pos = vertices[idx];
            pos = MultiplyVec3s(pos, lossyScale);
            pos = RotateAroundPoint(pos, Vector3.zero, rotation);
            pos += position;

            if (mirrorX)
                pos.x = -pos.x;

            // Write position
            writer.Write(pos.x);
            writer.Write(pos.y);
            writer.Write(pos.z);

            // UV coords
            Vector2 uv = (uvs != null && uvs.Length > idx) ? uvs[idx] : Vector2.zero;
            writer.Write(uv.x);
            writer.Write(uv.y);

            // Color
            Color col = (colors != null && colors.Length > idx) ? colors[idx] : Color.white;
            writer.Write((byte)Mathf.Clamp(Mathf.RoundToInt(col.r * 255), 0, 255));
            writer.Write((byte)Mathf.Clamp(Mathf.RoundToInt(col.g * 255), 0, 255));
            writer.Write((byte)Mathf.Clamp(Mathf.RoundToInt(col.b * 255), 0, 255));
            writer.Write((byte)Mathf.Clamp(Mathf.RoundToInt(col.a * 255), 0, 255));

            // Normal with mirroring if needed
            Vector3 norm = (normals != null && normals.Length > idx) ? normals[idx] : Vector3.up;
            norm = MultiplyVec3s(norm, lossyScale);
            norm = RotateAroundPoint(norm, Vector3.zero, rotation);

            if (mirrorX)
                norm.x = -norm.x;

            writer.Write(norm.x);
            writer.Write(norm.y);
            writer.Write(norm.z);
        }


        /// <summary>
        /// Writes the submesh header and index data for the de-indexed mesh.
        /// This method writes:
        /// 1. A data header for the submesh:
        ///    - Flags (1 byte, here 0)
        ///    - Local ID (1 byte, set to 1)
        ///    - A fixed-length submesh name (32 bytes; using the Unity mesh name)
        /// 2. The submesh header:
        ///    - Material ID (1 byte)
        ///    - Arrangement (1 byte, e.g., 2 for triangles)
        ///    - Submesh type (1 byte, e.g., 2 for indexed)
        ///    - Number of indices (2 bytes, ushort)
        /// 3. A sequential list of indices (each an unsigned 32-bit integer) that reference the de-indexed vertices.
        /// </summary>
        /// <param name="writer">BinaryWriter for the output file.</param>
        /// <param name="unityMesh">The Unity mesh being exported.</param>
        /// <param name="vertexCount">
        /// The de-indexed vertex count (should equal unityMesh.triangles.Length).
        /// </param>
        private void WriteSubmeshData(BinaryWriter writer, Mesh unityMesh, uint vertexCount)
        {
            // 1. Write Data Header for the submesh.
            writer.Write((byte)0);  // Flags: 0 (no special flags).
            writer.Write((byte)1);  // Local ID for this submesh (set to 1 in this example).
            WriteFixedString(writer, unityMesh.name, 128);  // Fixed-length submesh name.

            // 2. Write the Submesh Header.
            // Material ID: assuming 1 (or map to the appropriate material index if exporting multiple).
            byte materialId = 1;
            // Arrangement: 2 representing a triangle arrangement (as per Python exporter conventions).
            byte arrangement = 2;

            // Submesh Type: 2 representing INDEXED data (the Python example uses 2 for SUB_MESH_TYPE_INDEXED).
            byte submeshType = 2;

            // Number of indices - note: we cast vertexCount to ushort.
            // Ensure that vertexCount does not exceed ushort.MaxValue for your models.
            ushort numIndices = (ushort)vertexCount;

            writer.Write(materialId);
            writer.Write(arrangement);
            writer.Write(submeshType);
            writer.Write(numIndices);

            // 3. Write the indices.
            // Since we de-indexed the vertices, the indices are sequential.
            // Each index is written as a 32-bit unsigned integer.

            for (uint i = 0; i < vertexCount; i++)
            {
                writer.Write(i);
            }
        }


        /// <summary>
        /// Main export method.
        /// This example gathers materials from the selected GameObject (and its children)
        /// before writing out the file header and material data.
        /// </summary>
        // Modified to handle multiple GameObjects
        private bool OnDCMExport(string filePath, List<GameObject> gameObjectsToExport)
        {
            if (gameObjectsToExport == null || gameObjectsToExport.Count == 0)
            {
                Debug.LogWarning("No GameObjects to export!");
                return false;
            }

            using (BinaryWriter writer = new BinaryWriter(File.Open(filePath, FileMode.Create)))
            {
                if (writer == null)
                {
                    Debug.LogError("Couldn't open file for writing: " + filePath);
                    return false;
                }

                // Setup file header.
                FileHeader fheader = new FileHeader();
                fheader.id[0] = (byte)'D';
                fheader.id[1] = (byte)'C';
                fheader.id[2] = (byte)'M';
                fheader.version = DCM_CURRENT_VERSION;

                // Populate header values that weren't written before.
                List<Material> materialList = GetUniqueMaterials(gameObjectsToExport);
                fheader.material_count = (byte)materialList.Count;
                fheader.mesh_count = (byte)gameObjectsToExport.Count;  // Updated to match exported objects count
                fheader.armature_count = 0;         // No armatures.
                fheader.animation_count = 0;        // No animations.
                fheader.pos_format = (byte)PositionFormat.POSITION_FORMAT_3F;
                fheader.tex0_format = (byte)TexCoordFormat.TEX_COORD_FORMAT_2F;
                fheader.tex1_format = (byte)TexCoordFormat.TEX_COORD_FORMAT_NONE;
                fheader.color_format = (byte)ColorFormat.COLOR_FORMAT_4UB;
                fheader.offset_colour_format = (byte)ColorFormat.COLOR_FORMAT_NONE;
                fheader.normal_format = (byte)NormalFormat.NORMAL_FORMAT_3F;
                fheader.index_size = 4;             // 32-bit indices.
                fheader.bone_weights_format = 0;    // Assuming no bone weights.

                // Write out the full file header in the correct order.
                writer.Write(fheader.id[0]);
                writer.Write(fheader.id[1]);
                writer.Write(fheader.id[2]);
                writer.Write(fheader.version);
                writer.Write(fheader.material_count);
                writer.Write(fheader.mesh_count);
                writer.Write(fheader.armature_count);
                writer.Write(fheader.animation_count);
                writer.Write(fheader.pos_format);
                writer.Write(fheader.tex0_format);
                writer.Write(fheader.tex1_format);
                writer.Write(fheader.color_format);
                writer.Write(fheader.offset_colour_format);
                writer.Write(fheader.normal_format);
                writer.Write(fheader.index_size);
                writer.Write(fheader.bone_weights_format);

                // Now write out each material.
                foreach (Material mat in materialList)
                {
                    WriteMaterialData(writer, mat);
                }

                // Export each GameObject's mesh
                foreach (GameObject go in gameObjectsToExport)
                {
                    Mesh unityMesh = null;
                    MeshFilter meshFilter = go.GetComponent<MeshFilter>();

                    if (meshFilter != null && meshFilter.sharedMesh != null)
                    {
                        unityMesh = meshFilter.sharedMesh;
                    }
                    else
                    {
                        SkinnedMeshRenderer skinnedMesh = go.GetComponent<SkinnedMeshRenderer>();
                        if (skinnedMesh != null && skinnedMesh.sharedMesh != null)
                        {
                            unityMesh = skinnedMesh.sharedMesh;
                        }
                    }

                    if (unityMesh != null)
                    {
                        // For this example, we're assuming one submesh.
                        int submeshCount = unityMesh.subMeshCount;

                        // The de-indexed vertex count is the total number of triangle indices.
                        uint vertexCount = (uint)unityMesh.triangles.Length;

                        // Write the mesh header
                        WriteMeshHeader(writer, unityMesh, go.name, submeshCount, vertexCount);

                        // Write all vertices.
                        WriteMeshVertices(writer, unityMesh, go);

                        // Write submesh data for each submesh
                        for (int i = 0; i < submeshCount; i++)
                        {
                            WriteSubmeshData(writer, unityMesh, vertexCount);
                        }
                    }
                }
            }

            // Show success dialog with number of exported objects
            string message = $"Successfully exported {gameObjectsToExport.Count} meshes to {Path.GetFileName(filePath)}";
            DisplayDialogOK(DialogSeverity.Information, message);
            return true;
        }


        #region HELPERS
        bool DisplayDialogOK(DialogSeverity severity, string message) => EditorUtility.DisplayDialog(severity.ToString(), message, "OK");
        
        bool DisplayDialogOKCancel(DialogSeverity severity, string message) => EditorUtility.DisplayDialog(severity.ToString(), message, "OK", "Cancel");

        int DisplayDialogComplex(DialogSeverity severity, string message, string ok, string cancel, string alt) => EditorUtility.DisplayDialogComplex(severity.ToString(), message, ok, cancel, alt);

        Vector3 RotateAroundPoint(Vector3 point, Vector3 pivot, Quaternion angle)
        {
            return angle * (point - pivot) + pivot;
        }
        
        Vector3 MultiplyVec3s(Vector3 v1, Vector3 v2)
        {
            return new Vector3(v1.x * v2.x, v1.y * v2.y, v1.z * v2.z);
        }
        #endregion
    }
}
#endif