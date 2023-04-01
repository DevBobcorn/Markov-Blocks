#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using TMPro;

using MarkovJunior;

namespace MarkovBlocks
{
    public class Test : MonoBehaviour
    {
        private static readonly Bounds cubeBounds = new Bounds(new(0.5F, 0.5F, 0.5F), new(1F, 1F, 1F));
        private static readonly RenderBounds renderBounds = new RenderBounds { Value = cubeBounds.ToAABB() };

        [SerializeField] public string modelName = "Apartemazements";
        [SerializeField] public int modelLength = 1;
        [SerializeField] public int modelWidth  = 1;
        [SerializeField] public int modelHeight = 1;
        [SerializeField] public int modelAmount = 2;
        [SerializeField] public int modelSteps = 1000;
        [SerializeField] public bool modelAnimated = false;
        [SerializeField] public string? modelSeed = string.Empty;

        [SerializeField] public TMP_Text? playbackSpeedText, generationText;
        [SerializeField] public Slider? playbackSpeedSlider;

        [SerializeField] public Material? cubeMaterial;

        private float playbackSpeed = 1F;
        private Mesh[] blockMeshes = { };


        private IEnumerator RunTest()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var folder = System.IO.Directory.CreateDirectory("output");
            foreach (var file in folder.GetFiles()) file.Delete();

            Dictionary<char, int> palette = XDocument.Load(PathHelper.GetExtraDataFile("palette.xml")).Root.Elements("color")
                    .ToDictionary(x => x.Get<char>("symbol"), x => (255 << 24) + Convert.ToInt32(x.Get<string>("value"), 16));

            System.Random rand = new();

            Debug.Log($"{modelName} > ");
            string filename = PathHelper.GetExtraDataFile($"models/{modelName}.xml");

            XDocument modeldoc;
            try { modeldoc = XDocument.Load(filename, LoadOptions.SetLineInfo); }
            catch (Exception)
            {
                Debug.Log($"ERROR: Couldn't open xml file {filename}");
                yield break;
            }

            Interpreter interpreter = Interpreter.Load(modeldoc.Root, modelLength, modelWidth, modelHeight);
            if (interpreter == null)
            {
                Debug.Log("ERROR: Failed to creating model interpreter");
                yield break;
            }

            if (modelSeed != null && modelSeed.Trim().Equals(string.Empty)) // Seed not specified
                modelSeed = null;
            
            int[]? seeds = modelSeed?.Split(' ').Select(s => int.Parse(s)).ToArray();
            
            Dictionary<char, int> customPalette = new(palette);

            /* TODO: Implement
            foreach (var x in xmodel.Elements("color"))
                customPalette[x.Get<char>("symbol")] = (255 << 24) + Convert.ToInt32(x.Get<string>("value"), 16);
            */

            var resultPerLine = Mathf.RoundToInt(Mathf.Sqrt(modelAmount));

            if (resultPerLine <= 0)
                resultPerLine = 1;

            for (int k = 0; k < modelAmount; k++)
            {
                int seed = seeds != null && k < seeds.Length ? seeds[k] : rand.Next();
                int frameCount = 0;

                int4[]? instanceDataRaw = null;

                foreach ((byte[] result, char[] legend, int FX, int FY, int FZ) in interpreter.Run(seed, modelSteps, true))
                {
                    int[] colors = legend.Select(ch => customPalette[ch]).ToArray();
                    float tick = 1F / playbackSpeed;

                    if (instanceDataRaw != null)
                        VisualizeState(instanceDataRaw, blockMeshes, tick);

                    // Update generation text
                    if (generationText != null)
                        generationText.text = $"Iteration: {k + 1}\nFrame: {frameCount}\nTick: {(int)(tick * 1000)}ms";

                    frameCount++;

                    int xCount = k % resultPerLine, zCount = k / resultPerLine;
                    int ox = xCount * (FX + 2), oz = zCount * (FY + 2);

                    instanceDataRaw = CubeDataBuilder.GetInstanceData(result,
                            (byte)FX, (byte)FY, (byte)FZ, ox, 0, oz, FZ > 1, colors);

                    yield return new WaitForSeconds(tick);
                }

                if (instanceDataRaw != null)
                    VisualizeState(instanceDataRaw, blockMeshes, 0F); // The final visualization is persistent

                Debug.Log($"Generation complete. Frame Count: {frameCount}");
            }

            Debug.Log($"Time elapsed: {sw.ElapsedMilliseconds}ms");
        }

        private void VisualizeState(int4[] instanceDataRaw, Mesh[] meshes, float persistence)
        {
            bool persistent = persistence <= 0F;
            var entityCount = instanceDataRaw.Length;

            var instanceData = new NativeArray<int4>(entityCount, Allocator.TempJob);
            instanceData.CopyFrom(instanceDataRaw);

            var world = World.DefaultGameObjectInjectionWorld;
            var entityManager = world.EntityManager;
            EntityCommandBuffer ecbJob = new EntityCommandBuffer(Allocator.TempJob);
            
            #region Prepare entity prototype
            var filterSettings = RenderFilterSettings.Default;

            var renderMeshArray = new RenderMeshArray(new[] { cubeMaterial }, blockMeshes);
            var renderMeshDescription = new RenderMeshDescription
            {
                FilterSettings = filterSettings,
                LightProbeUsage = LightProbeUsage.Off,
            };

            var prototype = entityManager.CreateEntity();

            RenderMeshUtility.AddComponents(
                prototype,
                entityManager,
                renderMeshDescription,
                renderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            
            entityManager.AddComponentData(prototype, new URPMaterialPropertyBaseColor());

            if (!persistent) // Add magic component to make it expire after some time
                entityManager.AddComponentData(prototype, new MagicComponent());
            #endregion

            // Spawn most of the entities in a Burst job by cloning a pre-created prototype entity,
            // which can be either a Prefab or an entity created at run time like in this sample.
            // This is the fastest and most efficient way to create entities at run time.
            var spawnJob = new SpawnJob
            {
                Ecb = ecbJob.AsParallelWriter(),
                Prototype = prototype,
                RenderBounds = renderBounds,
                InstanceData = instanceData,
                LifeTime = persistence
            };

            var spawnHandle = spawnJob.Schedule(entityCount, 128);

            spawnHandle.Complete();

            ecbJob.Playback(entityManager);
            ecbJob.Dispose();

            entityManager.DestroyEntity(prototype);
        }

        void Start()
        {
            // Generate block meshes
            var buffer = new VertexBuffer();
            CubeGeometry.Build(ref buffer, 0, 0, 0, 0b111111, float3.zero);

            var buffers = new VertexBuffer[] { buffer };

            blockMeshes = BlockMeshGenerator.GenerateMeshes(buffers);

            if (playbackSpeedSlider != null) // Initialize playback speed
                UpdatePlaybackSpeed(playbackSpeedSlider.value);
            else
                Debug.LogWarning("Playback speed slider is not assigned!");

            // Start it up!
            StartCoroutine(RunTest());

        }

        public void UpdatePlaybackSpeed(float newValue)
        {
            if (playbackSpeedText != null) // Update playback speed
            {
                playbackSpeedText.text = $"{newValue:0.0}";
                playbackSpeed = newValue;
            }
            else
                Debug.LogWarning("Playback speed text is not assigned!");
            
        }

    }
}