using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TankGame.Prototype
{
    /// <summary>
    /// Runtime-only quality reduction for phones (never applied in the editor, so the pipeline asset on disk is untouched):
    /// lower render scale, no HDR, shorter shadow distance. Tune from device frame-rate numbers, not by guessing.
    /// </summary>
    public static class MobilePerformance
    {
        public static void Apply()
        {
            if (!Application.isMobilePlatform || Application.isEditor) return;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) return;
            urp.renderScale = 0.75f;
            urp.supportsHDR = false;
            urp.shadowDistance = 35f;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
        }
    }
}
