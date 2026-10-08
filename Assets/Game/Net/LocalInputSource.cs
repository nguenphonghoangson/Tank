using Tank.Core.Common;
using Tank.Core.Combat;
using Tank.Core.Hit;
using Tank.Core.Input;
using Tank.Core.Items;
using Tank.Gameplay.Input;

namespace Tank.Net
{
    /// <summary>Who drives the tank on this machine: the person at the keyboard, or a bot when a test client was started with -autoplay.</summary>
    public static class LocalInputSource
    {
        public static ICommandSource Create(ITankRegistry tanks, IHitWorld world, IClock clock, IRandom random, IItemQuery items, ICommandSource human)
        {
            return LaunchOptions.Autoplay ? (ICommandSource)new BotCommandSource(tanks, world, clock, random, items) : human;
        }
    }
}
