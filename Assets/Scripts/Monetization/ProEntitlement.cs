using System;

namespace ScrapSiege.Monetization
{
    /// <summary>
    /// Decoupled Pro-status gate that gameplay code reads, without needing to reference the
    /// RevenueCat SDK directly. The SDK ships with no .asmdef, so its classes compile into
    /// Unity's implicit default assembly - an asmdef-based assembly like ScrapSiege.Runtime
    /// cannot reference that (only the reverse is allowed). MonetizationManager, which lives
    /// outside this assembly alongside the SDK, pushes updates in here; everything else -
    /// including this assembly's own gameplay code - just reads it.
    /// </summary>
    public static class ProEntitlement
    {
        public static bool IsUnlocked { get; private set; }
        public static event Action<bool> Changed;

        public static void SetUnlocked(bool unlocked)
        {
            if (IsUnlocked == unlocked) return;
            IsUnlocked = unlocked;
            Changed?.Invoke(unlocked);
        }
    }
}
