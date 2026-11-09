using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SlaydGames.Mist
{
    /// <summary>Where in the frame the mist is drawn.</summary>
    public enum MistInjectionPoint
    {
        /// <summary>After opaques and the sky, before transparents. Transparent objects draw over the mist.</summary>
        AfterSkybox = RenderPassEvent.AfterRenderingSkybox,
        /// <summary>After transparents, before post-processing. The mist draws over transparent objects.</summary>
        AfterTransparents = RenderPassEvent.AfterRenderingTransparents,
    }

    /// <summary>
    /// The URP renderer feature that draws every <see cref="MistVolume"/>. Add it to the URP renderer asset
    /// (Tools > Mist > Add Mist To URP Renderers does this). The renderer-wide settings of section 1 live here.
    /// </summary>
    [DisallowMultipleRendererFeature("Mist")]
    [Tooltip("Draws the Mist Volume components in the scene as volumetric mist.")]
    public sealed class MistRendererFeature : ScriptableRendererFeature
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Draw the mist at all.")]
            public bool enabled = true;

            [Tooltip("Maximum march steps per stretch of mist. Quality against cost.")]
            [Range(4, 64)] public int steps = 24;

            [Tooltip("A short look toward the sun per sample, so the billows are shaded.")]
            public bool selfShadow = true;

            [Tooltip("Scene point and spot lights scatter in the mist.")]
            public bool lights = true;

            [Tooltip("The ground under the boxes is darkened where the sun passes through mist.")]
            public bool groundShade = true;

            [Tooltip("How much of the ground shade to apply. A screen-space pass cannot tell sunlit from ambient light, so this stands in for the sun's share.")]
            [Range(0f, 1f)] public float groundShadeStrength = 0.75f;

            [Tooltip("How strongly the sun lights the mist.")]
            [Range(0f, 2f)] public float sun = 0.45f;

            [Tooltip("Share of the sky or ambient colour the mist takes.")]
            [Range(0f, 2f)] public float ambient = 0.6f;

            [Tooltip("Resolution of the mist pass relative to the camera.")]
            [Range(0.25f, 1f)] public float resolutionScale = 0.5f;

            [Tooltip("Blend each frame with the last, reprojected. Smooths the low-resolution march.")]
            public bool temporal = true;

            [Tooltip("Depth-aware blur passes over the mist before it is composited.")]
            [Range(0, 3)] public int blurPasses = 1;

            [Tooltip("Where in the frame the mist is drawn.")]
            public MistInjectionPoint injectionPoint = MistInjectionPoint.AfterSkybox;

            [Tooltip("Draw the mist in Scene views too.")]
            public bool renderInSceneView = true;
        }

        public Settings settings = new Settings();

        // Serialized so the shader is included in builds. Filled in by Create() in the editor.
        [SerializeField, HideInInspector] Shader m_Shader;

        Material m_Material;
        MistRenderPass m_Pass;

        /// <summary>Set by the editor's "hide all mist" toggle: Scene views skip the mist, outlines stay.</summary>
        public static bool HideInSceneView { get; set; }

        /// <summary>
        /// Set by the editor while a slider is held and "update only on release" is on: the pass keeps
        /// drawing the last packed volumes instead of reading the changing values.
        /// </summary>
        public static bool PreviewPaused { get; set; }

        public override void Create()
        {
            if (m_Shader == null) m_Shader = Shader.Find(MistRenderPass.ShaderName);
            m_Pass ??= new MistRenderPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!settings.enabled) return;

            var type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            if (type == CameraType.SceneView && (!settings.renderInSceneView || HideInSceneView)) return;
            if (MistVolume.Active.Count == 0) return;

            if (m_Shader == null)
            {
                m_Shader = Shader.Find(MistRenderPass.ShaderName);
                if (m_Shader == null) return;
            }
            if (m_Material == null || m_Material.shader != m_Shader)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = CoreUtils.CreateEngineMaterial(m_Shader);
            }

            m_Pass.renderPassEvent = (RenderPassEvent)settings.injectionPoint;
            m_Pass.Setup(settings, m_Material);
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            m_Pass?.Dispose();
            m_Pass = null;
            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }
    }
}
