using System;
using System.Collections.Generic;
using UnityEngine;

using MarkovCraft.Mapping;

namespace MarkovCraft
{
    public class ResourcePackManager
    {
        // Identifier -> Texture file path
        public readonly Dictionary<ResourceLocation, string> TextureFileTable = new();

        // Identidier -> Block json model file path
        public readonly Dictionary<ResourceLocation, string> BlockModelFileTable = new();

        // Identidier -> BlockState json model file path
        public readonly Dictionary<ResourceLocation, string> BlockStateFileTable = new();

        // Identifier -> Block model
        public readonly Dictionary<ResourceLocation, JsonModel> BlockModelTable = new();

        // Block state numeral id -> Block state geometries (One single block state may have a list of models to use randomly)
        public readonly Dictionary<int, BlockStateModel> StateModelTable = new();

        public readonly BlockModelLoader BlockModelLoader;
        public readonly BlockStateModelLoader StateModelLoader;

        public int GeneratedItemModelPrecision { get; set; } = 16;
        public int GeneratedItemModelThickness { get; set; } =  1;

        private readonly List<ResourcePack> packs = new List<ResourcePack>();

        public static readonly ResourcePackManager Instance = new();

        private ResourcePackManager()
        {
            // Block model loaders
            BlockModelLoader = new BlockModelLoader(this);
            StateModelLoader = new BlockStateModelLoader(this);
        }

        public void AddPack(ResourcePack pack) => packs.Add(pack);

        public void ClearPacks()
        {
            packs.Clear();
            TextureFileTable.Clear();
            BlockModelTable.Clear();
            StateModelTable.Clear();
        }

        public void LoadPacks(DataLoadFlag flag, Action<string> updateStatus)
        {
            System.Diagnostics.Stopwatch sw = new();
            sw.Start();

            // Gather all textures and model files
            updateStatus("status.info.gather_resource");
            foreach (var pack in packs) pack.GatherResources(this);

            var atlasGenFlag = new DataLoadFlag();

            // Load texture atlas (on main thread)...
            updateStatus("status.info.create_texture");
            Loom.QueueOnMainThread(() => {
                Loom.Current.StartCoroutine(AtlasManager.Generate(this, atlasGenFlag));
            });
            
            while (!atlasGenFlag.Finished) { /* Wait */ }

            updateStatus("status.info.load_block_model");

            // Load block models...
            foreach (var blockModelId in BlockModelFileTable.Keys)
            {
                // This model loader will load this model, its parent model(if not yet loaded),
                // and then add them to the manager's model dictionary
                BlockModelLoader.LoadBlockModel(blockModelId);
            }

            // Load item models...
            // [Code removed]

            updateStatus("status.info.build_blockstate_geometry");
            BuildStateGeometries();
            // [Code removed]

            // Perform integrity check...
            var statesTable = BlockStatePalette.INSTANCE.StatesTable;

            foreach (var stateItem in statesTable)
            {
                if (!StateModelTable.ContainsKey(stateItem.Key))
                {
                    Debug.LogWarning($"Model for {stateItem.Value}(state Id {stateItem.Key}) not loaded!");
                }
            }

            Debug.Log($"Resource packs loaded in {sw.ElapsedMilliseconds} ms.");
            Debug.Log($"Built {StateModelTable.Count} block state geometry lists.");

            flag.Finished = true;
        }

        public void BuildStateGeometries()
        {
            // Load all blockstate files and build their block meshes...
            foreach (var blockPair in BlockStatePalette.INSTANCE.StateListTable)
            {
                var blockId = blockPair.Key;
                
                if (BlockStateFileTable.ContainsKey(blockId)) // Load the state model definition of this block
                {
                    var renderType =
                        BlockStatePalette.INSTANCE.RenderTypeTable.GetValueOrDefault(blockId, RenderType.SOLID);

                    StateModelLoader.LoadBlockStateModel(this, blockId, BlockStateFileTable[blockId], renderType);
                }
                else
                    Debug.LogWarning($"Block state model definition not assigned for {blockId}!");
                
            }

        }

    }
}