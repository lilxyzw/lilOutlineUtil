以下は開発途中なので変更される可能性があります。

## 日本語
 
[ダウンロード](#)
1. unitypackageをUnityウィンドウにドラッグ＆ドロップでインポート
2. マテリアルの輪郭線（アウトライン）の設定で頂点カラーを使うようにする
3. `Window - _lil - Outline Util`からウィンドウを開く
4. シーン（Hierarchy）からアバターをD&D
5. 編集対象のメッシュを選択
6. `生成 & テスト`ボタンを押すとメッシュが生成される
7. `保存`ボタンを押して生成したメッシュを保存

生成されたメッシュは元のメッシュ（fbx）と同一階層、もしくはAssets直下のBakedMeshesフォルダに保存されます。既に保存済みである場合は上書きされます。一度保存した後は任意のフォルダに移動できます。SkinnedMeshRendererやMeshFilterのメッシュの参照先が変わった場合は別ファイルとして保存されます。

### 開発者向け情報
以下のような関数で頂点カラーからアウトラインの方向・太さをデコードできます。[使用例](https://github.com/lilxyzw/lilOutlineUtil/blob/master/Assets/Shaders/DecodeOutline.shader)

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