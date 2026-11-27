#pragma warning disable CS0162 // Chopper edit: the check below is switched off (unreachable code).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UniStorm.Utility
{
    public static class RendererFeatureCheck
    {
        public static void Check()
        {
            if (!Application.isPlaying) return;
            // Chopper edit: our URP renderer has no UniStorm features on purpose (Buto volumetric fog, our own sky setup),
            // so this check only logs a false error every match. Remove this line to bring the check back.
            return;

            var pipeline = GraphicsSettings.currentRenderPipeline
                as UniversalRenderPipelineAsset;

            if (pipeline == null) return;

            var renderer = pipeline.scriptableRenderer;

            for (int i = 0; i < pipeline.rendererDataList.Length; i++)
            {
                var data = pipeline.rendererDataList[i];

                if (data == null || pipeline.GetRenderer(i) != renderer)
                    continue;

                foreach (var feature in data.rendererFeatures)
                {
                    if (feature is UniStormCloudsRendererFeature)
                        return;
                }

                break;
            }

            Debug.LogError("UniStorm's Renderer Features were not detected. To add them, exit Playmode and go to Window>UniStorm>Add URP Renderer Features");
        }
    }
}