using UnityEngine;

namespace Tank.Gameplay.Flow
{
    /// <summary>Phone and tablet settings the game needs to be playable: landscape, a steady 60 fps and a screen that stays on.</summary>
    public static class MobileDisplay
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            if (!Application.isMobilePlatform) return;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.targetFrameRate = 60;
        }
    }
}
