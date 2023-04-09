#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using UnityEngine;
using UnityEngine.UI;
using Unity.Mathematics;
using TMPro;

using MarkovJunior;
using MarkovBlocks.Mapping;
using System.Threading.Tasks;

namespace MarkovBlocks
{
    public class Test : MonoBehaviour
    {
        public const int WINDOWED_APP_WIDTH = 1600, WINDOWED_APP_HEIGHT = 900;

        [SerializeField] public TMP_Text? playbackSpeedText, generationText;
        [SerializeField] public TMP_Dropdown? modelDropdown;
        [SerializeField] public Slider? playbackSpeedSlider;
        [SerializeField] public Toggle? dyeBlockMeshesToggle;
        [SerializeField] public Button? executeButton;
        [SerializeField] public RawImage? graphImage;

        private string confModelName = string.Empty;
        public string ConfiguredModelName => confModelName;
        private readonly Dictionary<int, string> loadedConfModels = new();

        private MarkovJuniorModel? currentModel = null;
        public MarkovJuniorModel? CurrentModel => currentModel;

        private Interpreter? interpreter = null;
        private float playbackSpeed = 1F;
        private bool dyeBlockMeshes = false;
        private bool executing = false;

        // Palettes and meshes
        private readonly ResourcePackManager packManager = new();
        private Mesh[] blockMeshes = { };
        private int blockMeshCount = 0;
        private readonly Dictionary<char, int2> fullPalette = new();
        private Material? blockMaterial;

        private readonly LoadStateInfo loadInfo = new();

        private static Test? instance;
        public static Test Instance
        {
            get {
                if (instance == null)
                    instance = Component.FindObjectOfType<Test>();

                return instance!;
            }
        }

        private void RedrawProcedureGraph(Dictionary<char, int2> palette)
        {
            if (currentModel != null && interpreter != null && graphImage != null)
            {
                int imageX = 200, imageY = 600;
                var image = new int[imageX * imageY];

                MarkovJunior.GUI.Draw(currentModel.Name, interpreter.root, null, image, imageX, imageY, palette);
                
                Texture2D texture = new(imageX, imageY);
                texture.filterMode = FilterMode.Point;

                var color32s = new Color32[imageX * imageY];

                for (int y = 0; y < imageY; y++) for (int x = 0; x < imageX; x++)
                {
                    int rgb = image[x + (imageY - 1 - y) * imageX];
                    color32s[x + y * imageX] = ColorConvert.GetOpaqueColor32(rgb);
                }

                texture.SetPixels32(color32s);
                texture.Apply(true, false);
                
                graphImage.texture = texture;
                graphImage.SetNativeSize();

            }
            else
                Debug.LogWarning("Failed to update procedure graph due to stuffs missing!");

        }

        private void GenerateBlockMeshes(Dictionary<int, int> stateId2Mesh) // StateId => Mesh index
        {
            var statePalette = BlockStatePalette.INSTANCE;
            var buffers = new VertexBuffer[blockMeshCount];
            
            for (int i = 0;i < buffers.Length;i++)
                buffers[i] = new VertexBuffer();

            // #0 is default cube mesh
            CubeGeometry.Build(ref buffers[0], AtlasManager.HAKU, 0, 0, 0, 0b111111, new float3(1F));

            var dummyWorld = new MarkovBlocks.Mapping.World();
            var modelTable = packManager.StateModelTable;
            
            foreach (var pair in stateId2Mesh) // StateId => Mesh index
            {
                var stateId = pair.Key;

                if (modelTable.ContainsKey(stateId))
                    modelTable[stateId].Geometries[0].Build(ref buffers[pair.Value], float3.zero, 0b111111,
                            statePalette.GetBlockColor(stateId, dummyWorld, Location.Zero, statePalette.FromId(stateId)));
                else
                {
                    Debug.LogWarning($"Model for block state #{stateId} ({statePalette.FromId(stateId)}) is not available. Using cube model instead.");
                    CubeGeometry.Build(ref buffers[pair.Value], AtlasManager.HAKU, 0, 0, 0, 0b111111, new float3(1F, 1F, 1F));
                }
            }

            // Set result to blockMeshes
            blockMeshes = BlockMeshGenerator.GenerateMeshes(buffers);
        }

        private void VisualizePaletteMapping()
        {
            int ia = 0;
            foreach (var pair in fullPalette)
            {
                var go = new GameObject($"Character [{pair.Key}]");

                go.AddComponent<MeshFilter>().mesh = blockMeshes[pair.Value.x];

                if (pair.Value.x != 0)
                    go.AddComponent<MeshRenderer>().sharedMaterial = blockMaterial;
                else
                {
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.material = blockMaterial; // Create a new material instance
                    mr.material!.SetColor("_InstanceBlockColor",
                            ColorConvert.GetOpaqueColor32(pair.Value.y));
                }

                go.transform.position = (ia++) % 2 == 0 ? new(ia - 3, 0, -2) : new(-2, 0, ia - 2);
            }
        }

        public IEnumerator SetConfiguredModel(string confModelName, MarkovJuniorModel model)
        {
            loadInfo.Loading = true;
            loadInfo.InfoText = $"Loading configured model [{confModelName}]...";

            // Clear up persistent entities
            BlockInstanceSpawner.ClearUpPersistentState();

            // Assign new generation model
            currentModel = model;

            string fileName = PathHelper.GetExtraDataFile($"models/{model.Name}.xml");
            Debug.Log($"{model.Name} > {fileName}");

            XDocument? modelDoc = null;

            if (File.Exists(fileName))
            {
                FileStream fs = new(fileName, FileMode.Open);

                var task = XDocument.LoadAsync(fs, LoadOptions.SetLineInfo, new());

                while (!task.IsCompleted)
                    yield return null;
                
                fs.Close();
                
                if (task.IsCompletedSuccessfully)
                    modelDoc = task.Result;
            }
            
            if (modelDoc is null)
            {
                Debug.LogWarning($"ERROR: Couldn't open xml file at {fileName}");
                yield break;
            }

            yield return null;

            var loadComplete = false;

            Task.Run(() => {
                // Use a task to load this in so that the main thread doesn't get blocked
                interpreter = Interpreter.Load(modelDoc.Root, model.SizeX, model.SizeY, model.SizeZ);

                loadComplete = true;
            });

            while (!loadComplete)
                yield return null;

            if (interpreter == null)
            {
                Debug.LogWarning("ERROR: Failed to create model interpreter");
                yield break;
            }

            yield return null;

            var statePalette = BlockStatePalette.INSTANCE;
            var stateId2Mesh = new Dictionary<int, int>(); // StateId => Mesh index

            fullPalette.Clear();

            Dictionary<char, int> basePalette = XDocument.Load(PathHelper.GetExtraDataFile("palette.xml")).Root.Elements("color")
                    .ToDictionary(x => x.Get<char>("symbol"), x => (255 << 24) + Convert.ToInt32(x.Get<string>("value"), 16));
            
            foreach (var item in basePalette) // Use mesh #0 by default (cube mesh)
                fullPalette.Add(item.Key, new(0, item.Value));

            blockMeshCount = 1; // #0 is preserved for default cube mesh

            foreach (var remap in model.CustomRemapping) // Read and assign custom remapping
            {
                int rgba = ColorConvert.GetRGBA(remap.RemapColor);

                if (!string.IsNullOrWhiteSpace(remap.RemapTarget))
                {
                    int remapStateId = BlockStateRemapper.GetStateIdFromString(remap.RemapTarget);
                    
                    if (remapStateId != BlockStateRemapper.INVALID_BLOCKSTATE)
                    {
                        var state = statePalette.StatesTable[remapStateId];
                        //Debug.Log($"Remapped '{remap.Symbol}' to [{remapStateId}] {state}");

                        if (stateId2Mesh.TryAdd(remapStateId, blockMeshCount))
                            fullPalette[remap.Symbol] = new(blockMeshCount++, rgba);
                        else // The mesh of this block state is already regestered, just use it
                            fullPalette[remap.Symbol] = new(stateId2Mesh[remapStateId], rgba);
                    }
                    else // Default cube mesh with custom color
                        fullPalette[remap.Symbol] = new(0, rgba);
                }
                else // Default cube mesh with custom color
                    fullPalette[remap.Symbol] = new(0, rgba);
                
                yield return null;
            }

            // Update procedure graph
            RedrawProcedureGraph(fullPalette);

            yield return null;

            // Generate block meshes
            GenerateBlockMeshes(stateId2Mesh);

            yield return null;

            loadInfo.Loading = false;

            if (generationText != null)
                generationText.text = $"[{confModelName}] loaded";
        }

        private IEnumerator LoadMCData(string dataVersion, string[] packs, Action? callback = null)
        {
            loadInfo.Loading = true;

            // First load all possible Block States...
            var loadFlag = new DataLoadFlag();
            StartCoroutine(BlockStatePalette.INSTANCE.PrepareData(dataVersion, loadFlag, loadInfo));

            while (!loadFlag.Finished)
                yield return null;
            
            // Then load all Items...
            // [Code removed]

            // Load resource packs...
            packManager.ClearPacks();
            // Collect packs
            foreach (var packName in packs)
                packManager.AddPack(new(packName));
            // Load valid packs...
            loadFlag.Finished = false;
            StartCoroutine(packManager.LoadPacks(this, loadFlag, loadInfo));

            while (!loadFlag.Finished)
                yield return null;
            
            loadInfo.Loading = false;

            if (loadFlag.Failed)
            {
                Debug.LogWarning("Block data loading failed");
                yield break;
            }

            MaterialManager.EnsureInitialized();
            blockMaterial = MaterialManager.GetAtlasMaterial(RenderType.SOLID);

            if (callback is not null)
                callback.Invoke();
        }

        private IEnumerator RunGeneration()
        {
            if (currentModel is null || interpreter is null || blockMaterial is null)
            {
                Debug.LogWarning("Generation cannot be initiated");
                yield break;
            }

            // Clear up persistent entities
            BlockInstanceSpawner.ClearUpPersistentState();

            executing = true;
            var model = currentModel;

            var resultPerLine = Mathf.RoundToInt(Mathf.Sqrt(model.Amount));
            resultPerLine = Mathf.Max(resultPerLine, 1);
            
            Material[] materials = { blockMaterial };

            System.Random rand = new();
            var seeds = model.Seeds;

            // Prepare a copy of full palette in which blockstate meshes are not dyed
            var undyedFullPalette = new Dictionary<char, int2>();

            foreach (var pair in fullPalette)
            {
                if (pair.Value.x == 0) // Cube meshes will still preserve their colors so as to be distinguished from each other
                    undyedFullPalette.Add(pair.Key, pair.Value);
                else // For Minecraft block meshes, set their color to white
                    undyedFullPalette.Add(pair.Key, new int2(pair.Value.x, 0xFFFFFF));
            }

            for (int k = 0; k < model.Amount; k++)
            {
                if (!executing) // Stop execution
                    break;
                
                int seed = seeds != null && k < seeds.Length ? seeds[k] : rand.Next();
                int frameCount = 0;

                (int3[], int2[])? instanceDataRaw = null;

                foreach ((byte[] result, char[] legend, int FX, int FY, int FZ) in interpreter.Run(seed, model.Steps, model.Animated))
                {
                    if (!executing) // Stop execution
                        break;

                    int2[] stepPalette = legend.Select(ch => dyeBlockMeshes ? fullPalette[ch] : undyedFullPalette[ch]).ToArray();
                    float tick = 1F / playbackSpeed;

                    if (instanceDataRaw != null)
                        BlockInstanceSpawner.VisualizeState(instanceDataRaw.Value, materials, blockMeshes, tick, 0.5F);

                    // Update generation text
                    if (generationText != null)
                        generationText.text = $"Iteration: {k + 1}\nFrame: {frameCount}\nTick: {(int)(tick * 1000)}ms";

                    frameCount++;

                    int xCount = k / resultPerLine,  zCount = k % resultPerLine;
                    var pos = new int3(xCount * (FX + 2), 0, zCount * (FY + 2));

                    instanceDataRaw = BlockDataBuilder.GetInstanceData(result, FX, FY, FZ, pos, FZ > 1, stepPalette);

                    yield return new WaitForSeconds(tick);
                }

                if (instanceDataRaw != null && executing)
                {
                    // The final visualization is persistent
                    BlockInstanceSpawner.VisualizePersistentState(instanceDataRaw.Value, materials, blockMeshes);
                    Debug.Log($"Generation complete. Frame Count: {frameCount}");
                }
                
            }

            if (executing) // If the execution wasn't forced stopped
                StopExecution();
        }

        void Start()
        {
            // First load Minecraft data & resources
            StartCoroutine(LoadMCData("markov", new string[] {
                    "vanilla-1.16.5", "vanilla_fix", "default"
                }, () => {
                    if (playbackSpeedSlider != null)
                    {
                        playbackSpeedSlider.onValueChanged.AddListener(UpdatePlaybackSpeed);
                        UpdatePlaybackSpeed(playbackSpeedSlider.value);
                    }

                    if (dyeBlockMeshesToggle != null)
                    {
                        dyeBlockMeshesToggle.onValueChanged.AddListener(UpdateDyeBlockMeshes);
                        UpdateDyeBlockMeshes(dyeBlockMeshesToggle.isOn);
                    }
                    
                    if (executeButton != null)
                    {
                        executeButton.GetComponentInChildren<TMP_Text>().text = "Start Execution";
                        executeButton.onClick.AddListener(StartExecution);
                    }

                    var dir = PathHelper.GetExtraDataFile("configured_models");
                    if (Directory.Exists(dir) && modelDropdown != null)
                    {
                        var models = Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories);
                        var options = new List<TMP_Dropdown.OptionData>();
                        loadedConfModels.Clear();
                        int index = 0;
                        foreach (var m in models)
                        {
                            var modelPath = m.Substring(dir.Length + 1);
                            options.Add(new(modelPath));
                            loadedConfModels.Add(index++, modelPath);
                        }

                        modelDropdown.AddOptions(options);
                        modelDropdown.onValueChanged.AddListener(UpdateDropdownOption);

                        if (options.Count > 0) // Use first model by default
                            UpdateDropdownOption(0);
                    }
                }));
            
            
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F11)) // Toggle full screen
            {
                if (Screen.fullScreen)
                {
                    Screen.SetResolution(WINDOWED_APP_WIDTH, WINDOWED_APP_HEIGHT, false);
                    Screen.fullScreen = false;
                }
                else
                {
                    var maxRes = Screen.resolutions[Screen.resolutions.Length - 1];
                    Screen.SetResolution(maxRes.width, maxRes.height, true);
                    Screen.fullScreen = true;
                }
                
            }

            if (loadInfo.Loading && generationText != null)
                generationText.text = loadInfo.InfoText;

        }

        public void UpdatePlaybackSpeed(float newValue)
        {
            playbackSpeed = newValue;

            if (playbackSpeedText != null)
                playbackSpeedText.text = $"{newValue:0.0}";
            
        }

        public void UpdateDyeBlockMeshes(bool newValue)
        {
            dyeBlockMeshes = newValue;
            
        }

        public void UpdateDropdownOption(int newValue)
        {
            if (executing)
                StopExecution();

            if (loadedConfModels.ContainsKey(newValue))
            {
                var dir = PathHelper.GetExtraDataFile("configured_models");

                // Update selected configured model
                confModelName = loadedConfModels[newValue];

                var xdoc = XDocument.Load($"{dir}/{confModelName}");
                var model = MarkovJuniorModel.CreateFromXMLDoc(xdoc);

                if (!loadInfo.Loading)
                    StartCoroutine(SetConfiguredModel(confModelName, model));
            }
        }

        public void StartExecution()
        {
            if (loadInfo.Loading || executing)
            {
                Debug.LogWarning("Execution cannot be started.");
                return;
            }

            StartCoroutine(RunGeneration());

            if (executeButton != null)
            {
                executeButton.GetComponentInChildren<TMP_Text>().text = "Stop Execution";
                executeButton.onClick.RemoveAllListeners();
                executeButton.onClick.AddListener(StopExecution);
            }
        }

        public void StopExecution()
        {
            if (loadInfo.Loading)
            {
                Debug.LogWarning("Execution cannot be stopped.");
                return;
            }

            executing = false;

            if (executeButton != null)
            {
                executeButton.GetComponentInChildren<TMP_Text>().text = "Start Execution";
                executeButton.onClick.RemoveAllListeners();
                executeButton.onClick.AddListener(StartExecution);
            }
        }

    }
}