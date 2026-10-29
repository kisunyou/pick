# Android Texture Optimization - 2026-09-13

## Scope and Result

- Inspected 327 TextureImporter assets reachable from enabled build scenes, Resources, and preloaded assets.
- Resources includes build-resident assets, not necessarily textures simultaneously displayed in gameplay.
- Changed Android importer overrides for 10 textures. Original image bytes, GUIDs, imported dimensions, mipmaps, filtering and sprite layout were preserved.
- Estimated encoded texture payload including mipmaps: 41,809,056 -> 34,540,752 bytes (39.87 -> 32.94 MiB).
- Reduction: 7,268,304 bytes / 6.93 MiB / 17.38% of the audited texture payload.
- This is NOT an APK/AAB download-size measurement, nor a measurement of device RAM. Source PNG/TGA sizes in the repository are unchanged.
- Font atlases embedded in .asset files, render textures, and embedded model textures are outside this TextureImporter estimate.

## Changes

All changed textures use Android compression quality 100. No texture resolution was reduced.

| Texture | Old Android format | New Android format | Composite PSNR |
| --- | --- | --- | --- |
| cubemap_sky_BK.tga | DXT1 | ASTC 6x6 | 45.33 dB |
| cubemap_sky_DN.tga | DXT1 | ASTC 6x6 | 46.90 dB |
| cubemap_sky_FR.tga | DXT1 | ASTC 6x6 | 45.97 dB |
| cubemap_sky_LF.tga | DXT1 | ASTC 6x6 | 45.59 dB |
| cubemap_sky_RT.tga | DXT1 | ASTC 6x6 | 45.98 dB |
| cubemap_sky_UP.tga | DXT1 | ASTC 6x6 | 49.49 dB |
| CornerPanelRadius_0px_Fill.png | RGB24 | ASTC 4x4 | Identical sampled pixels |
| titleLogo_takepick_en.png | RGBA32 | ASTC 4x4 | 46.67 dB |
| titleLogo_takepick_ja.png | RGBA32 | ASTC 4x4 | 44.97 dB |
| titleLogo_takepick_th.png | RGBA32 | ASTC 4x4 | 45.19 dB |

Sky textures are under `Assets/Resources/Model/Materials/`; logos are under `Assets/Resources/UI2/`; the panel is under `Assets/Resources/UI2/Images/UIBuilder/`.

## Quality Protection

- Most textures (312 before optimization), including doll textures, already used ASTC 6x6. Their settings were left unchanged.
- The Korean logo `titleLogo_bbop.png` was tested, then its exact previous importer metadata was restored: alpha RMSE 1.62/255 exceeded the conservative 1.5/255 guard.
- Accepted logos have alpha RMSE 0.92-1.21/255. Sky alpha is unchanged.
- Comparison uses same-resolution Unity render/readback before and after import, composited against black and white; fully transparent pixels are excluded. These metrics do not guarantee imperceptibility on every display.
- The guard requires at least 40 dB for uncompressed UI candidates, 38 dB for previously DXT1 sky candidates, and a positive payload reduction.
- Normal maps, linear/data textures, readable textures, single-channel textures, and already compressed textures other than the six legacy sky overrides are excluded.

## Reproduction and Remaining Checks

- Editor tool: `Assets/Editor/TextureOptimizationTools.cs`.
- Menus: `Tools/Texture Optimization/Audit Android textures`, `Optimize Android without resizing`, and `Verify optimization` (Android target, Edit Mode).
- Local evidence: `.utmp/texture_optimization/before.json`, `result.json`, `audit.json`, `verification.txt`, per-texture before/after PNGs and metadata backups. Optimize regenerates this local run evidence; archive it before another run if needed.
- Validation checks source SHA256/GUID preservation, dimensions/mipmap/settings preservation, format changes limited to accepted candidates, payload reduction, localized title renders, and logo startup/input regressions.
- Test the next Android build on a physical device, particularly the sky gradients and localized logo edges. APK/AAB packaging compression can produce a different download-size saving.
- ASTC is supported by most modern Android GPUs; unsupported devices can decompress it and use more memory. The project already used ASTC broadly before this change. See [Unity 6.3 GPU format guidance](https://docs.unity3d.com/6000.3/Documentation/Manual/texture-choose-format-by-platform.html).
