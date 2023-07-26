#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Unity.Mathematics;
using TMPro;

using MinecraftClient.Mapping;
using MarkovJunior;

namespace MarkovCraft
{
    public class GenerationScene : GameScene
    {
        private static readonly char SP = Path.DirectorySeparatorChar;
        private static readonly Vector3 BLOCK_SELECTION_HIDDEN_POS = new Vector3(0F, -100F, 0F);

        [SerializeField] private ScreenManager? screenManager;
        [SerializeField] public CameraController? CamController;
        [SerializeField] public LayerMask VolumeLayerMask;
        [SerializeField] public LayerMask BlockColliderLayerMask;
        [SerializeField] public VolumeSelection? VolumeSelection;
        [SerializeField] public GameObject? BlockSelection;
        [SerializeField] public CanvasGroup? BlockSelectionPanelGroup;
        [SerializeField] public TMP_Text? BlockSelectionText;
        [SerializeField] public MappingItem? BlockSelectionMappingItem;
        [SerializeField] public MappingItem? ExportOverrideMappingItem;
        [SerializeField] public GameObject? ExportOverrideSection;

        [SerializeField] public Toggle? RecordToggle;
        [SerializeField] public TMP_Text? VolumeText, PlaybackSpeedText, GenerationText, FPSText;
        [SerializeField] public TMP_Dropdown? ConfiguredModelDropdown;
        [SerializeField] public Slider? PlaybackSpeedSlider;
        [SerializeField] public Button? CreateButton, ConfigButton, ExecuteButton, ExportButton;
        [SerializeField] public ModelGraph? ModelGraphUI;
        [SerializeField] public GameObject? GenerationResultPrefab;
        private readonly List<GenerationResult> generationResults = new();
        private GenerationResult? selectedResult = null;

        // Character => RGB Color specified in base palette
        // This palette should be loaded for only once
        private readonly Dictionary<char, int> baseColorPalette = new();
        private bool baseColorPaletteLoaded = false;

        private string confModelFile = string.Empty;
        public string ConfiguredModelFile => confModelFile;
        private readonly Dictionary<int, string> loadedConfModels = new();
        private ConfiguredModel? currentConfModel = null;
        public ConfiguredModel? ConfiguredModel => currentConfModel;
        // Character => (meshIndex, meshColor)
        private readonly Dictionary<char, int2> meshPalette = new();
        // Character => CustomMappingItem, its items CAN be edited in exporter
        private readonly Dictionary<char, CustomMappingItem> fullPaletteForEditing = new();
        // Character => CustomMappingItem, its items SHOULD NOT be edited once loaded from file
        private readonly Dictionary<char, CustomMappingItem> fullPaletteAsLoaded = new();
        private Interpreter? interpreter = null;
        private float playbackSpeed = 1F;
        private bool executing = false;

        private void GenerateProcedureGraph(string modelName)
        {
            if (interpreter != null && ModelGraphUI != null)
            {
                var graphPalette = meshPalette.ToDictionary(x => x.Key, x => ColorConvert.GetOpaqueColor32(x.Value.y));
                ModelGraphGenerator.GenerateGraph(ModelGraphUI, modelName, interpreter.root, graphPalette);
            }
            else
                ModelGraphUI?.gameObject.SetActive(false);
        }

        private void EnsureBasePaletteLoaded()
        {
            if (!baseColorPaletteLoaded)
            {
                XDocument.Load(PathHelper.GetExtraDataFile("palette.xml")).Root.Elements("color").ToList().ForEach(x =>
                {
                    var character = x.Get<char>("symbol");
                    var rgb = ColorConvert.RGBFromHexString(x.Get<string>("value"));
                    baseColorPalette.Add(character, rgb);
                });

                baseColorPaletteLoaded = true;
            }
        }

        public Dictionary<char, int> GetBaseColorPalette()
        {
            // Load base palette if not loaded yet
            EnsureBasePaletteLoaded();
            return baseColorPalette;
        }        

        public Dictionary<char, CustomMappingItem> GetFullPaletteAsLoaded()
        {
            return fullPaletteAsLoaded;
        }

        // Get only mapping items whose key is among the given charset
        public Dictionary<char, CustomMappingItem> GetPartialPaletteForEditing(HashSet<char> charSet)
        {
            return fullPaletteForEditing.Where(x => charSet.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value);
        }

        public IEnumerator UpdateConfiguredModel(string confModelFile, ConfiguredModel confModel)
        {
            Loading = true;
            GenerationText!.text = GetL10nString("status.info.load_conf_model", confModelFile);

            ExecuteButton!.interactable = false;
            ExecuteButton.GetComponentInChildren<TMP_Text>().text = GetL10nString("hud.text.load_conf_model");

            // Clear up scene
            ClearUpScene();

            // Clear up current model graph
            ModelGraphUI?.ClearUp();

            string fileName = PathHelper.GetExtraDataFile($"models{SP}{confModel.Model}.xml");
            Debug.Log($"{confModel.Model} > {fileName}");

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
                Loading = false;
                GenerationText!.text = GetL10nString("status.error.open_xml_failure", fileName);
                yield break;
            }

            yield return null;

            var loadComplete = false;

            Task.Run(() => {
                // Use a task to load this in so that the main thread doesn't get blocked
                interpreter = Interpreter.Load(modelDoc.Root, confModel.SizeX, confModel.SizeY, confModel.SizeZ);

                loadComplete = true;
            });

            while (!loadComplete)
                yield return null;

            if (interpreter == null)
            {
                Debug.LogWarning("ERROR: Failed to create model interpreter");
                Loading = false;
                GenerationText!.text = GetL10nString("status.error.model_interpreter_failure");
                yield break;
            }

            yield return null;

            var statePalette = BlockStatePalette.INSTANCE;
            var stateId2Mesh = new Dictionary<int, int>(); // StateId => Mesh index

            // Update full palette and mesh palette
            meshPalette.Clear();
            fullPaletteForEditing.Clear();
            fullPaletteAsLoaded.Clear();

            // Load base palette if not loaded yet
            EnsureBasePaletteLoaded();

            // First apply all items in base palette
            foreach (var pair in baseColorPalette)
            {
                meshPalette.Add( pair.Key, new int2(0, pair.Value) );
                fullPaletteAsLoaded.Add( pair.Key, new CustomMappingItem{
                        Character = pair.Key, Color = ColorConvert.GetOpaqueColor32(pair.Value) } );
            }

            blockMeshCount = 1; // #0 is preserved for default cube mesh

            // Read and apply custom mappings
            foreach (var item in confModel.CustomMapping)
            {
                int rgb = ColorConvert.GetRGB(item.Color);

                if (!string.IsNullOrWhiteSpace(item.BlockState))
                {
                    int stateId = BlockStateHelper.GetStateIdFromString(item.BlockState);
                    
                    if (stateId != BlockStateHelper.INVALID_BLOCKSTATE)
                    {
                        var state = statePalette.StatesTable[stateId];
                        //Debug.Log($"Mapped '{item.Character}' to [{stateId}] {state}");

                        if (stateId2Mesh.TryAdd(stateId, blockMeshCount))
                            meshPalette[item.Character] = new(blockMeshCount++, rgb);
                        else // The mesh of this block state is already regestered, just use it
                            meshPalette[item.Character] = new(stateId2Mesh[stateId], rgb);
                    }
                    else // Default cube mesh with custom color
                        meshPalette[item.Character] = new(0, rgb);
                }
                else // Default cube mesh with custom color
                    meshPalette[item.Character] = new(0, rgb);
                
                if (fullPaletteAsLoaded.ContainsKey(item.Character)) // Entry defined in base palette, overwrite it
                {
                    fullPaletteAsLoaded[item.Character] = item;
                }
                else // A new entry
                {
                    fullPaletteAsLoaded.Add( item.Character, item );
                }
                
                yield return null;
            }

            // Make a copy of each mapping item for editing
            foreach (var pair in fullPaletteAsLoaded)
            {
                fullPaletteForEditing.Add( pair.Key, pair.Value.AsCopy() );
            }

            // Update model graph
            GenerateProcedureGraph(confModel.Model);
            yield return null;

            // Generate block meshes
            GenerateBlockMeshes(stateId2Mesh);
            yield return null;

            Loading = false;

            ExecuteButton!.interactable = true;
            ExecuteButton.GetComponentInChildren<TMP_Text>().text = GetL10nString("hud.text.start_execution");
            ExecuteButton.onClick.RemoveAllListeners();
            ExecuteButton.onClick.AddListener(StartExecution);

            GenerationText!.text = GetL10nString("status.info.loaded_conf_model", confModelFile);
        }

        private IEnumerator RunGeneration()
        {
            if (executing || currentConfModel is null || interpreter is null || BlockMaterial is null || GenerationText == null || GenerationResultPrefab is null)
            {
                Debug.LogWarning("Generation cannot be initiated");
                StopExecution();
                yield break;
            }

            ClearUpScene();
            // Delay a bit so that the first frame of generation doesn't get cleared
            yield return new WaitForSecondsRealtime(0.1F);

            executing = true;
            var model = currentConfModel;

            var resultPerLine = Mathf.CeilToInt(Mathf.Sqrt(model.Amount));
            resultPerLine = Mathf.Max(resultPerLine, 1);
            
            Material[] materials = { BlockMaterial };

            System.Random rand = new();
            var seeds = model.Seeds;

            List<GenerationResult> results = new();

            int maxX = 0, maxY = 0, maxZ = 0;
            int stepsPerFrame = model.StepsPerRefresh;

            var record = RecordToggle?.isOn ?? false;

            for (int k = 1; k <= model.Amount; k++)
            {
                if (!executing) // Stop execution
                    break;
                
                int seed = seeds != null && k <= seeds.Length ? seeds[k - 1] : rand.Next();
                int xCount = (k - 1) % resultPerLine,  zCount = (k - 1) / resultPerLine;
                
                var resultObj = Instantiate(GenerationResultPrefab);
                resultObj.name = $"Result #{k} (Seed: {seed})";
                var result = resultObj!.GetComponent<GenerationResult>();
                result.GenerationSeed = seed;
                result.Iteration = k;

                results.Add(result);
                
                GenerationText.text = GetL10nString("status.info.generation_start", k);

                (byte[] state, char[] legend, int FX, int FY, int FZ, int stepCount) dataFrame = new();

                var enumerator = interpreter.Run(seed, model.Steps, model.Animated).GetEnumerator();

                bool hasNext = true;
                var recordedFrames = new List<GenerationFrameRecord>();

                while (hasNext)
                {
                    bool frameCompleted = false;

                    Task.Run(() => {
                        for (int s = 0;s < stepsPerFrame;s++)
                        {
                            if (hasNext)
                                hasNext = enumerator.MoveNext();
                        }

                        frameCompleted = true;
                    });

                    while (!frameCompleted)
                        yield return null;
                    
                    dataFrame = enumerator.Current;

                    if (!executing) // Stop execution
                    {
                        Destroy(resultObj);
                        break;
                    }

                    float tick = 1F / playbackSpeed;

                    if (model.Animated) // Visualize this step
                    {
                        // Update generation text
                        GenerationText.text = GetL10nString("status.info.generation_step", k, dataFrame.stepCount, (int)(tick * 1000));

                        var pos = new int3(2 + xCount * (dataFrame.FX + 2), 0, 2 + zCount * (dataFrame.FY + 2));
                        result.UpdateVolume(pos, new(dataFrame.FX, dataFrame.FZ, dataFrame.FY));

                        var instanceData = BlockDataBuilder.GetInstanceData(dataFrame.state!, dataFrame.FX, dataFrame.FY, dataFrame.FZ, pos,
                                // Frame legend index => (meshIndex, meshColor)
                                dataFrame.legend.Select(ch => meshPalette[ch]).ToArray());
                        BlockInstanceSpawner.VisualizeFrameState(instanceData, materials, blockMeshes, tick);

                        if (record)
                        {
                            // Record the data frame
                            recordedFrames.Add(new GenerationFrameRecord(new int3(dataFrame.FX, dataFrame.FY, dataFrame.FZ),
                                    dataFrame.state.Select(v => dataFrame.legend[v]).ToArray()));
                        }
                        
                        if (dataFrame.FX > maxX) maxX = dataFrame.FX;
                        if (dataFrame.FY > maxY) maxY = dataFrame.FY;
                        if (dataFrame.FZ > maxZ) maxZ = dataFrame.FZ;

                        // Update active node on graph
                        ModelGraphGenerator.UpdateGraph(ModelGraphUI!, interpreter.current);
                        //RedrawModelGraphAsImage("Working...");
                    }

                    yield return new WaitForSeconds(tick);
                }

                if (executing) // Visualize final state (last step)
                {
                    var pos = new int3(2 + xCount * (dataFrame.FX + 2), 0, 2 + zCount * (dataFrame.FY + 2));
                    result.UpdateVolume(pos, new(dataFrame.FX, dataFrame.FZ, dataFrame.FY));

                    var instanceData = BlockDataBuilder.GetInstanceData(dataFrame.state!, dataFrame.FX, dataFrame.FY, dataFrame.FZ, pos,
                            // Frame legend index => (meshIndex, meshColor)
                            dataFrame.legend.Select(ch => meshPalette[ch]).ToArray());
                    
                    // The final visualization is persistent
                    BlockInstanceSpawner.VisualizePersistentState(instanceData, materials, blockMeshes);

                    if (dataFrame.FX > maxX) maxX = dataFrame.FX;
                    if (dataFrame.FY > maxY) maxY = dataFrame.FY;
                    if (dataFrame.FZ > maxZ) maxZ = dataFrame.FZ;

                    var stateClone = new byte[dataFrame.state!.Length];
                    Array.Copy(dataFrame.state!, stateClone, stateClone.Length);

                    var legendClone = new char[dataFrame.legend!.Length];
                    Array.Copy(dataFrame.legend!, legendClone, legendClone.Length);

                    result.SetData((new[] { confModelFile, $"{seed}" }, stateClone, legendClone, dataFrame.FX, dataFrame.FY, dataFrame.FZ, dataFrame.stepCount));

                    Debug.Log($"Iteration #{k} complete. Steps: {dataFrame.stepCount} Frames: {recordedFrames.Count}");
                    ModelGraphUI!.SetActiveNode(-1); // Deselect active node
                    GenerationText.text = GetL10nString("status.info.generation_complete", k);

                    if (record)
                    {
                        // Record the data frame
                        recordedFrames.Add(new GenerationFrameRecord(new int3(dataFrame.FX, dataFrame.FY, dataFrame.FZ),
                                dataFrame.state.Select(v => dataFrame.legend![v]).ToArray()));
                        var recordingName = (currentConfModel?.Model ?? "Untitled") + $"_#{k}";
                        StartCoroutine(RecordingExporter.SaveRecording(fullPaletteForEditing, recordingName, maxX, maxY, maxZ, recordedFrames.ToArray()));
                    }
                }
            }

            if (executing) // If the execution hasn't been forced stopped
                StopExecution();
        }

        void Start()
        {
            // First load Minecraft data & resources
            var ver = VersionHolder!.Versions[VersionHolder.SelectedVersion];

            StartCoroutine(LoadMCBlockData(ver.DataVersion, ver.ResourceVersion,
                () => {
                    ExecuteButton!.interactable = false;
                    ExecuteButton.GetComponentInChildren<TMP_Text>().text = GetL10nString("hud.text.load_resource");
                },
                (status) => GenerationText!.text = GetL10nString(status),
                () => {
                    if (PlaybackSpeedSlider != null)
                    {
                        PlaybackSpeedSlider.onValueChanged.AddListener(UpdatePlaybackSpeed);
                        UpdatePlaybackSpeed(PlaybackSpeedSlider.value);
                    }

                    if (CreateButton != null)
                    {
                        CreateButton.onClick.RemoveAllListeners();
                        CreateButton.onClick.AddListener(() => screenManager!.SetActiveScreenByType<ConfiguredModelCreatorScreen>() );
                    }

                    if (ConfigButton != null)
                    {
                        ConfigButton.onClick.RemoveAllListeners();
                        ConfigButton.onClick.AddListener(() => screenManager!.SetActiveScreenByType<ConfiguredModelEditorScreen>() );
                    }

                    if (ExportButton != null)
                    {
                        ExportButton.onClick.RemoveAllListeners();
                        ExportButton.onClick.AddListener(() => screenManager!.SetActiveScreenByType<ExporterScreen>() );
                    }

                    UpdateConfModelList();

                    if (loadedConfModels.Count > 0) // Use first model by default
                        UpdateDropdownOption(0);
                })
            );
        }

        public void UpdateBlockSelection(CustomMappingItem? selectedItem, CustomMappingItem? selectedItemOverride = null, string text = "")
        {
            if (selectedItem is null)
            {
                BlockSelectionPanelGroup!.alpha = 0F;
            }
            else
            {
                BlockSelectionPanelGroup!.alpha = 1F;
                BlockSelectionText!.text = text;
                // Update mapping item information
                BlockSelectionMappingItem!.SetBlockState(selectedItem.BlockState);
                BlockSelectionMappingItem!.SetCharacter(selectedItem.Character);
                var hexString = ColorConvert.GetHexRGBString(selectedItem.Color);
                BlockSelectionMappingItem!.OnColorCodeInputValidate(hexString);
                BlockSelectionMappingItem!.OnColorCodeInputValueChange(hexString);

                if (selectedItemOverride is not null) // Display override section
                {
                    ExportOverrideSection?.SetActive(true);
                    // Update mapping item information
                    ExportOverrideMappingItem!.SetBlockState(selectedItemOverride.BlockState);
                    ExportOverrideMappingItem!.SetCharacter(selectedItemOverride.Character);
                    var hexOverrideString = ColorConvert.GetHexRGBString(selectedItemOverride.Color);
                    ExportOverrideMappingItem!.OnColorCodeInputValidate(hexOverrideString);
                    ExportOverrideMappingItem!.OnColorCodeInputValueChange(hexOverrideString);
                }
                else // Hide override section
                {
                    ExportOverrideSection?.SetActive(false);
                }
            }
        }

        void Update()
        {
            if (FPSText != null)
                FPSText.text = $"FPS:{((int)(1 / Time.unscaledDeltaTime)).ToString().PadLeft(4, ' ')}";
            
            if (screenManager != null && screenManager.IsPaused) return;
            
            var cam = CamController?.ViewCamera;
            
            if (cam != null && VolumeSelection != null)
            {
                if (!VolumeSelection.Locked) // Update selected volume
                {
                    var ray = cam.ScreenPointToRay(Input.mousePosition);
                    RaycastHit hit;

                    if (!EventSystem.current.IsPointerOverGameObject() && Physics.Raycast(ray.origin, ray.direction, out hit, 1000F, VolumeLayerMask))
                    {
                        UpdateSelectedResult(hit.collider.gameObject.GetComponentInParent<GenerationResult>());

                        if (Input.GetKeyDown(KeyCode.Mouse0) && selectedResult!.Completed) // Lock can only be applied to completed results
                        {
                            // Lock volume selection
                            VolumeSelection!.Lock();
                            // Enable block colliders
                            selectedResult.EnableBlockColliders();
                            // Show export button
                            ExportButton?.GetComponent<Animator>()?.SetBool("Hidden", false);
                        }
                    }
                    else
                    {
                        UpdateSelectedResult(null);
                    }
                }
                else // Selection is locked
                {
                    if (!EventSystem.current.IsPointerOverGameObject())
                    {
                        var ray = cam.ScreenPointToRay(Input.mousePosition);
                        RaycastHit hit;
                        
                        // Volume is still locked, update block selection
                        if (selectedResult != null && Physics.Raycast(ray.origin, ray.direction, out hit, 1000F, BlockColliderLayerMask)) // Mouse pointer is over a block
                        {
                            // Get block position in the volume (local space in volume)
                            var boxCollider = hit.collider as BoxCollider;
                            (int x, int y, int z, char character) = selectedResult.GetColliderPosInVolume(boxCollider!);

                            var itemAsLoaded = fullPaletteAsLoaded[character];
                            var itemToExport = fullPaletteForEditing[character];

                            UpdateBlockSelection(itemAsLoaded, itemAsLoaded.MapTargetIdentical(itemToExport) ? null : itemToExport, $"({x}, {y}, {z})");
                            // Update cursor position (world space)
                            BlockSelection!.transform.position = hit.collider.bounds.center;
                        }
                        else // Mouse pointer is over no block
                        {
                            if (Input.GetKeyDown(KeyCode.Mouse0)) // Unlock volume
                            {
                                // Unlock volume selection
                                VolumeSelection!.Unlock();
                                // Disable block colliders
                                selectedResult?.DisableBlockColliders();
                                // Hide export button
                                ExportButton?.GetComponent<Animator>()?.SetBool("Hidden", true);
                            }

                            // Reset block selection
                            BlockSelection!.transform.position = BLOCK_SELECTION_HIDDEN_POS;
                            UpdateBlockSelection(null);
                        }
                    }
                }
            }
        }

        private void UpdateSelectedResult(GenerationResult? newResult)
        {
            if (selectedResult == newResult) return;

            // Disable block colliders
            selectedResult?.DisableBlockColliders();

            if (newResult != null && newResult.Valid) // Valid
            {
                var size = newResult.GenerationSize;
                var fsc = newResult.FinalStepCount;

                VolumeText!.text = GetL10nString("hud.text.result_info", newResult.Iteration, newResult.GenerationSeed,
                        size.x, size.z, size.y, fsc == 0 ? "-" : fsc.ToString());
                VolumeSelection!.UpdateVolume(newResult.GetVolumePosition(), newResult.GetVolumeSize());
            
                selectedResult = newResult;
            }
            else // Null or going to be destroyed
            {
                VolumeText!.text = string.Empty;
                VolumeSelection!.HideVolume();
                // Reset block selection
                BlockSelection!.transform.position = BLOCK_SELECTION_HIDDEN_POS;
                UpdateBlockSelection(null);

                // Hide export button
                ExportButton?.GetComponent<Animator>()?.SetBool("Hidden", true);

                selectedResult = null;
            }
        }

        public (string[] info, byte[] state, char[] legend, int FX, int FY, int FZ, int steps)? GetSelectedResultData()
        {
            if (selectedResult == null || !selectedResult.Valid || !selectedResult.Completed)
                return null;

            // Result data should be present if the generation is completed
            return selectedResult.Data;
        }

        private void ClearUpScene()
        {
            // Clear up persistent entities
            BlockInstanceSpawner.ClearUpPersistentState();

            // Clear up generation results
            UpdateSelectedResult(null);

            var results = Component.FindObjectsOfType<GenerationResult>().ToArray();

            for (int i = 0;i < results.Length;i++)
            {
                results[i].Valid = false;
                Destroy(results[i].gameObject);
            }
        }

        public void UpdatePlaybackSpeed(float newValue)
        {
            playbackSpeed = newValue;

            if (PlaybackSpeedText != null)
                PlaybackSpeedText.text = $"{newValue:0.0}";
            
        }

        public void UpdateDropdownOption(int newValue)
        {
            if (loadedConfModels.ContainsKey(newValue))
                SetConfiguredModel(loadedConfModels[newValue]);
        }

        private void UpdateConfModelList()
        {
            var dir = PathHelper.GetExtraDataFile("configured_models");

            if (Directory.Exists(dir))
            {
                var options = new List<TMP_Dropdown.OptionData>();
                loadedConfModels.Clear();
                int index = 0;
                foreach (var m in Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories))
                {
                    var modelPath = m.Substring(m.LastIndexOf(SP) + 1);
                    options.Add(new(modelPath));
                    loadedConfModels.Add(index++, modelPath);
                }

                ConfiguredModelDropdown!.onValueChanged.RemoveAllListeners();
                ConfiguredModelDropdown!.ClearOptions();
                ConfiguredModelDropdown.AddOptions(options);
                ConfiguredModelDropdown.onValueChanged.AddListener(UpdateDropdownOption);
            }
        }

        public void UpdateConfiguredModel(string confModelFile)
        {
            // First update configure model list
            UpdateConfModelList();
            // Find index of this item in our list
            int selectedIndex = -1;
            foreach (var pair in loadedConfModels)
            {
                if (pair.Value == confModelFile)
                {
                    selectedIndex = pair.Key;
                }
            }

            if (selectedIndex != -1)
            {
                ConfiguredModelDropdown!.value = selectedIndex;
                UpdateDropdownOption(selectedIndex);
            }
        }

        private void SetConfiguredModel(string newConfModelFile)
        {
            if (executing)
                StopExecution();
            
            confModelFile = newConfModelFile;

            var xdoc = XDocument.Load($"{PathHelper.GetExtraDataFile("configured_models")}/{confModelFile}");
            var newConfModel = ConfiguredModel.CreateFromXMLDoc(xdoc);

            // Assign new configured model
            currentConfModel = newConfModel;

            if (!Loading)
                StartCoroutine(UpdateConfiguredModel(newConfModelFile, newConfModel));
        }

        public void StartExecution()
        {
            if (Loading || executing)
            {
                Debug.LogWarning("Execution cannot be started.");
                return;
            }

            StartCoroutine(RunGeneration());

            if (ExecuteButton != null)
            {
                ExecuteButton.GetComponentInChildren<TMP_Text>().text = GetL10nString("hud.text.stop_execution");
                ExecuteButton.onClick.RemoveAllListeners();
                ExecuteButton.onClick.AddListener(StopExecution);
            }

            if (RecordToggle != null)
            {
                RecordToggle.enabled = false;
            }
        }

        public void StopExecution()
        {
            if (Loading)
            {
                Debug.LogWarning("Execution cannot be stopped.");
                return;
            }

            executing = false;
            ModelGraphUI?.SetActiveNode(-1);

            if (ExecuteButton != null)
            {
                ExecuteButton.GetComponentInChildren<TMP_Text>().text = GetL10nString("hud.text.start_execution");
                ExecuteButton.onClick.RemoveAllListeners();
                ExecuteButton.onClick.AddListener(StartExecution);
            }

            if (RecordToggle != null)
            {
                RecordToggle.enabled = true;
            }
        }

        public override void ReturnToMenu()
        {
            if (executing)
                StopExecution();
            
            ClearUpScene();

            // Unpause game to restore time scale
            screenManager!.IsPaused = false;

            SceneManager.LoadScene("Scenes/Welcome", LoadSceneMode.Single);
        }
    }
}