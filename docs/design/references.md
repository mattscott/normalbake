# References

Consolidated bibliography for the NormalBake research docs. Grouped by topic; each entry is cited from
the relevant document.

> Source-access note: `wiki.polycount.com`, `polycount.com`, `docs.marmoset.co`, `helpx.adobe.com`, and
> `docs.blender.org` block automated fetchers (403 / refused). Quotes in the docs were captured from
> search-index excerpts, official mirrors (Adobe Experience League), and reader proxies; the canonical
> URLs below open normally in a browser. A few figures (4096→32px padding; exact px/cm texel-density
> units) are forum-sourced and flagged in-doc.

## Foundational papers
- Krishnamurthy & Levoy, *Fitting Smooth Surfaces to Dense Polygon Meshes*, SIGGRAPH 1996. DOI 10.1145/237170.237270. https://graphics.stanford.edu/papers/surfacefitting/surf_fit.pdf
- Cohen, Olano & Manocha, *Appearance-Preserving Simplification*, SIGGRAPH 1998 (coins "normal map"). DOI 10.1145/280814.280832. https://history.siggraph.org/learning/appearance-preserving-simplification-by-cohen-olano-and-manocha/ · https://gamma.umd.edu/publication/97/
- Cignoni, Montani, Rocchini & Scopigno, *A general method for preserving attribute values on simplified meshes*, IEEE Vis 1998 (the high→low baking algorithm; nearest-point sampling). DOI 10.1109/VISUAL.1998.745285. https://ieeexplore.ieee.org/document/745285/ · https://www.academia.edu/33238585/
- Sander, Snyder, Gortler & Hoppe, *Texture Mapping Progressive Meshes*, SIGGRAPH 2001 (UV stretch & deviation). DOI 10.1145/383259.383307. https://hhoppe.com/proj/tmpm/
- Sander et al., *Signal-Specialized Parametrization*, EGSR 2002. https://www.researchgate.net/publication/220852917_Signal-Specialized_Parametrization
- Sander, Wood, Gortler, Snyder & Hoppe, *Multi-Chart Geometry Images*, SGP 2003. https://dl.acm.org/doi/10.5555/882370.882390
- Mikkelsen, *Simulation of Wrinkled Surfaces Revisited*, MSc thesis, DIKU/Univ. Copenhagen, 2008 (tangent-space standardization). http://image.diku.dk/projects/media/morten.mikkelsen.08.pdf · https://core.ac.uk/outputs/103577394/
- Mikkelsen, *Surface Gradient–Based Bump Mapping Framework*, JCGT 9(3), 2020. https://jcgt.org/published/0009/03/04/ · tutorial: https://zero-radiance.github.io/post/surface-gradient/

## Normal mapping, encoding & conventions
- Wikipedia — Normal mapping. https://en.wikipedia.org/wiki/Normal_mapping
- LearnOpenGL — Normal Mapping. https://learnopengl.com/Advanced-Lighting/Normal-Mapping
- Polycount wiki — Normal map. http://wiki.polycount.com/wiki/Normal_map
- Polycount wiki — Normal Map Technical Details. http://wiki.polycount.com/wiki/Normal_Map_Technical_Details
- Adobe — OpenGL vs DirectX normal format (Y+ vs Y−). https://experienceleague.adobe.com/en/docs/substance-3d/bakers/common-questions/what-is-the-difference-between-the-opengl-and-directx-normal-format
- Unity Shader Graph — Normal Reconstruct Z. https://docs.unity3d.com/Packages/com.unity.shadergraph@6.9/manual/Normal-Reconstruct-Z-Node.html
- NVIDIA GPU Gems 3 — *The Importance of Being Linear*. https://developer.nvidia.com/gpugems/gpugems3/part-iv-image-effects/chapter-24-importance-being-linear
- 3DCoat — Types of Normal Maps (tangent/object/world). https://3dcoat.com/documentation/2025/01/13/types-of-normal-maps/

## Tangent space & MikkTSpace
- MikkTSpace repo + `mikktspace.h` (guarantees + zlib license). https://github.com/mmikk/MikkTSpace · https://github.com/mmikk/MikkTSpace/blob/master/mikktspace.h
- Eric Lengyel — Computing Tangent Space Basis Vectors. https://terathon.com/blog/tangent-space.html
- Blender — Tangent Space Normal Maps (Mikkelsen pixel-shader decode convention). https://archive.blender.org/wiki/2015/index.php/Dev:Shading/Tangent_Space_Normal_Maps/
- Polycount — Is my tangent basis un-synced? https://polycount.com/discussion/175538/

## Baking, projection & cage
- Blender Manual — Render Baking (Selected to Active, Extrusion, Ray Distance, Cage). https://docs.blender.org/manual/en/latest/render/cycles/baking.html · https://docs.blender.org/manual/en/2.80/render/cycles/baking.html
- Blender — BakeSettings API / margin types. https://docs.blender.org/api/current/bpy.types.BakeSettings.html · https://docs.blender.org/api/current/bpy_types_enum_items/bake_margin_type_items.html
- Blender issue #96977 — use "Extend" margin for tangent-space normals. https://projects.blender.org/blender/blender/issues/96977
- Marmoset — Cage. https://docs.marmoset.co/docs/cage/ · Projection Tools. https://docs.marmoset.co/docs/projection-tools/ · Setting up a Bake Project. https://docs.marmoset.co/docs/setting-up-a-bake-project/ · Baking Tutorial. https://marmoset.co/posts/toolbag-baking-tutorial/
- Adobe Substance — Mesh map settings. https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/baking/mesh-map-settings · Matching by name. https://helpx.adobe.com/substance-3d-bake/features/matching-by-name.html
- Polycount wiki — Texture Baking. http://wiki.polycount.com/wiki/Texture_Baking
- 80.lv — How Normal Maps Work & the Baking Process. https://80.lv/articles/tutorial-how-normal-maps-work-baking-process
- Polycount — averaged normals & ray projection (waviness). https://polycount.com/discussion/81154/

## UVs, texel density & dilation
- Polycount wiki — Edge padding (px table, gutter 2×, flood-fill). http://wiki.polycount.com/wiki/Edge_padding
- Polycount wiki — Smoothing Groups; Texel. http://wiki.polycount.com/wiki/Smoothing_Groups · http://wiki.polycount.com/wiki/Texel
- Polycount — Overlapping UVs / mirrored UVs. https://polycount.com/discussion/102872/ · https://polycount.com/discussion/116922/ · https://polycount.com/discussion/169594/
- Polycount — hard edges / UVs / normal maps / vertex counts. https://polycount.com/discussion/107196/
- Texel density — beyondextent deep dive; UE4 standards. https://www.beyondextent.com/deep-dives/deepdive-texeldensity · https://polycount.com/discussion/227863/
- Adobe — Texture dilation / Padding. https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/technical-support/workflow-issues/export-issues/texture-dilation-or-padding
- RageWorks — Mip mapping and the importance of pixel padding. https://therageworks.wordpress.com/2013/02/28/mip-mapping-and-the-importance-of-pixel-padding/
- Quad Remesher FAQ / release (no finished UVs from remeshers). https://www.blendernation.com/2019/10/18/quad-remesher-auto-retopologizer-for-blender-unofficial-faq/

## Anti-aliasing & specular/mip filtering
- Adobe — Mesh map settings (Subsampling). https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/baking/mesh-map-settings
- Marmoset — Baking (Samples Per Pixel). https://marmoset.co/toolbag/baking/ · https://marmoset.co/posts/free-update-toolbag-3-05/
- Self Shadow — *Specular Showdown in the Wild West*. https://blog.selfshadow.com/2011/07/22/specular-showdown/
- Toksvig — *Mipmapping Normal Maps* (NVIDIA 2004). https://www.researchgate.net/publication/220494180_Mipmapping_Normal_Maps
- Karis — normal map filtering using vMF / Geometric Specular AA. http://graphicrants.blogspot.com/2018/05/normal-map-filtering-using-vmf-part-3.html

## Acceleration / ray tracing
- PBRT-v4 — Bounding Volume Hierarchies. https://pbr-book.org/4ed/Primitives_and_Intersection_Acceleration/Bounding_Volume_Hierarchies
- Bikker — How to Build a BVH (pt.2 faster rays, pt.3 quick builds). https://jacco.ompf2.com/2022/04/18/how-to-build-a-bvh-part-2-faster-rays/ · https://jacco.ompf2.com/2022/04/21/how-to-build-a-bvh-part-3-quick-builds/
- Wald — *On fast Construction of SAH-based BVHs*, 2007. https://www.sci.utah.edu/~wald/Publications/2007/ParallelBVHBuild/fastbuild.pdf
- Karras — *Maximizing Parallelism in BVH/Octree/k-d Construction*, HPG 2012. https://research.nvidia.com/sites/default/files/pubs/2012-06_Maximizing-Parallelism-in/karras2012hpg_paper.pdf
- Stich, Friedrich & Dietrich — *Spatial Splits in BVHs* (SBVH), HPG 2009. https://dl.acm.org/doi/10.1145/1572769.1572771
- Möller & Trumbore — *Fast, Minimum Storage Ray-Triangle Intersection*, 1997. https://www.graphics.cornell.edu/pubs/1997/MT97.html
- Woop, Benthin & Wald — *Watertight Ray/Triangle Intersection*, JCGT 2013. https://jcgt.org/published/0002/01/05/
- Wald et al. — *Embree: Efficient Ray Tracing Kernels*, JCGT 2015. https://jcgt.org/published/0004/04/05/
- .NET 8 Hardware Intrinsics. https://devblogs.microsoft.com/dotnet/dotnet-8-hardware-intrinsics/ · https://learn.microsoft.com/dotnet/api/system.runtime.intrinsics

## Texture compression & formats
- Microsoft — Block Compression (BCn) / BC7. https://learn.microsoft.com/en-us/windows/win32/direct3d10/d3d10-graphics-programming-guide-resources-block-compression · https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format
- Nathan Reed — Understanding BCn Texture Compression Formats. https://www.reedbeta.com/blog/understanding-bcn-texture-compression-formats/
- NVIDIA Texture Tools — Normal Map Compression. https://github.com/castano/nvidia-texture-tools/wiki/NormalMapCompression
- 3Dc / BC5 background. https://en.wikipedia.org/wiki/3Dc

## Tools
- xNormal. https://xnormal.net/
- Marmoset Toolbag — Tangents & Handedness; Map Types; Tips & Tricks. https://docs.marmoset.co/docs/tangent-handedness/ · https://docs.marmoset.co/docs/map-types/ · https://docs.marmoset.co/docs/tips-tricks/
- Adobe Substance 3D — Bakers; sbsbaker CLI. https://helpx.adobe.com/substance-3d-bake/home.html · https://substance3d.adobe.com/documentation/sat/command-line-tools/sbsbaker/sbsbaker-command-line-options
- Knald. https://docs.knaldtech.com/doku.php?id=normal_maps_knald_1.1
- Handplane. https://handplane3d.com/
- SideFX Houdini — Labs Games Baker. https://www.sidefx.com/docs/houdini/nodes/out/labs--games_baker-2.0.html
- Maxon ZBrush — Map Generation. https://www.maxon.net/en/zbrush/features/map-generation

## Libraries (license-checked)
- Embree (Apache-2.0, native). https://github.com/RenderKit/embree · https://github.com/RenderKit/embree/blob/master/LICENSE.txt
- xatlas (MIT, native). https://github.com/jpcy/xatlas
- Microsoft UVAtlas (MIT, native). https://github.com/microsoft/UVAtlas
- MikkTSpace (zlib, native C). https://github.com/mmikk/MikkTSpace
- BCnEncoder.NET (MIT/Unlicense, managed). https://github.com/Nominom/BCnEncoder.NET
- StbImageSharp / StbImageWriteSharp (PublicDomain/MIT, managed). https://github.com/StbSharp/StbImageSharp · https://github.com/StbSharp/StbImageWriteSharp
- Pfim (MIT, managed; DDS/TGA decode). https://github.com/nickbabcock/Pfim
- Jither.OpenEXR (MIT, managed). https://github.com/Jither/Jither.OpenEXR
- SixLabors ImageSharp (Six Labors Split License — note commercial terms). https://github.com/SixLabors/ImageSharp/blob/main/LICENSE · https://sixlabors.com/pricing/

## Testing / image comparison
- NVlabs FLIP (BSD-3-Clause). https://github.com/NVlabs/flip · https://dl.acm.org/doi/10.1145/3406183
- pbrt-v4 CI testing (thresholded reference diffs). https://github.com/mmp/pbrt-v4-ci-testing
- Imatest — SSIM/PSNR. https://www.imatest.com/docs/ssim/
