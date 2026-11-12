Jannah Garden - optimised assets for Unity
==========================================
One folder per asset. Source Meshy folders were not changed.

Each folder:
  <Name>.fbx                      LOD0 + LOD1 + LOD2 in one file (Unity builds the LOD Group automatically from the _LOD0/_LOD1/_LOD2 names)
  <Name>_BaseColor.png            LOD0 textures (1024)
  <Name>_Normal.png
  <Name>_MetallicSmoothness.png   R = metallic, A = smoothness (URP Lit / Standard layout)
  <Name>_Emission.png             only on assets that glow
  <Name>_LOD1_*.png / _LOD2_*.png smaller texture sets for the far LODs (512 / 256)

Triangle budgets
  Solid props:          LOD0 20k, LOD1 7k, LOD2 2.5k
  Foliage / many parts: LOD0 50k, LOD1 18k, LOD2 6k
  (originals were ~1.9M triangles each)

Pivot is at the bottom centre, scale is 1 unit = 1 metre, Y up, no rotation on import.

In Unity
  1. Select all *_Normal.png      -> Texture Type: Normal map -> Apply
  2. Select all *_MetallicSmoothness.png -> untick sRGB (Color Texture) -> Apply
  3. On each material: Metallic Map = _MetallicSmoothness, Smoothness Source = Metallic Alpha;
     if there is an _Emission.png, tick Emission and assign it.
  4. LOD switch distances: on the prefab's LOD Group, drag the bars to taste.

_optimize_report.json lists every asset with its source folder and final triangle counts.
