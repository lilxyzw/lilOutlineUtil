# lilOutlineUtil
Version 1.0.0

# 概要
頂点カラーを焼き込みアウトラインの方向を補正するスクリプトです。ハードエッジのモデルやシェーディング用に法線を調整されたモデルでの滑らかなアウトラインの実現を補助します。

# 使い方
1. unitypackageをUnityウィンドウにドラッグ＆ドロップでインポート
2. マテリアルの輪郭線（アウトライン）の設定で頂点カラーを使うようにする
3. メニューバーの`Window/_lil/Outline Util`からウィンドウを開く
4. シーン（Hierarchy）からアバターをD&D
5. 編集対象のメッシュを選択
6. `生成 & テスト`ボタンを押すとメッシュが生成される
7. `保存`ボタンを押して生成したメッシュを保存

生成されたメッシュは元のメッシュ（fbx）と同一階層、もしくはAssets直下のBakedMeshesフォルダに保存されます。既に保存済みである場合は上書きされます。一度保存した後は任意のフォルダに移動できます。SkinnedMeshRendererやMeshFilterのメッシュの参照先が変わった場合は別ファイルとして保存されます。

# 設定項目
## ベイクモード（方向）
頂点カラーのRGB値に方向の情報を格納します。形式はノーマルマップと同様で、RGBはそれぞれTangent、Bitangent、Normalに対応しています。ベイクの種類は以下の通りです。
- **近傍頂点の法線の平均**  
  ハードエッジ向けのオプションです。同一座標の法線を平均化することでアウトラインが途切れるのを防ぎます。`先端を細くする度合い`は大きくすることで先端が細くなります（正確には元の法線から乖離している度合いで細くなるようにしています）。`ベイクマスク`を指定すると黒く塗った部分は空の法線が焼き込まれアウトラインに頂点カラーを使わない状態と一致します。
- **ノーマルマップ**  
  ノーマルマップを元に法線を焼き込みます。
- **外部メッシュの参照**  
  外部メッシュを参照して焼き込みます。例えばモデルデータを複製し`Normals`のインポート設定を`Calculate`にしたメッシュを指定すると滑らかになります。顔の法線をシェーディング用に調整していてアウトラインが思ったように出ない場合に有用です。
- **空の法線**  
  空の法線を焼き込みます。アウトラインに頂点カラーを使わない状態と一致します。
- **元の頂点カラーを保持**  
  焼き込みを行わず、元の頂点カラーそのままになります。

## ベイクモード（太さ）
頂点カラーのAチャンネルに太さを格納します。マテリアル設定のアウトラインの太さにここの数値が掛け算されます。
- **マスクテクスチャ**  
  マスクテクスチャから太さを指定して焼き込みます。ここで太さを焼き込むことでマテリアル設定側でアウトラインマスクが不要になります。
- **頂点カラー (R/G/B/A)**  
  頂点カラーからコピーします。もともと頂点カラーに太さが格納されているモデルデータに使用します。
- **最大値 (1.0)**  
  太さを1.0固定で焼き込みます。つまり太さの変更をしない状態になります。

# 開発者向け情報
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