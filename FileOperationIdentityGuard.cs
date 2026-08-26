namespace DesktopOrganizer
{
    internal static class FileOperationIdentityGuard
    {
        public static bool TryCapture(string path, out string identity)
        {
            identity = string.Empty;
            if (!NativeMethods.TryGetFileIdentity(path, out string? capturedIdentity) ||
                string.IsNullOrWhiteSpace(capturedIdentity))
            {
                return false;
            }

            identity = capturedIdentity;
            return true;
        }

        public static bool Matches(string path, string? expectedIdentity)
        {
            return !string.IsNullOrWhiteSpace(expectedIdentity) &&
                   TryCapture(path, out string currentIdentity) &&
                   string.Equals(currentIdentity, expectedIdentity, StringComparison.Ordinal);
        }
    }
}
