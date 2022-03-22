#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace lilOutlineUtil
{
    public class OutlineUtilWindow : EditorWindow
    {
        private static Vector2 scrollPosition = new Vector2(0,0);
        private static GameObject avatar;
        private static readonly Dictionary<int, MeshSettings[]> meshSettings = new Dictionary<int, MeshSettings[]>(); // <instanceID, <submesh, setting>>
        private static Dictionary<Mesh, Mesh> bakedMeshes = new Dictionary<Mesh, Mesh>();
        private static int lang = -1;
        private static bool isCancelled = false;
        private static readonly Color emptyColor = new Color(0.5f, 0.5f, 1.0f, 1.0f);
        private static GUIStyle marginBox;

        private enum BakeMode
        {
            Average,
            NormalMap,
            OtherMesh,
            Empty,
            Keep
        }

        private enum WidthBakeMode
        {
            Mask,
            Red,
            Green,
            Blue,
            Alpha,
            Empty
        }

        private struct MeshSettings
        {
            public string name;
            public bool isBakeTarget;
            public bool isSkinned;
            public BakeMode bakeMode;
            public WidthBakeMode widthBakeMode;
            public Texture2D normalMap;
            public Texture2D normalMask;
            public Texture2D widthMask;
            public Mesh referenceMesh;
            public float distanceThreshold;
            public float shrinkTipStrength;
        }

        //------------------------------------------------------------------------------------------------------------------------------
        // GUI
        [MenuItem("Window/_lil/Outline Util")]
        static void Init()
        {
            OutlineUtilWindow window = (OutlineUtilWindow)GetWindow(typeof(OutlineUtilWindow), false, TEXT_WINDOW_NAME);
            window.Show();
        }

        void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // Language
            if(lang == -1)
            {
                lang = Application.systemLanguage == SystemLanguage.Japanese ? 1 : 0;
            }
            lang = EditorGUILayout.Popup("Language", lang, TEXT_LANGUAGES);
            marginBox = new GUIStyle(EditorStyles.helpBox);
            marginBox.margin.left = 30;

            //------------------------------------------------------------------------------------------------------------------------------
            // 1. Select the mesh
            EditorGUILayout.LabelField(TEXT_STEP_SELECT_AVATAR[lang], EditorStyles.boldLabel);
            avatar = (GameObject)EditorGUILayout.ObjectField(TEXT_ITEM_DD_AVATAR[lang], avatar, typeof(GameObject), true);
            if(avatar == null)
            {
                EditorGUILayout.EndScrollView();
                return;
            }
            if(AssetDatabase.Contains(avatar))
            {
                EditorGUILayout.HelpBox(TEXT_WARN_SELECT_FROM_SCENE[lang], MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }
            EditorGUILayout.Space();

            //------------------------------------------------------------------------------------------------------------------------------
            // 2. Select the modify target
            EditorGUILayout.LabelField(TEXT_STEP_SELECT_SUBMESH[lang], EditorStyles.boldLabel);
            Component[] skinnedMeshRenderers = avatar.GetComponentsInChildren(typeof(SkinnedMeshRenderer), true);
            Component[] meshRenderers = avatar.GetComponentsInChildren(typeof(MeshRenderer), true);
            DrawModifyTargetsGUI(skinnedMeshRenderers, meshRenderers);
            EditorGUILayout.Space();

            //------------------------------------------------------------------------------------------------------------------------------
            // 3. Generate the mesh, test it, then save
            GameObject bakedAvatar = FindBakedAvatar();

            EditorGUILayout.LabelField(TEXT_STEP_GENERATE_AND_SAVE[lang], EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button(TEXT_BUTTON_GENERATE_AND_TEST[lang]))
            {
                GenerateMeshes(bakedAvatar, skinnedMeshRenderers, meshRenderers);
            }

            if(bakedAvatar == null)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndScrollView();
                return;
            }

            // Save
            bakedMeshes = new Dictionary<Mesh, Mesh>();
            GetBakedMeshes(bakedAvatar, skinnedMeshRenderers, meshRenderers);
            bool isSaved = true;
            foreach(Mesh bakedMesh in bakedMeshes.Values)
            {
                if(!isSaved) break;
                if(bakedMesh == null) continue;
                isSaved = AssetDatabase.Contains(bakedMesh);
            }

            GUIStyle saveButton = new GUIStyle(GUI.skin.button);
            if(!isSaved)
            {
                saveButton.normal.textColor = Color.red;
                saveButton.fontStyle = FontStyle.Bold;
            }

            if(GUILayout.Button(TEXT_BUTTON_SAVE[lang], saveButton))
            {
                SaveMeshes();
            }
            EditorGUILayout.EndHorizontal();

            if(!isSaved)
            {
                EditorGUILayout.HelpBox(TEXT_WARN_MESH_NOT_SAVED[lang], MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
        }

        //------------------------------------------------------------------------------------------------------------------------------
        // 2. Select the modify target
        private static void DrawModifyTargetsGUI(Component[] skinnedMeshRenderers, Component[] meshRenderers)
        {
            foreach(SkinnedMeshRenderer skinnedMeshRenderer in skinnedMeshRenderers)
            {
                EditorGUILayout.LabelField(skinnedMeshRenderer.gameObject.name, EditorStyles.boldLabel);
                int id = skinnedMeshRenderer.gameObject.GetInstanceID();
                Mesh sharedMesh = skinnedMeshRenderer.sharedMesh;
                Material[] materials = skinnedMeshRenderer.sharedMaterials;
                EditorGUI.indentLevel++;
                DrawGUIPerComponent(id, sharedMesh, materials);
                EditorGUI.indentLevel--;
            }

            foreach(MeshRenderer meshRenderer in meshRenderers)
            {
                MeshFilter meshFilter = meshRenderer.gameObject.GetComponent<MeshFilter>();
                if(meshFilter == null)
                {
                    continue;
                }
                EditorGUILayout.LabelField(meshRenderer.gameObject.name, EditorStyles.boldLabel);
                int id = meshRenderer.gameObject.GetInstanceID();
                Mesh sharedMesh = meshFilter.sharedMesh;
                Material[] materials = meshRenderer.sharedMaterials;
                EditorGUI.indentLevel++;
                DrawGUIPerComponent(id, sharedMesh, materials);
                EditorGUI.indentLevel--;
            }
        }

        private static void DrawGUIPerComponent(int id, Mesh sharedMesh, Material[] materials)
        {
            Vector3[] vertices = sharedMesh?.vertices;
            Vector3[] normals = sharedMesh?.normals;
            Vector4[] tangents = sharedMesh?.tangents;
            Color[] colors = sharedMesh?.colors;
            Vector2[] uv = sharedMesh?.uv;
            bool hasColors = colors != null && colors.Length > 2;
            bool hasUV0 = uv != null || uv.Length > 2;

            // Draw error messages
            if(sharedMesh == null)
            {
                EditorGUILayout.HelpBox(TEXT_WARN_MESH_IS_EMPTY[lang], MessageType.Error);
                return;
            }

            if(!sharedMesh.isReadable)
            {
                EditorGUILayout.HelpBox(TEXT_WARN_MESH_NOT_READABLE[lang], MessageType.Error);
                return;
            }

            if(vertices == null || vertices.Length < 2)
            {
                EditorGUILayout.HelpBox(TEXT_WARN_MESH_HAS_NO_VERT[lang], MessageType.Error);
                return;
            }

            if(normals == null && normals.Length < 2)
            {
                EditorGUILayout.HelpBox(TEXT_WARN_MESH_HAS_NO_NORM[lang], MessageType.Error);
                return;
            }

            if(tangents == null && tangents.Length < 2)
            {
                EditorGUILayout.HelpBox(TEXT_WARN_MESH_HAS_NO_TANJ[lang], MessageType.Error);
                return;
            }

            // Generate empty settings
            if(!meshSettings.ContainsKey(id)) meshSettings[id] = null;
            if(meshSettings[id] == null || meshSettings[id].Length != sharedMesh.subMeshCount)
            {
                meshSettings[id] = new MeshSettings[sharedMesh.subMeshCount];
                for(int i = 0; i < sharedMesh.subMeshCount; i++)
                {
                    meshSettings[id][i] = new MeshSettings
                    {
                        name = null,
                        isBakeTarget = false,
                        bakeMode = BakeMode.Average,
                        widthBakeMode = WidthBakeMode.Empty,
                        normalMap = null,
                        normalMask = null,
                        widthMask = null,
                        referenceMesh = null,
                        distanceThreshold = 0.0f,
                        shrinkTipStrength = 0.0f
                    };
                }
            }

            // Draw settings
            for(int i = 0; i < sharedMesh.subMeshCount; i++)
            {
                if(string.IsNullOrEmpty(meshSettings[id][i].name))
                {
                    meshSettings[id][i].name = i + ": ";
                    if(i < materials.Length && materials[i] != null && !string.IsNullOrEmpty(materials[i].name))
                    {
                        meshSettings[id][i].name += materials[i].name;
                    }
                }
                DrawMeshSettingsGUI(id, i, hasColors, hasUV0);
            }
        }

        private static void DrawMeshSettingsGUI(int id, int i, bool hasColors, bool hasUV0)
        {
            meshSettings[id][i].isBakeTarget = EditorGUILayout.ToggleLeft(meshSettings[id][i].name, meshSettings[id][i].isBakeTarget);

            int indentCopy = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            if(meshSettings[id][i].isBakeTarget)
            {
                EditorGUILayout.BeginVertical(marginBox);
                meshSettings[id][i].bakeMode = (BakeMode)EditorGUILayout.Popup(TEXT_ITEM_NORMAL_BAKE_MODE[lang], (int)meshSettings[id][i].bakeMode, TEXT_LABELS_NORMAL_BAKE_MODE[lang]);
                EditorGUI.indentLevel++;
                    if(meshSettings[id][i].bakeMode == BakeMode.Average)
                    {
                        meshSettings[id][i].distanceThreshold = EditorGUILayout.FloatField(TEXT_ITEM_DISTANCE_THRESHOLD[lang], meshSettings[id][i].distanceThreshold);
                        meshSettings[id][i].shrinkTipStrength = EditorGUILayout.FloatField(TEXT_ITEM_SHRINK_TIP[lang], meshSettings[id][i].shrinkTipStrength);
                        meshSettings[id][i].normalMask = (Texture2D)EditorGUILayout.ObjectField(TEXT_ITEM_NORMAL_MASK[lang], meshSettings[id][i].normalMask, typeof(Texture2D), false);
                    }
                    if(meshSettings[id][i].bakeMode == BakeMode.NormalMap)
                    {
                        meshSettings[id][i].normalMap = (Texture2D)EditorGUILayout.ObjectField(TEXT_ITEM_NORMAL_MAP[lang], meshSettings[id][i].normalMap, typeof(Texture2D), false);
                        if(!hasUV0) EditorGUILayout.HelpBox(TEXT_WARN_MESH_HAS_NO_UV[lang], MessageType.Warning);
                    }
                    if(meshSettings[id][i].bakeMode == BakeMode.OtherMesh)
                    {
                        meshSettings[id][i].referenceMesh = (Mesh)EditorGUILayout.ObjectField(TEXT_ITEM_REFERENCE_MESH[lang], meshSettings[id][i].referenceMesh, typeof(Mesh), false);
                        meshSettings[id][i].shrinkTipStrength = EditorGUILayout.FloatField(TEXT_ITEM_SHRINK_TIP[lang], meshSettings[id][i].shrinkTipStrength);
                        meshSettings[id][i].normalMask = (Texture2D)EditorGUILayout.ObjectField(TEXT_ITEM_NORMAL_MASK[lang], meshSettings[id][i].normalMask, typeof(Texture2D), false);
                    }
                    if(meshSettings[id][i].bakeMode == BakeMode.Keep && !hasColors)
                    {
                        EditorGUILayout.HelpBox(TEXT_WARN_MESH_HAS_NO_COLOR[lang], MessageType.Warning);
                    }
                EditorGUI.indentLevel--;

                meshSettings[id][i].widthBakeMode = (WidthBakeMode)EditorGUILayout.Popup(TEXT_ITEM_WIDTH_BAKE_MODE[lang], (int)meshSettings[id][i].widthBakeMode, TEXT_LABELS_WIDTH_BAKE_MODE[lang]);
                EditorGUI.indentLevel++;
                    if(meshSettings[id][i].widthBakeMode == WidthBakeMode.Mask)
                    {
                        meshSettings[id][i].widthMask = (Texture2D)EditorGUILayout.ObjectField(TEXT_ITEM_WIDTH_MASK[lang], meshSettings[id][i].widthMask, typeof(Texture2D), false);
                        if(!hasUV0) EditorGUILayout.HelpBox(TEXT_WARN_MESH_HAS_NO_UV[lang], MessageType.Warning);
                    }
                    if((meshSettings[id][i].widthBakeMode == WidthBakeMode.Red ||
                        meshSettings[id][i].widthBakeMode == WidthBakeMode.Green ||
                        meshSettings[id][i].widthBakeMode == WidthBakeMode.Blue ||
                        meshSettings[id][i].widthBakeMode == WidthBakeMode.Alpha) &&
                        !hasColors
                    )
                    {
                        EditorGUILayout.HelpBox(TEXT_WARN_MESH_HAS_NO_COLOR[lang], MessageType.Warning);
                    }
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }
            EditorGUI.indentLevel = indentCopy;
        }

        //------------------------------------------------------------------------------------------------------------------------------
        // 3. Generate the mesh, test it, then save
        private static int[] GetChildIndices(GameObject root, GameObject child)
        {
            var indices = new List<int>();
            indices.Add(child.transform.GetSiblingIndex());
            Transform parent = child.transform.parent;
            while(parent != null && parent != root.transform)
            {
                indices.Add(parent.GetSiblingIndex());
                parent = parent.parent;
            }
            return indices.ToArray();
        }

        private static GameObject GetChild(GameObject root, int[] indices)
        {
            Transform current = root.transform;
            for(int i = indices.Length - 1; i >= 0; i--)
            {
                current = current.GetChild(indices[i]);
                if(current == null) return null;
            }
            return current.gameObject;
        }

        private static GameObject GetChildInstance(GameObject root, GameObject rootInstance, GameObject child)
        {
            return GetChild(rootInstance, GetChildIndices(root, child));
        }

        private static void GenerateMeshes(GameObject bakedAvatar, Component[] skinnedMeshRenderers, Component[] meshRenderers)
        {
            if(bakedAvatar == null)
            {
                bakedAvatar = Instantiate(avatar);
                bakedAvatar.name = avatar.name + " (VertexColorBaked)";
                bakedAvatar.transform.parent = avatar.transform.parent;
            }

            isCancelled = false;
            foreach(SkinnedMeshRenderer skinnedMeshRenderer in skinnedMeshRenderers)
            {
                GameObject child = GetChildInstance(avatar, bakedAvatar, skinnedMeshRenderer.gameObject);
                if(child == null)
                {
                    Debug.LogWarning("[lilOutlineUtil] Child is not found");
                    continue;
                }
                SkinnedMeshRenderer bakedSkinnedMeshRenderer = child.GetComponent<SkinnedMeshRenderer>();
                if(bakedSkinnedMeshRenderer == null)
                {
                    Debug.LogWarning("[lilOutlineUtil] Component is not found");
                    continue;
                }

                int id = skinnedMeshRenderer.gameObject.GetInstanceID();
                Mesh sharedMesh = skinnedMeshRenderer.sharedMesh;
                Mesh bakedMesh = bakedSkinnedMeshRenderer.sharedMesh;
                if(bakedMesh == null || !bakedMesh.name.Contains("(Clone)"))
                {
                    bakedMesh = Instantiate(sharedMesh);
                }
                BakeVertexColors(ref bakedMesh, sharedMesh, id);
                if(isCancelled) break;

                if(bakedMesh == null)
                {
                    Debug.LogWarning("[lilOutlineUtil] Mesh is not found");
                    continue;
                }
                bakedSkinnedMeshRenderer.sharedMesh = bakedMesh;
            }

            foreach(MeshRenderer meshRenderer in meshRenderers)
            {
                MeshFilter meshFilter = meshRenderer.gameObject.GetComponent<MeshFilter>();
                if(meshFilter == null)
                {
                    continue;
                }
                GameObject child = GetChildInstance(avatar, bakedAvatar, meshRenderer.gameObject);
                if(child == null)
                {
                    Debug.LogWarning("[lilOutlineUtil] Child is not found");
                    continue;
                }
                MeshFilter bakedMeshFilter = child.GetComponent<MeshFilter>();
                if(bakedMeshFilter == null)
                {
                    Debug.LogWarning("[lilOutlineUtil] Component is not found");
                    continue;
                }

                int id = meshRenderer.gameObject.GetInstanceID();
                Mesh sharedMesh = meshFilter.sharedMesh;
                Mesh bakedMesh = bakedMeshFilter.sharedMesh;
                if(bakedMesh == null || !bakedMesh.name.Contains("(Clone)"))
                {
                    bakedMesh = Instantiate(sharedMesh);
                }
                BakeVertexColors(ref bakedMesh, sharedMesh, id);
                if(isCancelled) break;

                if(bakedMesh == null)
                {
                    Debug.LogWarning("[lilOutlineUtil] Mesh is not found");
                    continue;
                }
                bakedMeshFilter.sharedMesh = bakedMesh;
            }
            if(!isCancelled) EditorUtility.DisplayDialog(TEXT_WINDOW_NAME, "Complete!", "OK");
        }

        private static void GetBakedMeshes(GameObject bakedAvatar, Component[] skinnedMeshRenderers, Component[] meshRenderers)
        {
            foreach(SkinnedMeshRenderer skinnedMeshRenderer in skinnedMeshRenderers)
            {
                Mesh sharedMesh = skinnedMeshRenderer.sharedMesh;
                if(sharedMesh == null)
                {
                    Debug.LogWarning("[lilOutlineUtil] Mesh is not found");
                    continue;
                }
                GameObject child = GetChildInstance(avatar, bakedAvatar, skinnedMeshRenderer.gameObject);
                if(child == null)
                {
                    Debug.LogWarning("[lilOutlineUtil] Child is not found");
                    continue;
                }
                SkinnedMeshRenderer bakedSkinnedMeshRenderer = child.GetComponent<SkinnedMeshRenderer>();
                if(bakedSkinnedMeshRenderer == null)
                {
                    Debug.LogWarning("[lilOutlineUtil] Component is not found");
                    continue;
                }
                bakedMeshes[sharedMesh] = bakedSkinnedMeshRenderer.sharedMesh;
            }

            foreach(MeshRenderer meshRenderer in meshRenderers)
            {
                MeshFilter meshFilter = meshRenderer.gameObject.GetComponent<MeshFilter>();
                if(meshFilter == null)
                {
                    Debug.LogWarning("[lilOutlineUtil] Component is not found");
                    continue;
                }
                Mesh sharedMesh = meshFilter.sharedMesh;
                if(sharedMesh == null)
                {
                    Debug.LogWarning("[lilOutlineUtil] Mesh is not found");
                    continue;
                }
                GameObject child = GetChildInstance(avatar, bakedAvatar, meshRenderer.gameObject);
                if(child == null)
                {
                    Debug.LogWarning("[lilOutlineUtil] Child is not found");
                    continue;
                }
                MeshFilter bakedMeshFilter = child.GetComponent<MeshFilter>();
                if(bakedMeshFilter == null)
                {
                    Debug.LogWarning("[lilOutlineUtil] Component is not found");
                    continue;
                }
                bakedMeshes[sharedMesh] = bakedMeshFilter.sharedMesh;
            }
        }

        private static void SaveMeshes()
        {
            foreach(KeyValuePair<Mesh, Mesh> bakedMesh in bakedMeshes)
            {
                if(bakedMesh.Value == null || string.IsNullOrEmpty(bakedMesh.Value.name)) continue;

                string path = AssetDatabase.GetAssetPath(bakedMesh.Value);
                if(string.IsNullOrEmpty(path))
                {
                    path = AssetDatabase.GetAssetPath(bakedMesh.Key);
                    if(string.IsNullOrEmpty(path) || !path.StartsWith("Assets/"))
                    {
                        path = "Assets/BakedMeshes/" + bakedMesh.Value.name + ".asset";
                    }
                    else
                    {
                        path = Path.GetDirectoryName(path) + "/BakedMeshes/" + bakedMesh.Value.name + ".asset";
                    }
                    path = GetUniqueName(path);
                }

                string saveDirectory = Path.GetDirectoryName(path);
                if(!Directory.Exists(saveDirectory))
                {
                    Directory.CreateDirectory(saveDirectory);
                }
                if(!File.Exists(path))
                {
                    Debug.Log("[lilOutlineUtil] Create asset to: " + path);
                    AssetDatabase.CreateAsset(bakedMesh.Value, path);
                }
                else
                {
                    Debug.Log("[lilOutlineUtil] Overwrite mesh to: " + path);
                }
            }
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog(TEXT_WINDOW_NAME, "Complete!", "OK");
        }

        private static string GetUniqueName(string path)
        {
            if(!File.Exists(path)) return path;

            string baseName = Path.GetDirectoryName(path) + "/" + Path.GetFileNameWithoutExtension(path);
            string outPath;
            int i = 1;
            while(true)
            {
                outPath = baseName + " " + i.ToString() + ".asset";
                if(!File.Exists(outPath)) return outPath;
                i++;
            }
        }

        //------------------------------------------------------------------------------------------------------------------------------
        // Mesh Generator
        private static void BakeVertexColors(ref Mesh mesh, Mesh sharedMesh, int id)
        {
            if(isCancelled || sharedMesh == null || !sharedMesh.isReadable) return;
            Vector3[] vertices = sharedMesh.vertices;
            Vector3[] normals = sharedMesh.normals;
            Vector4[] tangents = sharedMesh.tangents;

            if(vertices == null || vertices.Length < 2 ||
               normals == null && normals.Length < 2 ||
               tangents == null && tangents.Length < 2)
            {
                return;
            }

            Color[] colors = sharedMesh.colors;
            Vector2[] uv = sharedMesh.uv;
            bool hasColors = colors != null && colors.Length > 2;
            bool hasUV0 = uv != null || uv.Length > 2;
            Color[] outColors = hasColors ? (Color[])colors.Clone() : Enumerable.Repeat(Color.white, vertices.Length).ToArray();

            isCancelled = false;
            for(int mi = 0; mi < sharedMesh.subMeshCount; mi++)
            {
                if(!meshSettings[id][mi].isBakeTarget) continue;
                meshSettings[id][mi] = FixInvalidSettings(meshSettings[id][mi], hasColors, hasUV0);

                // Get readable texture
                Texture2D normalMap = meshSettings[id][mi].normalMap;
                if(meshSettings[id][mi].bakeMode == BakeMode.NormalMap)
                {
                    GetReadableTexture(ref normalMap);
                }
                else
                {
                    normalMap = null;
                }

                Texture2D widthMask = meshSettings[id][mi].widthMask;
                if(meshSettings[id][mi].widthBakeMode == WidthBakeMode.Mask)
                {
                    GetReadableTexture(ref widthMask);
                }
                else
                {
                    widthMask = null;
                }

                Texture2D normalMask = meshSettings[id][mi].normalMask;
                if(normalMask != null && hasUV0 && (meshSettings[id][mi].bakeMode == BakeMode.Average || meshSettings[id][mi].bakeMode == BakeMode.OtherMesh))
                {
                    GetReadableTexture(ref normalMask);
                }
                else
                {
                    normalMask = null;
                }

                int[] sharedIndices = GetOptIndices(sharedMesh, mi);

                switch(meshSettings[id][mi].bakeMode)
                {
                    case BakeMode.Average:
                        if(normalMask != null)
                        {
                            BakeNormalAverage(ref outColors, sharedIndices, meshSettings[id][mi], vertices, normals, tangents, colors, uv, widthMask, normalMask, true);
                        }
                        else
                        {
                            BakeNormalAverage(ref outColors, sharedIndices, meshSettings[id][mi], vertices, normals, tangents, colors, uv, widthMask, normalMask, false);
                        }
                        break;
                    case BakeMode.NormalMap:
                        BakeNormalMap(ref outColors, sharedIndices, meshSettings[id][mi], colors, uv, widthMask, normalMap);
                        break;
                    case BakeMode.OtherMesh:
                        if(normalMask != null)
                        {
                            BakeNormalMesh(ref outColors, sharedIndices, meshSettings[id][mi], vertices, normals, tangents, colors, uv, widthMask, normalMask, true);
                        }
                        else
                        {
                            BakeNormalMesh(ref outColors, sharedIndices, meshSettings[id][mi], vertices, normals, tangents, colors, uv, widthMask, normalMask, false);
                        }
                        break;
                    case BakeMode.Empty:
                        BakeNormalEmpty(ref outColors, sharedIndices, meshSettings[id][mi], colors, uv, widthMask);
                        break;
                    case BakeMode.Keep:
                        BakeNormalKeep(ref outColors, sharedIndices, meshSettings[id][mi], colors, uv, widthMask);
                        break;
                    default:
                        BakeNormalEmpty(ref outColors, sharedIndices, meshSettings[id][mi], colors, uv, widthMask);
                        break;
                }
                EditorUtility.ClearProgressBar();
                if(isCancelled) return;
            }

            FixIllegalDatas(ref outColors);
            mesh.SetColors(outColors);
            EditorUtility.SetDirty(mesh);
        }

        //------------------------------------------------------------------------------------------------------------------------------
        // Bake normal to color
        private static void BakeNormalAverage(ref Color[] outColors, int[] sharedIndices, MeshSettings settings, Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Color[] colors, Vector2[] uv, Texture2D widthMask, Texture2D normalMask, bool useNormalMask)
        {
            var normalAverages = NormalGatherer.GetNormalAverages(sharedIndices, vertices, normals, settings.distanceThreshold);
            string message = "Run bake in " + settings.name;

            for(int i = 0; i < sharedIndices.Length; ++i)
            {
                int index = sharedIndices[i];
                float width = GetWidth(settings, colors, uv, index, widthMask);
                Vector3 normal = normals[index];
                Vector4 tangent = tangents[index];
                Vector3 bitangent = Vector3.Cross(normal, tangent) * tangent.w;
                if(IsIllegalTangent(normal, tangent) || useNormalMask && !GetNormalMask(uv, index, normalMask))
                {
                    outColors[index].r = 0.5f;
                    outColors[index].g = 0.5f;
                    outColors[index].b = 1.0f;
                    outColors[index].a = width;
                    continue;
                }
                Vector3 normalAverage = NormalGatherer.GetClosestNormal(normalAverages, vertices[index]);
                if(settings.shrinkTipStrength > 0) width *= Mathf.Pow(Mathf.Clamp01(Vector3.Dot(normal,normalAverage)), settings.shrinkTipStrength);
                outColors[index].r = Vector3.Dot(normalAverage, tangent) * 0.5f + 0.5f;
                outColors[index].g = Vector3.Dot(normalAverage, bitangent) * 0.5f + 0.5f;
                outColors[index].b = Vector3.Dot(normalAverage, normal) * 0.5f + 0.5f;
                outColors[index].a = width;
                if(DrawProgress(message, i, (float)i / (float)sharedIndices.Length)) return;
            }
        }

        private static void BakeNormalMesh(ref Color[] outColors, int[] sharedIndices, MeshSettings settings, Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Color[] colors, Vector2[] uv, Texture2D widthMask, Texture2D normalMask, bool useNormalMask)
        {
            Vector3[] refVertices = settings.referenceMesh.vertices;
            Vector3[] refNormals = settings.referenceMesh.normals;
            var normalOriginal = NormalGatherer.GetNormalAveragesFast(sharedIndices, refVertices, refNormals);
            string message = "Run bake in " + settings.name;

            for(int i = 0; i < sharedIndices.Length; ++i)
            {
                int index = sharedIndices[i];
                float width = GetWidth(settings, colors, uv, index, widthMask);
                Vector3 normal = normals[index];
                Vector4 tangent = tangents[index];
                Vector3 bitangent = Vector3.Cross(normal, tangent) * (tangent.w >= 0 ? 1 : -1);
                if(IsIllegalTangent(normal, tangent) || useNormalMask && !GetNormalMask(uv, index, normalMask))
                {
                    outColors[index].r = 0.5f;
                    outColors[index].g = 0.5f;
                    outColors[index].b = 1.0f;
                    outColors[index].a = width;
                    continue;
                }
                Vector3 normalAverage = NormalGatherer.GetClosestNormal(normalOriginal, vertices[index]);
                if(settings.shrinkTipStrength > 0) width *= Mathf.Pow(Mathf.Clamp01(Vector3.Dot(normal,normalAverage)), settings.shrinkTipStrength);
                outColors[index].r = Vector3.Dot(normalAverage, tangent) * 0.5f + 0.5f;
                outColors[index].g = Vector3.Dot(normalAverage, bitangent) * 0.5f + 0.5f;
                outColors[index].b = Vector3.Dot(normalAverage, normal) * 0.5f + 0.5f;
                outColors[index].a = width;
                if(DrawProgress(message, i, (float)i / (float)sharedIndices.Length)) return;
            }
        }

        private static void BakeNormalMap(ref Color[] outColors, int[] sharedIndices, MeshSettings settings, Color[] colors, Vector2[] uv, Texture2D widthMask, Texture2D normalMap)
        {
            string message = "Run bake in " + settings.name;

            for(int i = 0; i < sharedIndices.Length; ++i)
            {
                int index = sharedIndices[i];
                Color normalMapColor = normalMap.GetPixelBilinear(uv[index].x, uv[index].y);
                outColors[index].r = normalMapColor.r;
                outColors[index].g = normalMapColor.g;
                outColors[index].b = normalMapColor.b;
                outColors[index].a = GetWidth(settings, colors, uv, index, widthMask);
                if(DrawProgress(message, i, (float)i / (float)sharedIndices.Length)) return;
            }
        }

        private static void BakeNormalEmpty(ref Color[] outColors, int[] sharedIndices, MeshSettings settings, Color[] colors, Vector2[] uv, Texture2D widthMask)
        {
            string message = "Run bake in " + settings.name;

            for(int i = 0; i < sharedIndices.Length; ++i)
            {
                int index = sharedIndices[i];
                outColors[index].r = 0.5f;
                outColors[index].g = 0.5f;
                outColors[index].b = 1.0f;
                outColors[index].a = GetWidth(settings, colors, uv, index, widthMask);
                if(DrawProgress(message, i, (float)i / (float)sharedIndices.Length)) return;
            }
        }

        private static void BakeNormalKeep(ref Color[] outColors, int[] sharedIndices, MeshSettings settings, Color[] colors, Vector2[] uv, Texture2D widthMask)
        {
            string message = "Run bake in " + settings.name;

            for(int i = 0; i < sharedIndices.Length; ++i)
            {
                int index = sharedIndices[i];
                outColors[index].a = GetWidth(settings, colors, uv, index, widthMask);
                if(DrawProgress(message, i, (float)i / (float)sharedIndices.Length)) return;
            }
        }

        private static float GetWidth(MeshSettings settings, Color[] colors, Vector2[] uv, int index, Texture2D widthMask)
        {
            switch(settings.widthBakeMode)
            {
                case WidthBakeMode.Mask:
                    if(widthMask == null) return 1.0f;
                    return widthMask.GetPixelBilinear(uv[index].x, uv[index].y).r;
                case WidthBakeMode.Red:
                    return colors[index].r;
                case WidthBakeMode.Green:
                    return colors[index].g;
                case WidthBakeMode.Blue:
                    return colors[index].b;
                case WidthBakeMode.Alpha:
                    return colors[index].a;
                case WidthBakeMode.Empty:
                    return 1.0f;
                default:
                    return 1.0f;
            }
        }

        private static bool GetNormalMask(Vector2[] uv, int index, Texture2D normalMask)
        {
            return normalMask.GetPixelBilinear(uv[index].x, uv[index].y).r > 0.5f;
        }

        public static bool DrawProgress(string message, int i, float progress)
        {
            if((i & 0b11111111) == 0b11111111) return isCancelled = isCancelled || EditorUtility.DisplayCancelableProgressBar(TEXT_WINDOW_NAME, message, progress);
            return isCancelled;
        }

        private static int[] GetOptIndices(Mesh mesh, int mi)
        {
            return mesh.GetIndices(mi).ToList().Distinct().ToArray();
        }

        private static bool IsIllegalTangent(Vector3 normal, Vector4 tangent)
        {
            return normal.x == tangent.x && normal.y == tangent.y && normal.z == tangent.z;
        }

        //------------------------------------------------------------------------------------------------------------------------------
        // Utils
        private static void GetReadableTexture(ref Texture2D tex)
        {
            #if UNITY_2018_3_OR_NEWER
            if(!tex.isReadable)
            #endif
            {
                RenderTexture bufRT = RenderTexture.active;
                RenderTexture texR = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
                Graphics.Blit(tex, texR);
                RenderTexture.active = texR;
                tex = new Texture2D(texR.width, texR.height);
                tex.ReadPixels(new Rect(0, 0, texR.width, texR.height), 0, 0);
                tex.Apply();
                RenderTexture.active = bufRT;
                RenderTexture.ReleaseTemporary(texR);
            }
        }

        private static GameObject FindBakedAvatar()
        {
            if(avatar.transform.parent != null)
            {
                for(int i = 0; i < avatar.transform.parent.childCount; i++)
                {
                    GameObject childObject = avatar.transform.parent.GetChild(i).gameObject;
                    if(childObject.name.Contains(avatar.name + " (VertexColorBaked)"))
                    {
                        return childObject;
                    }
                }
            }

            return GameObject.Find(avatar.name + " (VertexColorBaked)");
        }

        private static void FixIllegalDatas(ref Color[] outColors)
        {
            for(int i = 0; i < outColors.Length; i++)
            {
                Color color = outColors[i];
                if(
                    color.r >= 0 && color.r <= 1 &&
                    color.g >= 0 && color.g <= 1 &&
                    color.b >= 0 && color.b <= 1 &&
                    color.a >= 0 && color.a <= 1
                )
                {
                    continue;
                }

                outColors[i] = emptyColor;
            }
        }

        private static MeshSettings FixInvalidSettings(MeshSettings settings, bool hasColors, bool hasUV0)
        {
            if(settings.bakeMode == BakeMode.NormalMap && (!hasUV0 || settings.normalMap == null))
            {
                settings.bakeMode = BakeMode.Empty;
            }
            if(settings.bakeMode == BakeMode.NormalMap && settings.referenceMesh == null)
            {
                settings.bakeMode = BakeMode.Empty;
            }
            if(settings.bakeMode == BakeMode.Keep && !hasColors)
            {
                settings.bakeMode = BakeMode.Empty;
            }
            if(settings.widthBakeMode == WidthBakeMode.Mask && (!hasUV0 || settings.widthMask == null))
            {
                settings.widthBakeMode = WidthBakeMode.Empty;
            }
            if((settings.widthBakeMode == WidthBakeMode.Red ||
                settings.widthBakeMode == WidthBakeMode.Green ||
                settings.widthBakeMode == WidthBakeMode.Blue ||
                settings.widthBakeMode == WidthBakeMode.Alpha) &&
                !hasColors
            )
            {
                settings.widthBakeMode = WidthBakeMode.Empty;
            }

            return settings;
        }

        //------------------------------------------------------------------------------------------------------------------------------
        // Languages
        private const string TEXT_WINDOW_NAME = "lilOutlineUtil";

        private static readonly string[] TEXT_LANGUAGES                 = new[] {"English", "Japanese"};

        private static readonly string[] TEXT_STEP_SELECT_AVATAR        = new[] {"1. Select the avatar",                        "1. アバターを選択"};
        private static readonly string[] TEXT_STEP_SELECT_SUBMESH       = new[] {"2. Select the modify target",                 "2. 編集対象を選択"};
        private static readonly string[] TEXT_STEP_GENERATE_AND_SAVE    = new[] {"3. Generate the mesh, test it, then save",    "3. メッシュを生成・テスト・保存"};

        private static readonly string[] TEXT_WARN_SELECT_FROM_SCENE    = new[] {"Please select from the scene (hierarchy)",            "シーン（ヒエラルキー）から選択してください"};
        private static readonly string[] TEXT_WARN_MESH_NOT_READABLE    = new[] {"The selected mesh is not set to \"Read/Write\" on.",  "選択されたメッシュは\"Read/Write\"がオンになっていません。"};
        private static readonly string[] TEXT_WARN_MESH_IS_EMPTY        = new[] {"The selected mesh is empty!",         "選択したメッシュは空です"};
        private static readonly string[] TEXT_WARN_MESH_HAS_NO_VERT     = new[] {"The selected mesh has no vertices!",  "選択したメッシュは頂点がありません。"};
        private static readonly string[] TEXT_WARN_MESH_HAS_NO_NORM     = new[] {"The selected mesh has no normals!",   "選択したメッシュは法線がありません。"};
        private static readonly string[] TEXT_WARN_MESH_HAS_NO_TANJ     = new[] {"The selected mesh has no tangents!",  "選択したメッシュはタンジェントがありません。"};
        private static readonly string[] TEXT_WARN_MESH_HAS_NO_UV       = new[] {"The setting is ignored because there is no uv.",              "UVが存在しないため設定が無視されます。"};
        private static readonly string[] TEXT_WARN_MESH_HAS_NO_COLOR    = new[] {"The setting is ignored because there is no vertex color.",    "頂点カラーが存在しないため設定が無視されます。"};
        private static readonly string[] TEXT_WARN_MESH_NOT_SAVED       = new[] {"Generated mesh is not saved!",        "生成されたメッシュが保存されていません。"};

        private static readonly string[] TEXT_ITEM_DD_AVATAR            = new[] {"Avatar (D&D from scene)", "アバター (シーンからD&D)"};
        private static readonly string[] TEXT_ITEM_NORMAL_BAKE_MODE     = new[] {"Bake Mode (Normal)",      "ベイクモード（方向）"};
        private static readonly string[] TEXT_ITEM_DISTANCE_THRESHOLD   = new[] {"Distance Threshold",      "同一頂点とみなす距離"};
        private static readonly string[] TEXT_ITEM_NORMAL_MAP           = new[] {"Normal map",              "ノーマルマップ"};
        private static readonly string[] TEXT_ITEM_REFERENCE_MESH       = new[] {"Reference mesh",          "参照メッシュ"};
        private static readonly string[] TEXT_ITEM_NORMAL_MASK          = new[] {"Bake mask",               "ベイクマスク"};
        private static readonly string[] TEXT_ITEM_SHRINK_TIP           = new[] {"Shrink the tip",          "先端を細くする度合い"};
        private static readonly string[] TEXT_ITEM_WIDTH_BAKE_MODE      = new[] {"Bake Mode (Width)",       "ベイクモード（太さ）"};
        private static readonly string[] TEXT_ITEM_WIDTH_MASK           = new[] {"Width mask",              "太さマスク"};

        private static readonly string[] TEXT_BUTTON_GENERATE_AND_TEST  = new[] {"Generate & Test",         "生成 & テスト"};
        private static readonly string[] TEXT_BUTTON_SAVE               = new[] {"Save",                    "保存"};

        private static readonly string[][] TEXT_LABELS_NORMAL_BAKE_MODE = {
            new[] {"Average of close vertices", "Normal map", "From other mesh", "Bake empty normal", "Keep vertex color"},
            new[] {"近傍頂点の法線の平均", "ノーマルマップ", "外部メッシュの参照", "空の法線", "元の頂点カラーを保持"}
        };
        private static readonly string[][] TEXT_LABELS_WIDTH_BAKE_MODE = {
            new[] {"Mask texture", "Vertex Color (R)", "Vertex Color (G)", "Vertex Color (B)", "Vertex Color (A)", "Maximum value (1.0)"},
            new[] {"マスクテクスチャ", "頂点カラー (R)", "頂点カラー (G)", "頂点カラー (B)", "頂点カラー (A)", "最大値 (1.0)"}
        };
    }

    public class NormalGatherer
    {
        public static Dictionary<Vector3, Vector3> GetNormalAveragesFast(int[] sharedIndices, Vector3[] vertices, Vector3[] normals)
        {
            var normalAverages = new Dictionary<Vector3, Vector3>();
            string message = "Generating averages";

            for(int i = 0; i < sharedIndices.Length; i++)
            {
                int index = sharedIndices[i];
                Vector3 pos = vertices[index];
                if(!normalAverages.ContainsKey(pos))
                {
                    normalAverages[pos] = normals[index];
                    continue;
                }
                normalAverages[pos] += normals[index];
                if(OutlineUtilWindow.DrawProgress(message, i, (float)i / (float)vertices.Length)) return normalAverages;
            }

            var keys = normalAverages.Keys.ToArray();
            for(int j = 0; j < keys.Length; j++)
            {
                normalAverages[keys[j]] = Vector3.Normalize(normalAverages[keys[j]]);
            }

            return normalAverages;
        }

        public static Dictionary<Vector3, Vector3> GetNormalAverages(int[] sharedIndices, Vector3[] vertices, Vector3[] normals, float threshold)
        {
            if(threshold == 0.0f) return GetNormalAveragesFast(sharedIndices, vertices, normals);
            var normalAverages = new Dictionary<Vector3, Vector3>();
            string message = "Generating averages";

            for(int i = 0; i < sharedIndices.Length; i++)
            {
                int index = sharedIndices[i];
                Vector3 pos = vertices[index];
                if(normalAverages.ContainsKey(pos)) continue;
                Vector3 average = new Vector3(0,0,0);
                for(int j = 0; j < vertices.Length; j++)
                {
                    average += Vector3.Distance(pos, vertices[j]) < threshold ? normals[j] : Vector3.zero;
                }
                normalAverages[pos] = Vector3.Normalize(average);
                if(OutlineUtilWindow.DrawProgress(message, i, (float)i / (float)sharedIndices.Length)) return normalAverages;
            }

            return normalAverages;
        }

        public static Vector3 GetClosestNormal(Dictionary<Vector3, Vector3> normalAverages, Vector3 pos)
        {
            if(normalAverages.ContainsKey(pos)) return normalAverages[pos];

            float closestDist = 1000.0f;
            Vector3 closestNormal = new Vector3(0,0,0);
            foreach(KeyValuePair<Vector3, Vector3> normalAverage in normalAverages)
            {
                float dist = Vector3.Distance(pos, normalAverage.Key);
                closestDist = dist < closestDist ? dist : closestDist;
                closestNormal = dist < closestDist ? normalAverage.Value : closestNormal;
            }

            return closestNormal;
        }
    }

}
#endif