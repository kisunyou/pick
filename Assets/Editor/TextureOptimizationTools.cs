using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

public static class TextureOptimizationTools
{
    const string Output = ".utmp/texture_optimization";

    [Serializable] public class Entry
    {
        public string path, type, shape, format, androidFormat, compression;
        public int width, height, mipCount, maxSize, quality;
        public bool readable, overridden, alpha, srgb;
        public long runtimeBytes, sourceBytes;
    }
    [Serializable] public class AuditReport
    {
        public string target;
        public Entry[] textures;
    }

    [Serializable] public class Change
    {
        public string path, status, guid, sourceHash;
        public Entry before, after;
        public double compositePsnr, alphaRmse;
    }

    [Serializable] public class OptimizationReport
    {
        public string note = "Android encoded texture payload estimate, including mipmaps. Not APK download size or measured device memory. Source files and dimensions preserved.";
        public long beforePayloadBytes, afterPayloadBytes;
        public List<Change> changes = new List<Change>();
    }

    static bool IsCandidate(Entry entry) => entry.shape == "Texture2D" && entry.srgb && !entry.readable &&
        (entry.type == "Sprite" || entry.type == "Default") &&
        (entry.androidFormat == "RGBA32" || entry.androidFormat == "RGB24" ||
         (entry.androidFormat == "DXT1" && entry.path.StartsWith("Assets/Resources/Model/Materials/cubemap_sky_")));

    static long Payload(Entry entry)
    {
        int block = entry.androidFormat == "ASTC_6x6" ? 6 : 4;
        int bytes = entry.androidFormat == "DXT1" || entry.androidFormat == "ETC2_RGB4" ? 8 : 16;
        int pixelBytes = entry.androidFormat == "RGBA32" ? 4 : entry.androidFormat == "RGB24" ? 3 : entry.androidFormat == "Alpha8" ? 1 : 0;
        if (pixelBytes == 0 && !new[] { "ASTC_6x6", "ASTC_4x4", "DXT1", "ETC2_RGB4" }.Contains(entry.androidFormat))
            throw new Exception("Unknown payload format: " + entry.androidFormat);
        long total = 0;
        int width = entry.width, height = entry.height;
        for (int mip = 0; mip < entry.mipCount; mip++)
        {
            total += pixelBytes > 0 ? (long)width * height * pixelBytes : (long)((width + block - 1) / block) * ((height + block - 1) / block) * bytes;
            width = Mathf.Max(1, width / 2);
            height = Mathf.Max(1, height / 2);
        }
        return total;
    }

    static string Hash(string path)
    {
        using (var hash = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
    }

    static Texture2D Snapshot(string path)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var active = RenderTexture.active;
        bool srgbWrite = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = true;
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            var result = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            result.Apply();
            return result;
        }
        finally
        {
            GL.sRGBWrite = srgbWrite;
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    static void Compare(Texture2D before, Texture2D after, Change change)
    {
        var a = before.GetPixels32();
        var b = after.GetPixels32();
        double error = 0, alphaError = 0;
        int count = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].a == 0 && b[i].a == 0) continue;
            double aa = a[i].a / 255d, ba = b[i].a / 255d;
            double r = a[i].r * aa - b[i].r * ba;
            double g = a[i].g * aa - b[i].g * ba;
            double blue = a[i].b * aa - b[i].b * ba;
            double white = b[i].a - a[i].a;
            // Test compositing over both black and white, including transparent edge artifacts.
            error += r * r + g * g + blue * blue + (r + white) * (r + white) +
                (g + white) * (g + white) + (blue + white) * (blue + white);
            alphaError += white * white;
            count++;
        }
        if (count == 0) throw new Exception("Unexpected blank texture: " + change.path);
        change.compositePsnr = error == 0 ? 99 : 10 * Math.Log10(255d * 255 * count * 6 / error);
        change.alphaRmse = Math.Sqrt(alphaError / count);
    }

    [MenuItem("Tools/Texture Optimization/Optimize Android without resizing")]
    public static void Optimize()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new Exception("Use Edit Mode with Android as the active target.");
        Directory.CreateDirectory(Output + "/backup");
        var before = IncludedTextures().Select(Inspect).ToArray();
        var report = new OptimizationReport { beforePayloadBytes = before.Sum(Payload) };
        File.WriteAllText(Output + "/before.json", JsonUtility.ToJson(new AuditReport { target = "Android", textures = before }, true));
        foreach (var entry in before.Where(IsCandidate))
        {
            string guid = AssetDatabase.AssetPathToGUID(entry.path);
            byte[] meta = File.ReadAllBytes(entry.path + ".meta");
            File.WriteAllBytes(Output + "/backup/" + guid + ".meta", meta);
            var change = new Change { path = entry.path, guid = guid, before = entry, sourceHash = Hash(entry.path) };
            var original = Snapshot(entry.path);
            Texture2D optimized = null;
            try
            {
                File.WriteAllBytes(Output + "/" + guid + "_before.png", original.EncodeToPNG());
                var importer = (TextureImporter)AssetImporter.GetAtPath(entry.path);
                var settings = importer.GetPlatformTextureSettings("Android");
                settings.name = "Android";
                settings.overridden = true;
                settings.maxTextureSize = entry.maxSize;
                settings.format = entry.androidFormat == "DXT1" ? TextureImporterFormat.ASTC_6x6 : TextureImporterFormat.ASTC_4x4;
                settings.compressionQuality = 100;
                settings.crunchedCompression = false;
                importer.SetPlatformTextureSettings(settings);
                importer.SaveAndReimport();
                change.after = Inspect(entry.path);
                if (change.after.width != entry.width || change.after.height != entry.height || change.after.mipCount != entry.mipCount ||
                    change.sourceHash != Hash(entry.path) || guid != AssetDatabase.AssetPathToGUID(entry.path))
                    throw new Exception("Source, dimensions, mipmaps or GUID changed: " + entry.path);
                optimized = Snapshot(entry.path);
                Compare(original, optimized, change);
                File.WriteAllBytes(Output + "/" + guid + "_after.png", optimized.EncodeToPNG());
                if (change.compositePsnr < (entry.androidFormat == "DXT1" ? 38 : 40) || change.alphaRmse > 1.5 || Payload(change.after) >= Payload(entry))
                {
                    File.WriteAllBytes(entry.path + ".meta", meta);
                    AssetDatabase.ImportAsset(entry.path, ImportAssetOptions.ForceSynchronousImport);
                    change.after = Inspect(entry.path);
                    change.status = "Kept original: quality or size guard";
                }
                else change.status = "Optimized";
                report.changes.Add(change);
                Debug.Log("TEXTURE_OPTIMIZED: " + change.status + " " + entry.path + " PSNR=" + change.compositePsnr);
            }
            catch
            {
                File.WriteAllBytes(entry.path + ".meta", meta);
                AssetDatabase.ImportAsset(entry.path, ImportAssetOptions.ForceSynchronousImport);
                throw;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(original);
                if (optimized != null) UnityEngine.Object.DestroyImmediate(optimized);
                report.afterPayloadBytes = IncludedTextures().Select(Inspect).Sum(Payload);
                File.WriteAllText(Output + "/result.json", JsonUtility.ToJson(report, true));
            }
        }
        Audit();
        Debug.Log("TEXTURE_OPTIMIZATION_COMPLETE");
    }

    static string[] IncludedTextures()
    {
        var roots = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") &&
            p.Contains("/Resources/") && !p.Contains("/Editor/") && !AssetDatabase.IsValidFolder(p))
            .Concat(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path))
            .Concat(PlayerSettings.GetPreloadedAssets().Where(a => a != null).Select(AssetDatabase.GetAssetPath))
            .Distinct().ToArray();
        return AssetDatabase.GetDependencies(roots, true)
            .Where(p => p.StartsWith("Assets/") && !p.Contains("/Editor/") && AssetImporter.GetAtPath(p) is TextureImporter)
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    static Entry Inspect(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var platform = importer.GetPlatformTextureSettings("Android");
        var texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
        var t2d = texture as Texture2D;
        return new Entry
        {
            path = path, type = importer.textureType.ToString(), shape = importer.textureShape.ToString(),
            format = t2d != null ? t2d.format.ToString() : texture.graphicsFormat.ToString(),
            androidFormat = (platform.overridden && platform.format != TextureImporterFormat.Automatic
                ? platform.format : importer.GetAutomaticFormat("Android")).ToString(),
            compression = (platform.overridden ? platform.textureCompression : importer.textureCompression).ToString(),
            width = texture.width, height = texture.height, mipCount = t2d != null ? t2d.mipmapCount : 1,
            maxSize = platform.overridden ? platform.maxTextureSize : importer.maxTextureSize,
            quality = platform.overridden ? platform.compressionQuality : importer.compressionQuality,
            readable = importer.isReadable, overridden = platform.overridden,
            alpha = importer.DoesSourceTextureHaveAlpha(), srgb = importer.sRGBTexture,
            runtimeBytes = Profiler.GetRuntimeMemorySizeLong(texture), sourceBytes = new FileInfo(path).Length
        };
    }

    [MenuItem("Tools/Texture Optimization/Audit Android textures")]
    public static void Audit()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit Mode.");
        Directory.CreateDirectory(Output);
        var report = new AuditReport { target = EditorUserBuildSettings.activeBuildTarget.ToString(),
            textures = IncludedTextures().Select(Inspect).ToArray() };
        File.WriteAllText(Output + "/audit.json", JsonUtility.ToJson(report, true));
        Debug.Log("TEXTURE_AUDIT_COMPLETE: " + report.textures.Length);
    }

    [MenuItem("Tools/Texture Optimization/Verify optimization")]
    public static void Verify()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new Exception("Use Edit Mode with Android as the active target.");
        var report = JsonUtility.FromJson<OptimizationReport>(File.ReadAllText(Output + "/result.json"));
        var before = JsonUtility.FromJson<AuditReport>(File.ReadAllText(Output + "/before.json"));
        var current = IncludedTextures().Select(Inspect).ToArray();
        if (!before.textures.Select(e => e.path).SequenceEqual(current.Select(e => e.path)))
            throw new Exception("Texture dependency set changed.");
        foreach (var entry in before.textures)
        {
            var after = current.Single(e => e.path == entry.path);
            if (entry.width != after.width || entry.height != after.height || entry.mipCount != after.mipCount ||
                entry.readable != after.readable || entry.srgb != after.srgb || entry.maxSize != after.maxSize)
                throw new Exception("Texture layout/settings changed: " + entry.path);
            if (!report.changes.Any(c => c.path == entry.path && c.status == "Optimized") && entry.androidFormat != after.androidFormat)
                throw new Exception("Non-selected texture compression changed: " + entry.path);
        }
        foreach (var change in report.changes)
        {
            if (change.guid != AssetDatabase.AssetPathToGUID(change.path) || change.sourceHash != Hash(change.path))
                throw new Exception("Source content or reference changed: " + change.path);
            if (change.status == "Optimized" && (change.compositePsnr < 38 || change.alphaRmse > 1.5))
                throw new Exception("Quality guard failed: " + change.path);
        }
        if (current.Sum(Payload) != report.afterPayloadBytes || report.afterPayloadBytes >= report.beforePayloadBytes)
            throw new Exception("Payload reduction mismatch.");
        TitleBrandingBuilder.Validate();
        LogoIntroChecks.Run();
        File.WriteAllText(Output + "/verification.txt", "PASS: " + current.Length + " texture dimensions/mipmaps/settings; source SHA256 and GUIDs; quality guards; payload reduction; 4 localized logos x 3 viewports; logo animation/input regressions. Device QA and APK download size not measured.\n");
        Debug.Log("TEXTURE_OPTIMIZATION_VERIFIED");
    }
}
