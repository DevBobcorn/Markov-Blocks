#nullable enable
using UnityEngine;

namespace MarkovCraft
{
    public class PauseScreen : BaseScreen
    {
        private Test? game;

        void Start()
        {
            game = Test.Instance;
        }

        public override bool ShouldPause() => true;

        public override void ScreenUpdate(ScreenManager manager)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                manager.SetActiveScreenByType<HUDScreen>();
            }

        }

        public void ReturnToMenu()
        {
            game!.ReturnToMenu();
            
        }

        public void BackToGame()
        {
            manager!.SetActiveScreenByType<HUDScreen>();

        }
    }
}