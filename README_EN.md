# lilOutlineUtil
Version 1.0.0

# Overview
This script corrects the outline direction by baking in vertex colors. It assists in achieving smooth outlines on hard-edged models and models with normals adjusted for shading.

# Usage
1. Drag and drop unitypackage to the Unity window to import it.
2. Use vertex color in material outline settings.
3. Click `Window/_lil/Outline Util` in the top menu bar.
4. Select an avatar from the hierarchy.
5. Select mesh to edit.
6. Press the `Generate & Test` button to generate the mesh.
7. Save the generated mesh by pressing the `save` button.

The generated mesh will be saved in the same hierarchy as the original mesh (fbx) or in the `BakedMeshes` folder directly under Assets. If the mesh has already been saved, it will be overwritten. Once saved, it can be moved to any folder. If the mesh reference in `SkinnedMeshRenderer` or `MeshFilter` is changed, it will be saved as a separate file.

# Settings
## Bake Mode (Normal)
Stores outline normal to the RGB value of the vertex color. The format is similar to that of a normal map, with RGB corresponding to Tangent, Bitangent, and Normal, respectively. The types of baking are as follows.
- **Average of close vertices**  
  This option is for hard edge models. Prevents outline breaks by averaging normals at the same coordinates. The `Shlink the tip` can be increased to make the tip thinner (or more precisely, to make it thinner by the degree of deviation from the original normal). If `Bake mask` is specified, the blackened areas will have empty normals baked in, consistent with not using vertex colors for outlines.
- **Normal map**  
  Burn in normals based on the normal map.
- **From other mesh**  
  Bake by referencing an external mesh. For example, you can duplicate the model data and specify a mesh with the import setting of `Normals` set to `Calculate` to make it smooth. This is useful if you are adjusting facial normals for shading and the outlines do not come out as expected.
- **Bake empty normal**  
  Bakes in the empty normals. This is consistent with not using vertex color for outlines.
- **Keep vertex color**  
  No baking is performed, and the original vertex color remains the same.

## Bake Mode (Width)
Stores the thickness in the A channel of the vertex color. The outline thickness in the material settings is multiplied by the value here.
- **Mask texture**  
  Specify the thickness in the mask texture and bake it in. Baking in the thickness here eliminates the need for an outline mask in the material settings.
- **Vertex Color (R/G/B/A)**  
  Copies the vertex color. Used for model data where the thickness is originally stored in the vertex color.
- **Maximum value (1.0)**  
  The maximum value is baked in. In other words, the thickness is not changed.


# Developer Information
The following functions can be used to decode outline direction and thickness from vertex color. [Example](https://github.com/lilxyzw/lilOutlineUtil/blob/master/Assets/Shaders/DecodeOutline.shader)

```HLSL
// object space
float3 CalcOutlineVectorOS(float4 color, float3 normalOS, float4 tangentOS)
{
    float3 bitangentOS = normalize(cross(normalOS, tangentOS.xyz)) * (tangentOS.w * length(normalOS));
    float3 outlineVectorTS = color.rgb * 2.0 - 1.0;
    float3 outlineVector = outlineVectorTS.x * tangentOS.xyz + outlineVectorTS.y * bitangentOS + outlineVectorTS.z * normalOS;
    return outlineVector * color.a;
}

// world space
float3 CalcOutlineVectorWS(float4 color, float3 normalOS, float4 tangentOS)
{
    float3 normalWS = UnityObjectToWorldNormal(normalOS);
    float3 tangentWS = UnityObjectToWorldDir(tangentOS.xyz);
    float3 bitangentWS = cross(normalWS, tangentWS.xyz) * tangentOS.w * unity_WorldTransformParams.w;

    float3 outlineVectorTS = color.rgb * 2.0 - 1.0;
    float3 outlineVector = outlineVectorTS.x * tangentWS.xyz + outlineVectorTS.y * bitangentWS + outlineVectorTS.z * normalWS;
    return outlineVector * color.a;
}
```