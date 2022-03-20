#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace lilOutlineUtil
{
    public class OutlineUtilWindow : EditorWindow
    {
        private static Vector2 scrollPosition = new Vector2(0,0);
        private static GameObject gameObject;
        private static MeshSettings[] meshSettings = null;
        private static int lang = -1;
        private static bool isCancelled = false;
        private static readonly Color emptyColor = new Color(0.5f, 0.5f, 1.0f, 1.0f);

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
            public BakeMode bakeMode;
            public WidthBakeMode widthBakeMode;
            public Texture2D normalMap;
            public Texture2D normalMask;
            public Texture2D widthMask;
            public Mesh mesh;
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

            //------------------------------------------------------------------------------------------------------------------------------
            // 1. Select the mesh
            EditorGUILayout.LabelField(TEXT_STEP_SELECT_MESH[lang], EditorStyles.boldLabel);
            gameObject = (GameObject)EditorGUILayout.ObjectField(TEXT_ITEM_DD_MESH[lang], gameObject, typeof(GameObject), true);
            if(gameObject == null)
            {
                EditorGUILayout.EndScrollView();
                return;
            }
            EditorGUILayout.Space();

            // Get mesh data
            MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();
            SkinnedMeshRenderer skinnedMeshRenderer = gameObject.GetComponent<SkinnedMeshRenderer>();
            bool isSkinned = skinnedMeshRenderer != null;
            Mesh sharedMesh = isSkinned ? skinnedMeshRenderer?.sharedMesh : meshFilter?.sharedMesh;
            Material[] materials = isSkinned ? skinnedMeshRenderer?.sharedMaterials : meshRenderer?.sharedMaterials;

            // Draw error messages
            if(AssetDatabase.Contains(gameObject))
            {
                EditorGUILayout.HelpBox(TEXT_WARN_SELECT_FROM_SCENE[lang], MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            if((meshFilter == null || meshRenderer == null) && skinnedMeshRenderer == null)
            {
                EditorGUILayout.HelpBox(TEXT_WARN_OBJ_NOT_HAVE_MESH[lang], MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            if(sharedMesh == null)
            {
                EditorGUILayout.HelpBox(TEXT_WARN_MESH_IS_EMPTY[lang], MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            if(!sharedMesh.isReadable)
            {
                EditorGUILayout.HelpBox(TEXT_WARN_MESH_NOT_READABLE[lang], MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            if(sharedMesh.vertices == null || sharedMesh.vertices.Length < 2)
            {
                EditorGUILayout.HelpBox(TEXT_WARN_MESH_HAS_NO_VERT[lang], MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            if(sharedMesh.normals == null && sharedMesh.normals.Length < 2)
            {
                EditorGUILayout.HelpBox(TEXT_WARN_MESH_HAS_NO_NORM[lang], MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            if(sharedMesh.tangents == null && sharedMesh.tangents.Length < 2)
            {
                EditorGUILayout.HelpBox(TEXT_WARN_MESH_HAS_NO_TANJ[lang], MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            //------------------------------------------------------------------------------------------------------------------------------
            // 2. Select the modify target
            EditorGUILayout.LabelField(TEXT_STEP_SELECT_SUBMESH[lang], EditorStyles.boldLabel);
            if(meshSettings == null || meshSettings.Length != sharedMesh.subMeshCount)
            {
                meshSettings = new MeshSettings[sharedMesh.subMeshCount];
                for(int i = 0; i < sharedMesh.subMeshCount; i++)
                {
                    meshSettings[i] = new MeshSettings
                    {
                        name = null,
                        isBakeTarget = false,
                        bakeMode = BakeMode.Average,
                        widthBakeMode = WidthBakeMode.Empty,
                        normalMap = null,
                        normalMask = null,
                        widthMask = null,
                        mesh = null,
                        distanceThreshold = 0.0f,
                        shrinkTipStrength = 0.0f
                    };
                }
            }

            for(int i = 0; i < sharedMesh.subMeshCount; i++)
            {
                if(string.IsNullOrEmpty(meshSettings[i].name))
                {
                    meshSettings[i].name = i + ": ";
                    if(i < materials.Length && materials[i] != null && !string.IsNullOrEmpty(materials[i].name))
                    {
                        meshSettings[i].name += materials[i].name;
                    }
                }
                DrawMeshSettingsGUI(i);
            }
            EditorGUILayout.Space();

            //------------------------------------------------------------------------------------------------------------------------------
            // 3. Generate the mesh, test it, then save
            EditorGUILayout.LabelField(TEXT_STEP_GENERATE_AND_SAVE[lang], EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button(TEXT_BUTTON_GENERATE_AND_TEST[lang]))
            {
                BakeVertexColors(sharedMesh, isSkinned);
                if(!isCancelled) EditorUtility.DisplayDialog(TEXT_WINDOW_NAME, "Complete!", "OK");
            }

            GameObject bakedObject = FindBakedObject();
            if(bakedObject == null)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndScrollView();
                return;
            }

            MeshFilter bakedMeshFilter = bakedObject.GetComponent<MeshFilter>();
            SkinnedMeshRenderer bakedSkinnedMeshRenderer = bakedObject.GetComponent<SkinnedMeshRenderer>();
            Mesh bakedMesh = isSkinned ? bakedSkinnedMeshRenderer?.sharedMesh : bakedMeshFilter?.sharedMesh;
            if(bakedMesh == null)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndScrollView();
                return;
            }

            GUIStyle saveButton = new GUIStyle(GUI.skin.button);
            bool isSaved = AssetDatabase.Contains(bakedMesh);
            if(!isSaved)
            {
                saveButton.normal.textColor = Color.red;
                saveButton.fontStyle = FontStyle.Bold;
            }

            if(GUILayout.Button(TEXT_BUTTON_SAVE[lang], saveButton))
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndScrollView();

                string path = "Assets/";
                if(isSaved)
                {
                    path = AssetDatabase.GetAssetPath(bakedMesh);
                    if(string.IsNullOrEmpty(path)) path = "Assets/";
                    else path = Path.GetDirectoryName(path);
                }
                path = EditorUtility.SaveFilePanel(TEXT_BUTTON_SAVE[lang], path, bakedMesh.name, "asset");
                if(!string.IsNullOrEmpty(path))
                {
                    AssetDatabase.CreateAsset(bakedMesh, FileUtil.GetProjectRelativePath(path));
                }
                return;
            }
            EditorGUILayout.EndHorizontal();

            if(!isSaved)
            {
                EditorGUILayout.HelpBox(TEXT_WARN_MESH_NOT_SAVED[lang], MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawMeshSettingsGUI(int i)
        {
            meshSettings[i].isBakeTarget = EditorGUILayout.ToggleLeft(meshSettings[i].name, meshSettings[i].isBakeTarget);

            if(meshSettings[i].isBakeTarget)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                meshSettings[i].bakeMode = (BakeMode)EditorGUILayout.Popup(TEXT_ITEM_NORMAL_BAKE_MODE[lang], (int)meshSettings[i].bakeMode, TEXT_LABELS_NORMAL_BAKE_MODE[lang]);
                EditorGUI.indentLevel++;
                    if(meshSettings[i].bakeMode == BakeMode.Average)
                    {
                        meshSettings[i].distanceThreshold = EditorGUILayout.FloatField(TEXT_ITEM_DISTANCE_THRESHOLD[lang], meshSettings[i].distanceThreshold);
                        meshSettings[i].shrinkTipStrength = EditorGUILayout.FloatField(TEXT_ITEM_SHRINK_TIP[lang], meshSettings[i].shrinkTipStrength);
                        meshSettings[i].normalMask = (Texture2D)EditorGUILayout.ObjectField(TEXT_ITEM_NORMAL_MASK[lang], meshSettings[i].normalMask, typeof(Texture2D), false);
                    }
                    if(meshSettings[i].bakeMode == BakeMode.NormalMap)
                    {
                        meshSettings[i].normalMap = (Texture2D)EditorGUILayout.ObjectField(TEXT_ITEM_NORMAL_MAP[lang], meshSettings[i].normalMap, typeof(Texture2D), false);
                    }
                    if(meshSettings[i].bakeMode == BakeMode.OtherMesh)
                    {
                        meshSettings[i].mesh = (Mesh)EditorGUILayout.ObjectField(TEXT_ITEM_REFERENCE_MESH[lang], meshSettings[i].mesh, typeof(Mesh), false);
                        meshSettings[i].shrinkTipStrength = EditorGUILayout.FloatField(TEXT_ITEM_SHRINK_TIP[lang], meshSettings[i].shrinkTipStrength);
                        meshSettings[i].normalMask = (Texture2D)EditorGUILayout.ObjectField(TEXT_ITEM_NORMAL_MASK[lang], meshSettings[i].normalMask, typeof(Texture2D), false);
                    }
                EditorGUI.indentLevel--;

                meshSettings[i].widthBakeMode = (WidthBakeMode)EditorGUILayout.Popup(TEXT_ITEM_WIDTH_BAKE_MODE[lang], (int)meshSettings[i].widthBakeMode, TEXT_LABELS_WIDTH_BAKE_MODE[lang]);
                EditorGUI.indentLevel++;
                    if(meshSettings[i].widthBakeMode == WidthBakeMode.Mask)
                    {
                        meshSettings[i].widthMask = (Texture2D)EditorGUILayout.ObjectField(TEXT_ITEM_WIDTH_MASK[lang], meshSettings[i].widthMask, typeof(Texture2D), false);
                    }
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }
        }

        //------------------------------------------------------------------------------------------------------------------------------
        // Mesh Generator
        private static void BakeVertexColors(Mesh sharedMesh, bool isSkinned)
        {
            Mesh mesh = Instantiate(sharedMesh);
            Vector3[] vertices = sharedMesh.vertices;
            Vector3[] normals = sharedMesh.normals;
            Vector4[] tangents = sharedMesh.tangents;
            Color[] colors = sharedMesh.colors;
            Vector2[] uv = sharedMesh.uv;
            bool hasColors = colors != null && colors.Length > 2;
            bool hasUV0 = uv != null || uv.Length > 2;
            Color[] outColors = hasColors ? colors : Enumerable.Repeat(Color.white, vertices.Length).ToArray();

            isCancelled = false;
            for(int mi = 0; mi < sharedMesh.subMeshCount; mi++)
            {
                if(!meshSettings[mi].isBakeTarget) continue;

                Texture2D widthMask = meshSettings[mi].widthMask;
                if(widthMask != null && hasUV0 && meshSettings[mi].widthBakeMode == WidthBakeMode.Mask)
                {
                    GetReadableTexture(ref widthMask);
                }
                else
                {
                    widthMask = null;
                }

                Texture2D normalMask = meshSettings[mi].normalMask;
                if(normalMask != null && hasUV0 && (meshSettings[mi].bakeMode == BakeMode.Average || meshSettings[mi].bakeMode == BakeMode.OtherMesh))
                {
                    GetReadableTexture(ref normalMask);
                }
                else
                {
                    normalMask = null;
                }

                int[] sharedIndices = GetOptIndices(sharedMesh, mi);

                switch(meshSettings[mi].bakeMode)
                {
                    case BakeMode.Average:
                        if(normalMask != null)
                        {
                            BakeNormalAverage(ref outColors, sharedIndices, mi, vertices, normals, tangents, colors, uv, widthMask, normalMask, true);
                        }
                        else
                        {
                            BakeNormalAverage(ref outColors, sharedIndices, mi, vertices, normals, tangents, colors, uv, widthMask, normalMask, false);
                        }
                        break;
                    case BakeMode.NormalMap:
                        BakeNormalMap(ref outColors, sharedIndices, mi, colors, uv, widthMask);
                        break;
                    case BakeMode.OtherMesh:
                        if(normalMask != null)
                        {
                            BakeNormalMesh(ref outColors, sharedIndices, mi, vertices, normals, tangents, colors, uv, widthMask, normalMask, true);
                        }
                        else
                        {
                            BakeNormalMesh(ref outColors, sharedIndices, mi, vertices, normals, tangents, colors, uv, widthMask, normalMask, false);
                        }
                        break;
                    case BakeMode.Empty:
                        BakeNormalEmpty(ref outColors, sharedIndices, mi, colors, uv, widthMask);
                        break;
                    case BakeMode.Keep:
                        BakeNormalKeep(ref outColors, sharedIndices, mi, colors, uv, widthMask);
                        break;
                    default:
                        BakeNormalKeep(ref outColors, sharedIndices, mi, colors, uv, widthMask);
                        break;
                }
                EditorUtility.ClearProgressBar();
                if(isCancelled) return;
            }

            FixIllegalDatas(ref outColors);
            mesh.SetColors(outColors);

            GameObject bakedObject = FindBakedObject();
            if(bakedObject == null)
            {
                bakedObject = Instantiate(gameObject);
                bakedObject.name = gameObject.name + " (VertexColorBaked)";
                bakedObject.transform.parent = gameObject.transform.parent;
            }

            if(isSkinned)
            {
                SkinnedMeshRenderer bakedSkinnedMeshRenderer = bakedObject.GetComponent<SkinnedMeshRenderer>();
                bakedSkinnedMeshRenderer.sharedMesh = mesh;
            }
            else
            {
                MeshFilter bakedMeshFilter = bakedObject.GetComponent<MeshFilter>();
                bakedMeshFilter.mesh = mesh;
            }
        }

        //------------------------------------------------------------------------------------------------------------------------------
        // Bake normal to color
        private static void BakeNormalAverage(ref Color[] outColors, int[] sharedIndices, int mi, Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Color[] colors, Vector2[] uv, Texture2D widthMask, Texture2D normalMask, bool useNormalMask)
        {
            var normalAverages = NormalGatherer.GetNormalAverages(vertices, normals, meshSettings[mi].distanceThreshold);
            string message = "Run bake in " + meshSettings[mi].name;

            for(int i = 0; i < sharedIndices.Length; ++i)
            {
                int index = sharedIndices[i];
                float width = GetWidth(mi, colors, uv, index, widthMask);
                if(useNormalMask && !GetNormalMask(uv, index, normalMask))
                {
                    outColors[index] = new Color(
                        0.5f,
                        0.5f,
                        1.0f,
                        width
                    );
                    continue;
                }
                Vector3 normalAverage = NormalGatherer.GetClosestNormal(normalAverages, vertices[index]);
                Vector3 normal = normals[index];
                Vector4 tangent = tangents[index];
                Vector3 bitangent = Vector3.Cross(normal, tangent) * tangent.w;
                if(meshSettings[mi].shrinkTipStrength > 0) width *= Mathf.Pow(Mathf.Clamp01(Vector3.Dot(normal,normalAverage)), meshSettings[mi].shrinkTipStrength);
                outColors[index] = new Color(
                    Vector3.Dot(normalAverage, tangent) * 0.5f + 0.5f,
                    Vector3.Dot(normalAverage, bitangent) * 0.5f + 0.5f,
                    Vector3.Dot(normalAverage, normal) * 0.5f + 0.5f,
                    width
                );
                if(DrawProgress(message, (float)i / (float)sharedIndices.Length)) return;
            }
        }

        private static void BakeNormalMesh(ref Color[] outColors, int[] sharedIndices, int mi, Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Color[] colors, Vector2[] uv, Texture2D widthMask, Texture2D normalMask, bool useNormalMask)
        {
            if(meshSettings[mi].mesh == null)
            {
                BakeNormalEmpty(ref outColors, sharedIndices, mi, colors, uv, widthMask);
                return;
            }
            var normalOriginal = NormalGatherer.GetNormalAveragesFast(vertices, normals);
            string message = "Run bake in " + meshSettings[mi].name;

            for(int i = 0; i < sharedIndices.Length; ++i)
            {
                int index = sharedIndices[i];
                float width = GetWidth(mi, colors, uv, index, widthMask);
                if(useNormalMask && !GetNormalMask(uv, index, normalMask))
                {
                    outColors[index] = new Color(
                        0.5f,
                        0.5f,
                        1.0f,
                        width
                    );
                    continue;
                }
                Vector3 normalAverage = NormalGatherer.GetClosestNormal(normalOriginal, vertices[index]);
                Vector3 normal = normals[index];
                Vector4 tangent = tangents[index];
                Vector3 bitangent = Vector3.Cross(normal, tangent) * (tangent.w >= 0 ? 1 : -1);
                if(meshSettings[mi].shrinkTipStrength > 0) width *= Mathf.Pow(Mathf.Clamp01(Vector3.Dot(normal,normalAverage)), meshSettings[mi].shrinkTipStrength);
                outColors[index] = new Color(
                    Vector3.Dot(normalAverage, tangent) * 0.5f + 0.5f,
                    Vector3.Dot(normalAverage, bitangent) * 0.5f + 0.5f,
                    Vector3.Dot(normalAverage, normal) * 0.5f + 0.5f,
                    width
                );
                if(DrawProgress(message, (float)i / (float)sharedIndices.Length)) return;
            }
        }

        private static void BakeNormalMap(ref Color[] outColors, int[] sharedIndices, int mi, Color[] colors, Vector2[] uv, Texture2D widthMask)
        {
            string message = "Run bake in " + meshSettings[mi].name;

            for(int i = 0; i < sharedIndices.Length; ++i)
            {
                int index = sharedIndices[i];
                Color normalMap = new Color(0.5f, 0.5f, 1.0f);
                if(uv != null && uv.Length > 2 && meshSettings[mi].widthMask == null)
                {
                    Texture2D normalMapTex = meshSettings[mi].normalMap;
                    GetReadableTexture(ref normalMapTex);
                    normalMap = normalMapTex.GetPixelBilinear(uv[index].x, uv[index].y);
                }
                outColors[index] = new Color(
                    normalMap.r,
                    normalMap.g,
                    normalMap.b,
                    GetWidth(mi, colors, uv, index, widthMask)
                );
                if(DrawProgress(message, (float)i / (float)sharedIndices.Length)) return;
            }
        }

        private static void BakeNormalEmpty(ref Color[] outColors, int[] sharedIndices, int mi, Color[] colors, Vector2[] uv, Texture2D widthMask)
        {
            string message = "Run bake in " + meshSettings[mi].name;

            for(int i = 0; i < sharedIndices.Length; ++i)
            {
                int index = sharedIndices[i];
                outColors[index] = new Color(
                    0.5f,
                    0.5f,
                    1.0f,
                    GetWidth(mi, colors, uv, index, widthMask)
                );
                if(DrawProgress(message, (float)i / (float)sharedIndices.Length)) return;
            }
        }

        private static void BakeNormalKeep(ref Color[] outColors, int[] sharedIndices, int mi, Color[] colors, Vector2[] uv, Texture2D widthMask)
        {
            if(colors == null || colors.Length < 2)
            {
                BakeNormalEmpty(ref outColors, sharedIndices, mi, colors, uv, widthMask);
                return;
            }
            string message = "Run bake in " + meshSettings[mi].name;

            for(int i = 0; i < sharedIndices.Length; ++i)
            {
                int index = sharedIndices[i];
                Color color = colors[index];
                outColors[index] = new Color(
                    color.r,
                    color.g,
                    color.b,
                    GetWidth(mi, colors, uv, index, widthMask)
                );
                if(DrawProgress(message, (float)i / (float)sharedIndices.Length)) return;
            }
        }

        private static float GetWidth(int mi, Color[] colors, Vector2[] uv, int index, Texture2D widthMask)
        {
            switch(meshSettings[mi].widthBakeMode)
            {
                case WidthBakeMode.Mask:
                    if(widthMask == null) return 1.0f;
                    return widthMask.GetPixelBilinear(uv[index].x, uv[index].y).r;
                case WidthBakeMode.Red:
                    if(colors == null || colors.Length < 2) return 1.0f;
                    return colors[index].r;
                case WidthBakeMode.Green:
                    if(colors == null || colors.Length < 2) return 1.0f;
                    return colors[index].g;
                case WidthBakeMode.Blue:
                    if(colors == null || colors.Length < 2) return 1.0f;
                    return colors[index].b;
                case WidthBakeMode.Alpha:
                    if(colors == null || colors.Length < 2) return 1.0f;
                    return colors[index].a;
                case WidthBakeMode.Empty:
                    return 1.0f;
                default:
                    return 1.0f;
            }
        }

        private static bool GetNormalMask(Vector2[] uv, int index, Texture2D normalMask)
        {
            //if(normalMask == null) return true;
            return normalMask.GetPixelBilinear(uv[index].x, uv[index].y).r > 0.5f;
        }

        public static bool DrawProgress(string message, float progress)
        {
            isCancelled = isCancelled || EditorUtility.DisplayCancelableProgressBar(TEXT_WINDOW_NAME, message, progress);
            return isCancelled;
        }

        public static int[] GetOptIndices(Mesh mesh, int mi)
        {
            return mesh.GetIndices(mi).ToList().Distinct().ToArray();
        }

        //------------------------------------------------------------------------------------------------------------------------------
        // Utils
        static void GetReadableTexture(ref Texture2D tex)
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

        private static GameObject FindBakedObject()
        {
            if(gameObject.transform.parent != null)
            {
                for(int i = 0; i < gameObject.transform.parent.childCount; i++)
                {
                    GameObject childObject = gameObject.transform.parent.GetChild(i).gameObject;
                    if(childObject.name.Contains(gameObject.name + " (VertexColorBaked)"))
                    {
                        return childObject;
                    }
                }
            }

            return GameObject.Find(gameObject.name + " (VertexColorBaked)");
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

        //------------------------------------------------------------------------------------------------------------------------------
        // Languages
        private const string TEXT_WINDOW_NAME = "lilOutlineUtil";

        private static readonly string[] TEXT_LANGUAGES                 = new[] {"English", "Japanese"};

        private static readonly string[] TEXT_STEP_SELECT_MESH          = new[] {"1. Select the mesh",                          "1. メッシュを選択"};
        private static readonly string[] TEXT_STEP_SELECT_SUBMESH       = new[] {"2. Select the modify target",                 "2. 編集対象を選択"};
        private static readonly string[] TEXT_STEP_GENERATE_AND_SAVE    = new[] {"3. Generate the mesh, test it, then save",    "3. メッシュを生成・テスト・保存"};

        private static readonly string[] TEXT_WARN_OBJ_NOT_HAVE_MESH    = new[] {
            "The selected GameObject does not have a \"SkinnedMeshRenderer\" or \"MeshRenderer\".",
            "選択したGameObjectは\"SkinnedMeshRenderer\"または\"MeshRenderer\"を持っていません。"
        };
        private static readonly string[] TEXT_WARN_SELECT_FROM_SCENE    = new[] {"Please select from the scene (hierarchy)",            "シーン（ヒエラルキー）から選択してください"};
        private static readonly string[] TEXT_WARN_MESH_NOT_READABLE    = new[] {"The selected mesh is not set to \"Read/Write\" on.",  "選択されたメッシュは\"Read/Write\"がオンになっていません。"};
        private static readonly string[] TEXT_WARN_MESH_IS_EMPTY        = new[] {"The selected mesh is empty!",         "選択したメッシュは空です"};
        private static readonly string[] TEXT_WARN_MESH_HAS_NO_VERT     = new[] {"The selected mesh has no vertices!",  "選択したメッシュは頂点がありません。"};
        private static readonly string[] TEXT_WARN_MESH_HAS_NO_NORM     = new[] {"The selected mesh has no normals!",   "選択したメッシュは法線がありません。"};
        private static readonly string[] TEXT_WARN_MESH_HAS_NO_TANJ     = new[] {"The selected mesh has no tangents!",  "選択したメッシュはタンジェントがありません。"};
        private static readonly string[] TEXT_WARN_MESH_NOT_SAVED       = new[] {"Generated mesh is not saved!",        "生成されたメッシュが保存されていません。"};

        private static readonly string[] TEXT_ITEM_DD_MESH              = new[] {"Mesh (D&D from scene)",   "メッシュ (シーンからD&D)"};
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
        public static Dictionary<Vector3, Vector3> GetNormalAveragesFast(Vector3[] vertices, Vector3[] normals)
        {
            var normalAverages = new Dictionary<Vector3, Vector3>();
            string message = "Generating averages";

            for(int i = 0; i < vertices.Length; i++)
            {
                Vector3 pos = vertices[i];
                if(!normalAverages.ContainsKey(pos))
                {
                    normalAverages[pos] = normals[i];
                    continue;
                }
                normalAverages[pos] += normals[i];
                if(OutlineUtilWindow.DrawProgress(message, (float)i / (float)vertices.Length)) return normalAverages;
            }

            var keys = normalAverages.Keys.ToArray();
            for(int j = 0; j < keys.Length; j++)
            {
                normalAverages[keys[j]] = Vector3.Normalize(normalAverages[keys[j]]);
            }

            return normalAverages;
        }

        public static Dictionary<Vector3, Vector3> GetNormalAverages(Vector3[] vertices, Vector3[] normals, float threshold)
        {
            if(threshold == 0.0f) return GetNormalAveragesFast(vertices, normals);
            var normalAverages = new Dictionary<Vector3, Vector3>();
            string message = "Generating averages";

            for(int i = 0; i < vertices.Length; i++)
            {
                Vector3 pos = vertices[i];
                if(normalAverages.ContainsKey(pos)) continue;
                Vector3 average = new Vector3(0,0,0);
                for(int j = 0; j < vertices.Length; j++)
                {
                    average += Vector3.Distance(pos, vertices[j]) < threshold ? normals[j] : Vector3.zero;
                }
                normalAverages[pos] = Vector3.Normalize(average);
                if(OutlineUtilWindow.DrawProgress(message, (float)i / (float)vertices.Length)) return normalAverages;
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