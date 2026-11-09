using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SlaydGames.Mist
{
    /// <summary>
    /// The mist frame (section 4): ground shade -> march -> temporal -> blur -> depth-aware upsample and
    /// composite. Each stage is one fullscreen raster pass in the render graph, all at the mist target's
    /// resolution except the ground shade and the composite, which run at camera resolution.
    /// </summary>
    public sealed class MistRenderPass : ScriptableRenderPass, IDisposable
    {
        public const string ShaderName = "Hidden/SlaydGames/Mist";

        // Pass indices in Mist.shader.
        const int kPassGroundShade = 0;
        const int kPassMarch = 1;
        const int kPassTemporal = 2;
        const int kPassBlurH = 3;
        const int kPassBlurV = 4;
        const int kPassComposite = 5;

        const int kPruneEvery = 300;       // frames between sweeps for dead camera states
        const int kStateLifetime = 600;    // frames a camera state survives unused

        static class Ids
        {
            public static readonly int Volumes = Shader.PropertyToID("_MistVolumes");
            public static readonly int Lights = Shader.PropertyToID("_MistLights");
            public static readonly int Clears = Shader.PropertyToID("_MistClears");
            public static readonly int VolumeCount = Shader.PropertyToID("_MistVolumeCount");
            public static readonly int ShadeCount = Shader.PropertyToID("_MistShadeCount");
            public static readonly int LightCount = Shader.PropertyToID("_MistLightCount");
            public static readonly int ClearCount = Shader.PropertyToID("_MistClearCount");
            public static readonly int Noise = Shader.PropertyToID("_MistNoise");
            public static readonly int Depth = Shader.PropertyToID("_MistDepth");
            public static readonly int Source = Shader.PropertyToID("_MistSource");
            public static readonly int Source2 = Shader.PropertyToID("_MistSource2");
            public static readonly int InvViewProj = Shader.PropertyToID("_MistInvViewProj");
            public static readonly int PrevViewProj = Shader.PropertyToID("_MistPrevViewProj");
            public static readonly int Params0 = Shader.PropertyToID("_MistParams0");
            public static readonly int Params1 = Shader.PropertyToID("_MistParams1");
            public static readonly int SunDir = Shader.PropertyToID("_MistSunDir");
            public static readonly int SunColor = Shader.PropertyToID("_MistSunColor");
            public static readonly int Ambient = Shader.PropertyToID("_MistAmbient");
            public static readonly int TargetSize = Shader.PropertyToID("_MistTargetSize");
        }

        /// <summary>What the pass remembers about a camera between frames: the temporal history and last frame's view.</summary>
        sealed class CameraState
        {
            public readonly RTHandle[] history = new RTHandle[2];
            public int writeIndex;
            public bool historyValid;
            public Matrix4x4 prevViewProj;
            public Vector3 prevPosition;
            public Quaternion prevRotation = Quaternion.identity;
            public int frame;
            public int lastUsedFrame;

            public void ReleaseHistory()
            {
                for (int i = 0; i < history.Length; i++)
                {
                    history[i]?.Release();
                    history[i] = null;
                }
                historyValid = false;
            }
        }

        class PassData
        {
            public Material material;
            public int pass;
            public MaterialPropertyBlock props;
            public TextureHandle depth;
            public TextureHandle source;
            public TextureHandle source2;
        }

        readonly Dictionary<Camera, CameraState> m_States = new Dictionary<Camera, CameraState>();
        readonly List<Camera> m_Dead = new List<Camera>();
        readonly MistFrameData m_Data = new MistFrameData();
        readonly MaterialPropertyBlock m_Props = new MaterialPropertyBlock();

        GraphicsBuffer m_VolumeBuffer, m_LightBuffer, m_ClearBuffer;
        MistRendererFeature.Settings m_Settings;
        Material m_Material;
        int m_FrameCounter;

        // Ambient probe evaluation scratch (one direction: straight up).
        static readonly Vector3[] s_UpDir = { Vector3.up };
        static readonly Color[] s_AmbientResult = new Color[1];

        public MistRenderPass()
        {
            profilingSampler = new ProfilingSampler("Mist");
        }

        public void Setup(MistRendererFeature.Settings settings, Material material)
        {
            m_Settings = settings;
            m_Material = material;
            ConfigureInput(ScriptableRenderPassInput.Depth);
            requiresIntermediateTexture = true;
        }

        public void Dispose()
        {
            foreach (var state in m_States.Values) state.ReleaseHistory();
            m_States.Clear();
            m_VolumeBuffer?.Dispose(); m_VolumeBuffer = null;
            m_LightBuffer?.Dispose(); m_LightBuffer = null;
            m_ClearBuffer?.Dispose(); m_ClearBuffer = null;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            var camera = cameraData.camera;
            if (camera == null || m_Material == null) return;

            // 1. Pick, sort and pack (section 3). While the editor holds the preview, the last packed data is reused.
            bool reuseLast = MistRendererFeature.PreviewPaused && m_Data.volumeCount > 0 && m_VolumeBuffer != null;
            if (!reuseLast)
            {
                if (!m_Data.CollectVolumes(camera, MistClock.Now)) return;
                m_Data.CollectLights(camera, lightData.visibleLights, lightData.mainLightIndex);
                m_Data.CollectClearZones();
                UploadBuffers();
            }

            // 2. Sizes and per-camera history.
            var camDesc = cameraData.cameraTargetDescriptor;
            int lowW = Mathf.Max(1, Mathf.RoundToInt(camDesc.width * m_Settings.resolutionScale));
            int lowH = Mathf.Max(1, Mathf.RoundToInt(camDesc.height * m_Settings.resolutionScale));

            var state = GetState(camera);
            bool temporal = m_Settings.temporal;
            if (temporal)
            {
                var histDesc = new RenderTextureDescriptor(lowW, lowH, GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormat.None)
                {
                    msaaSamples = 1, useMipMap = false, dimension = TextureDimension.Tex2D,
                };
                bool reallocated = false;
                for (int i = 0; i < 2; i++)
                    reallocated |= RenderingUtils.ReAllocateHandleIfNeeded(ref state.history[i], histDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: i == 0 ? "_MistHistory0" : "_MistHistory1");
                if (reallocated) state.historyValid = false;   // dropped on resize
            }
            else
            {
                state.ReleaseHistory();
            }

            // 3. Camera matrices and motion for the temporal pass. The mist always renders into textures, so the
            //    projection takes the render-into-texture Y flip, the same as URP's own matrices for these passes.
            Matrix4x4 viewProj = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(0), true) * cameraData.GetViewMatrix(0);
            var camTransform = camera.transform;
            float moved = state.historyValid
                ? (camTransform.position - state.prevPosition).magnitude + Quaternion.Angle(camTransform.rotation, state.prevRotation) * 0.02f
                : 0f;
            // Large jumps are camera cuts: drop the history rather than smear it.
            if (moved > 5f) state.historyValid = false;

            SetShaderData(lightData, state, viewProj, moved, lowW, lowH, camera.farClipPlane);

            // 4. The passes.
            var lowDesc = camDesc;
            lowDesc.width = lowW;
            lowDesc.height = lowH;
            lowDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            lowDesc.depthStencilFormat = GraphicsFormat.None;
            lowDesc.msaaSamples = 1;
            lowDesc.useMipMap = false;
            lowDesc.bindMS = false;
            lowDesc.dimension = TextureDimension.Tex2D;

            TextureHandle depth = resources.cameraDepthTexture;
            TextureHandle target = resources.activeColorTexture;

            if (m_Settings.groundShade && lightData.mainLightIndex >= 0)
                AddFullscreenPass(renderGraph, "Mist Ground Shade", kPassGroundShade, target, depth, TextureHandle.nullHandle, TextureHandle.nullHandle);

            TextureHandle march = UniversalRenderer.CreateRenderGraphTexture(renderGraph, lowDesc, "_MistMarch", false, FilterMode.Bilinear);
            AddFullscreenPass(renderGraph, "Mist March", kPassMarch, march, depth, TextureHandle.nullHandle, TextureHandle.nullHandle);

            TextureHandle resolved = march;
            if (temporal)
            {
                // The history holds the unblurred march (section 4.4).
                TextureHandle historyWrite = renderGraph.ImportTexture(state.history[state.writeIndex]);
                TextureHandle historyRead = renderGraph.ImportTexture(state.history[1 - state.writeIndex]);
                AddFullscreenPass(renderGraph, "Mist Temporal", kPassTemporal, historyWrite, depth, march, historyRead);
                resolved = historyWrite;
                state.writeIndex = 1 - state.writeIndex;
                state.historyValid = true;
            }

            for (int i = 0; i < m_Settings.blurPasses; i++)
            {
                TextureHandle blurH = UniversalRenderer.CreateRenderGraphTexture(renderGraph, lowDesc, "_MistBlurH", false, FilterMode.Bilinear);
                AddFullscreenPass(renderGraph, "Mist Blur H", kPassBlurH, blurH, depth, resolved, TextureHandle.nullHandle);
                TextureHandle blurV = UniversalRenderer.CreateRenderGraphTexture(renderGraph, lowDesc, "_MistBlurV", false, FilterMode.Bilinear);
                AddFullscreenPass(renderGraph, "Mist Blur V", kPassBlurV, blurV, depth, blurH, TextureHandle.nullHandle);
                resolved = blurV;
            }

            AddFullscreenPass(renderGraph, "Mist Composite", kPassComposite, target, depth, resolved, TextureHandle.nullHandle);

            // 5. Remember this frame.
            state.prevViewProj = viewProj;
            state.prevPosition = camTransform.position;
            state.prevRotation = camTransform.rotation;
            state.frame++;
            state.lastUsedFrame = m_FrameCounter;
            if (++m_FrameCounter % kPruneEvery == 0) PruneStates();
        }

        void AddFullscreenPass(RenderGraph renderGraph, string name, int pass, TextureHandle target,
            TextureHandle depth, TextureHandle source, TextureHandle source2)
        {
            using var builder = renderGraph.AddRasterRenderPass<PassData>(name, out var data, profilingSampler);
            data.material = m_Material;
            data.pass = pass;
            data.props = m_Props;
            data.depth = depth;
            data.source = source;
            data.source2 = source2;

            if (depth.IsValid()) builder.UseTexture(depth);
            if (source.IsValid()) builder.UseTexture(source);
            if (source2.IsValid()) builder.UseTexture(source2);
            builder.UseAllGlobalTextures(true);   // the main light shadow map
            builder.SetRenderAttachment(target, 0, AccessFlags.Write);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
            {
                d.props.Clear();
                if (d.depth.IsValid()) d.props.SetTexture(Ids.Depth, AsTexture(d.depth));
                if (d.source.IsValid()) d.props.SetTexture(Ids.Source, AsTexture(d.source));
                if (d.source2.IsValid()) d.props.SetTexture(Ids.Source2, AsTexture(d.source2));
                ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass, MeshTopology.Triangles, 3, 1, d.props);
            });
        }

        static Texture AsTexture(TextureHandle handle)
        {
            RTHandle rt = handle;
            return rt;
        }

        void SetShaderData(UniversalLightData lightData, CameraState state, Matrix4x4 viewProj, float moved, int lowW, int lowH, float farClip)
        {
            var m = m_Material;
            var s = m_Settings;

            m.SetBuffer(Ids.Volumes, m_VolumeBuffer);
            m.SetBuffer(Ids.Lights, m_LightBuffer);
            m.SetBuffer(Ids.Clears, m_ClearBuffer);
            m.SetInt(Ids.VolumeCount, m_Data.volumeCount);
            m.SetInt(Ids.ShadeCount, Mathf.Min(MistFrameData.MaxShadeVolumes, m_Data.volumeCount));
            m.SetInt(Ids.LightCount, s.lights ? m_Data.lightCount : 0);
            m.SetInt(Ids.ClearCount, m_Data.clearCount);
            m.SetTexture(Ids.Noise, MistNoiseTexture.Get());

            m.SetMatrix(Ids.InvViewProj, viewProj.inverse);
            m.SetMatrix(Ids.PrevViewProj, state.prevViewProj);

            float frameJitter = s.temporal ? Frac(state.frame * 0.6180339887f) : 0f;
            m.SetVector(Ids.Params0, new Vector4(s.steps, s.selfShadow ? 1f : 0f, s.lights ? 1f : 0f, frameJitter));
            m.SetVector(Ids.Params1, new Vector4(s.groundShadeStrength, moved, Mathf.Min(farClip, 400f), state.historyValid ? 1f : 0f));
            m.SetVector(Ids.TargetSize, new Vector4(lowW, lowH, 1f / lowW, 1f / lowH));

            // Sun: URP's main light. finalColor is linear and includes intensity.
            Vector4 sunDir = new Vector4(0f, 1f, 0f, 0f);
            Color sunColor = Color.black;
            if (lightData.mainLightIndex >= 0 && lightData.mainLightIndex < lightData.visibleLights.Length)
            {
                var sun = lightData.visibleLights[lightData.mainLightIndex];
                Vector3 toSun = -((Vector3)sun.localToWorldMatrix.GetColumn(2)).normalized;
                sunDir = new Vector4(toSun.x, toSun.y, toSun.z, 1f);
                sunColor = sun.finalColor * s.sun;
            }
            m.SetVector(Ids.SunDir, sunDir);
            m.SetVector(Ids.SunColor, sunColor);
            m.SetVector(Ids.Ambient, AmbientColor() * s.ambient);
        }

        /// <summary>The scene's sky or ambient colour, linear, as the mist sees it.</summary>
        static Color AmbientColor()
        {
            switch (RenderSettings.ambientMode)
            {
                case AmbientMode.Flat:
                    return RenderSettings.ambientLight.linear;
                case AmbientMode.Trilight:
                    return Color.Lerp(RenderSettings.ambientEquatorColor.linear, RenderSettings.ambientSkyColor.linear, 0.6f);
                default:
                    RenderSettings.ambientProbe.Evaluate(s_UpDir, s_AmbientResult);
                    var c = s_AmbientResult[0];
                    return new Color(Mathf.Max(0f, c.r), Mathf.Max(0f, c.g), Mathf.Max(0f, c.b), 1f);
            }
        }

        void UploadBuffers()
        {
            EnsureBuffer(ref m_VolumeBuffer, MistFrameData.MaxVolumes * MistFrameData.FloatsPerVolume);
            EnsureBuffer(ref m_LightBuffer, MistFrameData.MaxLights * MistFrameData.FloatsPerLight);
            EnsureBuffer(ref m_ClearBuffer, MistFrameData.MaxClearZones * MistFrameData.FloatsPerClear);

            m_VolumeBuffer.SetData(m_Data.volumes, 0, 0, Mathf.Max(1, m_Data.volumeCount * MistFrameData.FloatsPerVolume));
            m_LightBuffer.SetData(m_Data.lights, 0, 0, Mathf.Max(1, m_Data.lightCount * MistFrameData.FloatsPerLight));
            m_ClearBuffer.SetData(m_Data.clears, 0, 0, Mathf.Max(1, m_Data.clearCount * MistFrameData.FloatsPerClear));
        }

        static void EnsureBuffer(ref GraphicsBuffer buffer, int count)
        {
            if (buffer != null && buffer.IsValid() && buffer.count == count) return;
            buffer?.Dispose();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
        }

        CameraState GetState(Camera camera)
        {
            if (!m_States.TryGetValue(camera, out var state))
            {
                state = new CameraState();
                m_States.Add(camera, state);
            }
            return state;
        }

        void PruneStates()
        {
            m_Dead.Clear();
            foreach (var kv in m_States)
                if (kv.Key == null || m_FrameCounter - kv.Value.lastUsedFrame > kStateLifetime) m_Dead.Add(kv.Key);
            foreach (var cam in m_Dead)
            {
                m_States[cam].ReleaseHistory();
                m_States.Remove(cam);
            }
        }

        static float Frac(float x) => x - Mathf.Floor(x);
    }
}
