namespace DesktopOrganizer
{
    internal static class PushReflowPolicy
    {
        public static bool CanConfigure(bool safeModeActive, bool snapToGrid, bool editMode) =>
            !safeModeActive && snapToGrid && editMode;

        public static bool IsActive(
            bool safeModeActive,
            bool snapToGrid,
            bool editMode,
            bool preferenceEnabled) =>
            preferenceEnabled && CanConfigure(safeModeActive, snapToGrid, editMode);
    }
}
