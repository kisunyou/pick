using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

[System.Serializable]
public class RenderPixelatedFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Range(1, 16)] public int pixelSize = 6;
        [Range(0f, 1f)] public float normalEdgeStrength = 0.3f;
        [Range(0f, 1f)] public float depthEdgeStrength = 0.4f;
    }

    public Settings settings = new Settings();
    private RenderPixelatedPass _pass;

    public override void Create()
    {
        _pass = new RenderPixelatedPass();
        _pass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var t = renderingData.cameraData.cameraType;
        if (t == CameraType.Preview || t == CameraType.Reflection) return;
        _pass.Setup(settings);
        renderer.EnqueuePass(_pass);
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        // DepthNormals prepass 요청 → _CameraNormalsTexture 생성
        if (settings.normalEdgeStrength > 0f)
            _pass.ConfigureInput(ScriptableRenderPassInput.Normal);

        // Depth 도 명시적으로 요청
        _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    protected override void Dispose(bool disposing) => _pass?.Dispose();

    // ═══════════════════════════════════════════════════════════════
    class RenderPixelatedPass : ScriptableRenderPass
    {
        private Settings _settings;
        private Material _mat;

        private static readonly int s_MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int s_DepthTex = Shader.PropertyToID("_DepthTex");
        private static readonly int s_NormalTex = Shader.PropertyToID("_NormalTex");
        private static readonly int s_Resolution = Shader.PropertyToID("_Resolution");
        private static readonly int s_NormalStrength = Shader.PropertyToID("_NormalEdgeStrength");
        private static readonly int s_DepthStrength = Shader.PropertyToID("_DepthEdgeStrength");

        private static readonly int s_TempBeauty = Shader.PropertyToID("_TempBeautyRT");
        private static readonly int s_TempOut = Shader.PropertyToID("_TempOutRT");

        private class DownsampleData { public TextureHandle src; public TextureHandle dst; }
        private class CompositeData
        {
            public Material mat;
            public TextureHandle beauty;
            public TextureHandle depth;
            public TextureHandle normal;
            public bool hasNormal;
            public Vector4 resolution;
            public float normalStrength;
            public float depthStrength;
        }

        public RenderPixelatedPass()
        {
            var sh = Shader.Find("Custom/URP/RenderPixelated");
            if (sh == null) { Debug.LogError("[RenderPixelated] 셰이더 없음"); return; }
            _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            Debug.Log("[RenderPixelated] 셰이더 로드 성공");
        }

        public void Setup(Settings s) => _settings = s;

        // ── RenderGraph 경로 ──────────────────────────────────────
        public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
        {
            if (_mat == null || _settings == null) return;

            var res = frameData.Get<UniversalResourceData>();
            var camData = frameData.Get<UniversalCameraData>();
            var desc = camData.cameraTargetDescriptor;
            if (!res.activeColorTexture.IsValid()) return;

            int rw = Mathf.Max(1, desc.width / _settings.pixelSize);
            int rh = Mathf.Max(1, desc.height / _settings.pixelSize);

            _mat.SetVector(s_Resolution, new Vector4(rw, rh, 1f / rw, 1f / rh));
            _mat.SetFloat(s_NormalStrength, _settings.normalEdgeStrength);
            _mat.SetFloat(s_DepthStrength, _settings.depthEdgeStrength);

            var beautyDesc = new RenderTextureDescriptor(rw, rh, desc.colorFormat, 0)
            { useMipMap = false, msaaSamples = 1 };
            TextureHandle beautyHandle = UniversalRenderer.CreateRenderGraphTexture(
                rg, beautyDesc, "_PixelBeautyRT", false, FilterMode.Point);

            bool hasDepth = res.cameraDepthTexture.IsValid();
            bool hasNormal = res.cameraNormalsTexture.IsValid();
            TextureHandle depthHandle = hasDepth ? res.cameraDepthTexture : beautyHandle;
            TextureHandle normalHandle = hasNormal ? res.cameraNormalsTexture : beautyHandle;

            using (var builder = rg.AddRasterRenderPass<DownsampleData>("PixelDownsample", out var pd))
            {
                pd.src = res.activeColorTexture;
                pd.dst = beautyHandle;
                builder.UseTexture(pd.src, AccessFlags.Read);
                builder.SetRenderAttachment(pd.dst, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((DownsampleData d, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, d.src, new Vector4(1f, 1f, 0f, 0f), 0f, false));
            }

            using (var builder = rg.AddRasterRenderPass<CompositeData>("PixelComposite", out var cd))
            {
                cd.mat = _mat;
                cd.beauty = beautyHandle;
                cd.depth = depthHandle;
                cd.normal = normalHandle;
                cd.hasNormal = hasNormal;
                cd.resolution = new Vector4(rw, rh, 1f / rw, 1f / rh);
                cd.normalStrength = _settings.normalEdgeStrength;
                cd.depthStrength = _settings.depthEdgeStrength;

                builder.UseTexture(cd.beauty, AccessFlags.Read);
                builder.UseTexture(cd.depth, AccessFlags.Read);
                if (cd.hasNormal)
                    builder.UseTexture(cd.normal, AccessFlags.Read);
                builder.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((CompositeData d, RasterGraphContext ctx) =>
                {
                    d.mat.SetTexture(s_MainTex, d.beauty);
                    d.mat.SetTexture(s_DepthTex, d.depth);
                    d.mat.SetTexture(s_NormalTex, d.normal);
                    d.mat.SetVector(s_Resolution, d.resolution);
                    d.mat.SetFloat(s_NormalStrength, d.normalStrength);
                    d.mat.SetFloat(s_DepthStrength, d.depthStrength);
                    Blitter.BlitTexture(ctx.cmd, d.beauty, new Vector4(1f, 1f, 0f, 0f), d.mat, 0);
                });
            }
        }

        // ── Compatibility Mode Execute 경로 ───────────────────────
#pragma warning disable CS0672
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
#pragma warning restore CS0672
        {
            if (_mat == null || _settings == null) return;

            var cmd = CommandBufferPool.Get("RenderPixelatedPass");
            var desc = renderingData.cameraData.cameraTargetDescriptor;

            int fullW = desc.width;
            int fullH = desc.height;
            int rw = Mathf.Max(1, fullW / _settings.pixelSize);
            int rh = Mathf.Max(1, fullH / _settings.pixelSize);

            var beautyDesc = desc;
            beautyDesc.width = rw;
            beautyDesc.height = rh;
            beautyDesc.depthBufferBits = 0;
            beautyDesc.msaaSamples = 1;
            cmd.GetTemporaryRT(s_TempBeauty, beautyDesc, FilterMode.Point);

            var outDesc = desc;
            outDesc.depthBufferBits = 0;
            outDesc.msaaSamples = 1;
            cmd.GetTemporaryRT(s_TempOut, outDesc, FilterMode.Bilinear);

            // BuiltinRenderTextureType.CameraTarget : deprecated API 없이
            // 카메라 컬러를 참조하는 가장 안전한 방법
            RenderTargetIdentifier cameraColor = BuiltinRenderTextureType.CameraTarget;
            RenderTargetIdentifier beautyID = new RenderTargetIdentifier(s_TempBeauty);
            RenderTargetIdentifier outID = new RenderTargetIdentifier(s_TempOut);

            cmd.Blit(cameraColor, beautyID);

            Texture depthTex = Shader.GetGlobalTexture("_CameraDepthTexture") ?? Texture2D.whiteTexture;
            Texture normalTex = Shader.GetGlobalTexture("_CameraNormalsTexture") ?? Texture2D.whiteTexture;

            cmd.SetGlobalTexture(s_MainTex, beautyID);
            cmd.SetGlobalTexture(s_DepthTex, depthTex);
            cmd.SetGlobalTexture(s_NormalTex, normalTex);
            _mat.SetVector(s_Resolution, new Vector4(rw, rh, 1f / rw, 1f / rh));
            _mat.SetFloat(s_NormalStrength, _settings.normalEdgeStrength);
            _mat.SetFloat(s_DepthStrength, _settings.depthEdgeStrength);

            cmd.Blit(beautyID, outID, _mat, 0);
            cmd.Blit(outID, cameraColor);

            cmd.ReleaseTemporaryRT(s_TempBeauty);
            cmd.ReleaseTemporaryRT(s_TempOut);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            if (_mat != null) CoreUtils.Destroy(_mat);
        }
    }
}
