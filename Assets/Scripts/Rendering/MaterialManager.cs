using System.Collections.Generic;
using UnityEngine;

namespace MarkovBlocks
{
    public static class MaterialManager {
        private static Dictionary<RenderType, Material> blockMaterials = new();

        private static Material defaultMaterial;

        private static bool initialized = false;

        public static Material GetAtlasMaterial(RenderType renderType)
        {
            EnsureInitialized();
            return blockMaterials.GetValueOrDefault(renderType, defaultMaterial);
        }

        public static void EnsureInitialized()
        {
            if (!initialized) Initialize();
        }

        public static void ClearInitializedFlag()
        {
            initialized = false;
        }

        private static void Initialize()
        {
            blockMaterials.Clear();

            /*
            // Solid
            var solid = Resources.Load<Material>("Materials/Block/Block Solid");
            solid.SetTexture("_BaseMap", AtlasManager.GetAtlasArray(RenderType.SOLID));
            blockMaterials.Add(RenderType.SOLID, solid);

            defaultMaterial = solid;

            // Cutout & Cutout Mipped
            var cutout = Resources.Load<Material>("Materials/Block/Block Cutout");
            cutout.SetTexture("_BaseMap", AtlasManager.GetAtlasArray(RenderType.CUTOUT));
            blockMaterials.Add(RenderType.CUTOUT, cutout);

            var cutoutMipped = Resources.Load<Material>("Materials/Block/Block Cutout Mipped");
            cutoutMipped.SetTexture("_BaseMap", AtlasManager.GetAtlasArray(RenderType.CUTOUT_MIPPED));
            blockMaterials.Add(RenderType.CUTOUT_MIPPED, cutoutMipped);

            // Translucent
            var translucent = Resources.Load<Material>("Materials/Block/Block Transparent");
            translucent.SetTexture("_BaseMap", AtlasManager.GetAtlasArray(RenderType.TRANSLUCENT));
            blockMaterials.Add(RenderType.TRANSLUCENT, translucent);

            // Also add water as translucent
            // TODO Sync with main project if necessary
            blockMaterials.Add(RenderType.WATER, translucent);
            */

            // All
            var solid = Resources.Load<Material>("Materials/Block");
            solid.SetTexture("_BaseMap", AtlasManager.GetAtlasArray(RenderType.SOLID));
            blockMaterials.Add(RenderType.SOLID, solid);
            blockMaterials.Add(RenderType.CUTOUT, solid);
            blockMaterials.Add(RenderType.CUTOUT_MIPPED, solid);
            blockMaterials.Add(RenderType.TRANSLUCENT, solid);
            blockMaterials.Add(RenderType.WATER, solid);

            defaultMaterial = solid;



            initialized = true;

        }

    }

}