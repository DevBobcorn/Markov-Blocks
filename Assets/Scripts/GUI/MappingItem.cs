#nullable enable
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MarkovBlocks
{
    public class MappingItem : MonoBehaviour
    {
        [SerializeField] Color32 MappingColor;

        [SerializeField] Image? ColorPreviewImage, IsUsedIndicator;
        [SerializeField] TMP_InputField? CharacterInput;
        [SerializeField] TMP_InputField? ColorCodeInput;
        [SerializeField] TMP_InputField? BlockStateInput;

        [SerializeField] Button? RevertOverrideButton;

        private bool isUsedByCurrentModel = false;
        private bool overridesPaletteColor = false;

        // RGB color of this item in the base palette
        private int defaultRgb = 0;

        private char character;
        public char Character => character;

        public void InitializeData(char character, int defoRgb, int rgb, string blockState)
        {
            if (ColorPreviewImage == null || CharacterInput == null || ColorCodeInput == null || BlockStateInput == null
                    || IsUsedIndicator == null || RevertOverrideButton == null)
            {
                Debug.LogError("Mapping Item missing components!");
                return;
            }

            this.character = character;
            defaultRgb = defoRgb & 0xFFFFFF; // Remove alpha channel if presents

            // Character input
            CharacterInput.text = character.ToString();
            // Color input
            ColorCodeInput.text = ColorConvert.GetHexRGBString(rgb);
            ColorPreviewImage.color = ColorConvert.GetOpaqueColor32(rgb);
            // Black state input
            BlockStateInput.text = blockState;

            TagAsUsed(false);

            SetOverridesPaletteColor(!blockState.Equals(string.Empty) || defoRgb != rgb);

            // Assign control events
            RevertOverrideButton.onClick.AddListener(RevertColorToBaseValue);
            ColorCodeInput.onValueChanged.AddListener(UpdateColorCode);
            ColorCodeInput.onEndEdit.AddListener(ValidateColorCode);
            
        }

        public void UpdateColorCode(string colorHex)
        {
            var padded = colorHex.PadRight(6, '0'); // Pad left with '0's

            int newRgb = ColorConvert.RGBFromHexString(colorHex);
            ColorPreviewImage!.color = ColorConvert.GetOpaqueColor32(newRgb);

            if (newRgb == defaultRgb)
                SetOverridesPaletteColor(false);
            else
                SetOverridesPaletteColor(true);
        }

        public void ValidateColorCode(string colorHex)
        {
            var paddedUpper = colorHex.PadRight(6, '0').ToUpper(); // Pad left with '0's
            ColorCodeInput!.text = paddedUpper;

            int newRgb = ColorConvert.RGBFromHexString(colorHex);
            ColorPreviewImage!.color = ColorConvert.GetOpaqueColor32(newRgb);

            if (newRgb == defaultRgb)
                SetOverridesPaletteColor(false);
            else
                SetOverridesPaletteColor(true);

        }

        public void TagAsUsed(bool isUsed)
        {
            isUsedByCurrentModel = isUsed;

            IsUsedIndicator?.gameObject.SetActive(isUsed);
        }

        public void SetOverridesPaletteColor(bool o)
        {
            overridesPaletteColor = o;

            if (RevertOverrideButton?.gameObject.activeSelf != o)
                RevertOverrideButton?.gameObject.SetActive(o);
        }

        public bool ShouldBeSaved()
        {
            return overridesPaletteColor || !string.IsNullOrWhiteSpace(BlockStateInput?.text);
        }

        public void RevertColorToBaseValue()
        {
            if (ColorPreviewImage == null || CharacterInput == null || ColorCodeInput == null || BlockStateInput == null)
            {
                Debug.LogError("Mapping Item missing components!");
                return;
            }

            ColorCodeInput.text = ColorConvert.GetHexRGBString(defaultRgb);
            ColorPreviewImage.color = ColorConvert.GetOpaqueColor32(defaultRgb);
        }

    }
}