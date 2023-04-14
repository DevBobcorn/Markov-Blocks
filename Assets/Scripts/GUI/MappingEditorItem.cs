#nullable enable
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MarkovBlocks
{
    public class MappingEditorItem : MonoBehaviour
    {
        [SerializeField] Color32 ActiveTagColor;
        [SerializeField] Color32 LockedTagColor;

        [SerializeField] Color32 MappingColor;

        [SerializeField] Image? ColorPreviewImage, MarkCornerImage;
        [SerializeField] TMP_Text? CharacterText;
        [SerializeField] TMP_InputField? ColorCodeInput;
        [SerializeField] TMP_InputField? BlockStateInput;

        [SerializeField] Button? RevertOverrideButton;

        private bool overridesPaletteColor = false;

        // RGB color of this item in the base palette
        private int defaultRgb = 0;

        private char character;
        public char Character => character;

        public void InitializeData(char character, int defoRgb, int rgb, string blockState)
        {
            if (ColorPreviewImage == null || CharacterText == null || ColorCodeInput == null || BlockStateInput == null
                    || MarkCornerImage == null || RevertOverrideButton == null)
            {
                Debug.LogError("Mapping Item missing components!");
                return;
            }

            this.character = character;
            defaultRgb = defoRgb & 0xFFFFFF; // Remove alpha channel if presents

            // Character input
            CharacterText.text = character.ToString();
            // Color input
            ColorCodeInput.text = ColorConvert.GetHexRGBString(rgb);
            ColorPreviewImage.color = ColorConvert.GetOpaqueColor32(rgb);
            // Black state input
            BlockStateInput.text = blockState;

            SetOverridesPaletteColor(defoRgb != rgb);

            // Assign control events (should get called only once)
            RevertOverrideButton.onClick.AddListener(RevertColorToBaseValue);
            ColorCodeInput.onValueChanged.AddListener(UpdateColorCode);
            ColorCodeInput.onEndEdit.AddListener(ValidateColorCode);
            
        }

        public void UpdateColorCode(string colorHex)
        {
            int newRgb = ColorConvert.RGBFromHexString(colorHex.PadRight(6, '0'));
            ColorPreviewImage!.color = ColorConvert.GetOpaqueColor32(newRgb);

            if (newRgb == defaultRgb)
                SetOverridesPaletteColor(false);
            else
                SetOverridesPaletteColor(true);
        }

        public void ValidateColorCode(string colorHex)
        {
            ColorCodeInput!.text = colorHex.PadRight(6, '0').ToUpper();

        }

        public string GetColorCode() => ColorCodeInput?.text ?? "000000";

        public string GetBlockState()
        {
            var blockState = BlockStateInput?.text;

            if (string.IsNullOrWhiteSpace(blockState))
                return string.Empty;
            
            return blockState;
        }

        public void TagAsActive()
        {
            MarkCornerImage!.gameObject.SetActive(true);
            MarkCornerImage!.color = ActiveTagColor;
        }

        public void TagAsLocked(string blockState)
        {
            BlockStateInput!.text = blockState;
            BlockStateInput.interactable = false;

            MarkCornerImage!.gameObject.SetActive(true);
            MarkCornerImage!.color = LockedTagColor;
        }

        public void SetOverridesPaletteColor(bool o)
        {
            overridesPaletteColor = o;

            RevertOverrideButton?.gameObject.SetActive(o);
        }

        public bool ShouldBeSaved()
        {
            return overridesPaletteColor || !string.IsNullOrWhiteSpace(BlockStateInput?.text);
        }

        public void RevertColorToBaseValue()
        {
            if (ColorPreviewImage == null || CharacterText == null || ColorCodeInput == null || BlockStateInput == null)
            {
                Debug.LogError("Mapping Item missing components!");
                return;
            }

            ColorCodeInput.text = ColorConvert.GetHexRGBString(defaultRgb);
            ColorPreviewImage.color = ColorConvert.GetOpaqueColor32(defaultRgb);
        }

    }
}