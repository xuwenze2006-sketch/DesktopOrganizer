namespace DesktopOrganizer
{
    internal static class QuietStartupVisibilityPolicy
    {
        public static bool ShouldHideControlPanel(bool startQuietly, bool trayRegistered) =>
            startQuietly && trayRegistered;
    }
}
