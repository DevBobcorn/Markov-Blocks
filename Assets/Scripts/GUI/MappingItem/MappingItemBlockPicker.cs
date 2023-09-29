#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

using CraftSharp;
using Unity.Entities.UniversalDelegates;

namespace MarkovCraft
{
    public class MappingItemBlockPicker : MonoBehaviour
    {
        // Block list
        [SerializeField] public GameObject? BlockListItemPrefab;
        [SerializeField] public ScrollRect? BlockListScrollView;
        [SerializeField] public RectTransform? GridTransform;
        // Blockstate panel
        [SerializeField] private BlockStatePreview? blockStatePreview;
        [SerializeField] private RectTransform? propertyListTransform;
        [SerializeField] private GameObject? propertyPrefab;

        private readonly Dictionary<ResourceLocation, BlockListItem> blockListItems = new();

        private MappingItem? activeItem = null;
        private int selectedBlockStateId = 0;
        private BlockState selectedBlockState = BlockState.AIR_STATE;

        private void SelectBlock(ResourceLocation blockId)
        {
            var defaultStateId = BlockStateHelper.GetDefaultStateId(blockId);

            if (defaultStateId != BlockStateHelper.INVALID_BLOCKSTATE)
            {
                SelectBlockState(defaultStateId, BlockStatePalette.INSTANCE.StatesTable[defaultStateId]);
            }
            else
            {
                SelectBlockState(0, BlockState.AIR_STATE);
            }
        }

        private void SelectBlockState(int blockStateId, BlockState blockState)
        {
            if (selectedBlockStateId == blockStateId)
            {
                // Target already selected
                return;
            }

            // Select our target
            selectedBlockStateId = blockStateId;
            selectedBlockState = blockState;
            blockStatePreview!.UpdatePreview(selectedBlockStateId);

            if (selectedBlockStateId != 0)
            {
                var allProps = BlockStatePalette.INSTANCE.GetBlockProperties(blockState.BlockId);

                // Prepare blockstate properties
                foreach (var prop in propertyListTransform!)
                {
                    Destroy((prop as Transform)!.gameObject);
                }

                foreach (var pair in selectedBlockState.Properties)
                {
                    var propObj = GameObject.Instantiate(propertyPrefab);
                    var prop = propObj!.GetComponent<BlockStateProperty>();
                    
                    prop.SetData(pair.Key, allProps[pair.Key].ToArray());
                    prop.SelectValue(pair.Value);
                    prop.SetCallback((key, val) =>
                    {
                        //Debug.Log($"Blockstate property updated: {key}={val}");
                        var (newStateId, newState) = BlockStatePalette.INSTANCE.GetBlockStateWithProperty
                                (selectedBlockStateId, selectedBlockState, key, val);
                        
                        SelectBlockState(newStateId, newState);
                    });

                    propObj.transform.SetParent(propertyListTransform, false);
                }
            }
        }

        private IEnumerator InitSelection(float pos)
        {
            yield return new WaitForEndOfFrame();

            BlockListScrollView!.verticalNormalizedPosition = 1F - pos;
        }

        private void Open()
        {
            var canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.alpha = 1F;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        private void Close()
        {
            var canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0F;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            // Clear 3d block preview items
            blockListItems.Clear();
            foreach (Transform block in GridTransform!)
            {
                Destroy(block.gameObject);
            }
            // Hide blockstate preview
            blockStatePreview!.UpdatePreview(BlockStateHelper.INVALID_BLOCKSTATE);

            SelectBlockState(0, BlockState.AIR_STATE);

            activeItem = null;
        }

        public void OpenAndInitialize(MappingItem item, int initialBlockStateId, BlockState initialBlockState)
        {
            if (activeItem != null) // Opened by an item while editing another one
            {
                // Apply current blockstate to active item
                ApplyToItem(activeItem);
            }

            // Update active item
            activeItem = item;

            // Initialize block list
            blockListItems.Clear();
            foreach (Transform block in GridTransform!)
            {
                Destroy(block.gameObject);
            }

            int index = 0;

            foreach (var pair in BlockStatePalette.INSTANCE.DefaultStateTable)
            {
                var blockId = pair.Key;
                var defaultStateId = pair.Value;

                var blockListItemObj = Instantiate(BlockListItemPrefab)!;
                blockListItemObj.transform.SetParent(GridTransform, false);

                var blockListItem = blockListItemObj.GetComponent<BlockListItem>();

                blockListItem.SetBlockId(blockId, defaultStateId);
                blockListItem.SetClickEvent(() => SelectBlock(blockId));
                
                blockListItems.Add(blockId, blockListItem);

                index++;
            }

            // Update selected blockstate
            if (blockListItems.ContainsKey(initialBlockState.BlockId))
            {
                var target = blockListItems[initialBlockState.BlockId]!;
                var pos = blockListItems.Keys.ToList().IndexOf(initialBlockState.BlockId);
                var posInList = pos / (float) blockListItems.Count;

                StartCoroutine(InitSelection(posInList));

                SelectBlockState(initialBlockStateId, initialBlockState);
                target.VisualSelect();
            }

            Open();
        }

        public void CloseAndDiscard()
        {
            Close();
        }

        private void ApplyToItem(MappingItem item)
        {
            var table = BlockStatePalette.INSTANCE.StatesTable;
            item.SetBlockState(table[selectedBlockStateId].ToString());
        }

        public void CloseAndApply()
        {
            if (activeItem != null) // Active item is available
            {
                // Apply current color to active item
                ApplyToItem(activeItem);
            }

            Close();
        }
    }
}