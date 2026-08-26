namespace DesktopOrganizer
{
    internal enum PendingExitLayoutRecoveryState
    {
        None,
        Promoted,
        Deferred,
        Invalid,
        Superseded,
        Conflict,
        Unavailable
    }

    internal sealed record PendingExitLayoutRecoveryResult(
        PendingExitLayoutRecoveryState State,
        string? Json,
        string? PendingIdentity);

    internal static class PendingExitLayoutRecovery
    {
        public static PendingExitLayoutRecoveryResult ReadAndPromote(
            string pendingPath,
            string layoutPath)
        {
            if (!File.Exists(pendingPath))
            {
                return new PendingExitLayoutRecoveryResult(
                    PendingExitLayoutRecoveryState.None,
                    Json: null,
                    PendingIdentity: null);
            }

            _ = FileOperationIdentityGuard.TryCapture(
                pendingPath,
                out string pendingIdentity);
            string? capturedIdentity = string.IsNullOrWhiteSpace(pendingIdentity)
                ? null
                : pendingIdentity;

            string recoveryJson;
            try
            {
                recoveryJson = File.ReadAllText(pendingPath, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Pending exit layout read failed: {exception}");
                return new PendingExitLayoutRecoveryResult(
                    PendingExitLayoutRecoveryState.Unavailable,
                    Json: null,
                    capturedIdentity);
            }

            LayoutDeserializationResult pendingLayout;
            try
            {
                pendingLayout = LayoutJsonSerializer.Deserialize(recoveryJson);
            }
            catch (JsonException exception)
            {
                Debug.WriteLine($"Pending exit layout is invalid: {exception}");
                _ = TryQuarantine(pendingPath, capturedIdentity);
                return new PendingExitLayoutRecoveryResult(
                    PendingExitLayoutRecoveryState.Invalid,
                    Json: null,
                    PendingIdentity: null);
            }

            bool mainExists = File.Exists(layoutPath);
            string? mainJson = null;
            LayoutDeserializationResult? mainLayout = null;
            if (mainExists)
            {
                try
                {
                    mainJson = File.ReadAllText(layoutPath, Encoding.UTF8);
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"Main layout generation read deferred: {exception}");
                    return new PendingExitLayoutRecoveryResult(
                        PendingExitLayoutRecoveryState.Deferred,
                        recoveryJson,
                        capturedIdentity);
                }

                try
                {
                    mainLayout = LayoutJsonSerializer.Deserialize(mainJson);
                }
                catch (JsonException exception)
                {
                    Debug.WriteLine($"Main layout is invalid; pending snapshot will replace it: {exception}");
                }

                if (mainLayout != null)
                {
                    long pendingGeneration = pendingLayout.Layout.SaveGeneration;
                    long mainGeneration = mainLayout.Layout.SaveGeneration;
                    if (pendingGeneration < mainGeneration)
                    {
                        return new PendingExitLayoutRecoveryResult(
                            PendingExitLayoutRecoveryState.Superseded,
                            Json: null,
                            capturedIdentity);
                    }

                    if (pendingGeneration == mainGeneration &&
                        pendingGeneration > 0)
                    {
                        return new PendingExitLayoutRecoveryResult(
                            string.Equals(recoveryJson, mainJson, StringComparison.Ordinal)
                                ? PendingExitLayoutRecoveryState.Superseded
                                : PendingExitLayoutRecoveryState.Conflict,
                            Json: null,
                            capturedIdentity);
                    }

                    if (pendingGeneration == 0 &&
                        mainGeneration == 0 &&
                        string.Equals(recoveryJson, mainJson, StringComparison.Ordinal))
                    {
                        return new PendingExitLayoutRecoveryResult(
                            PendingExitLayoutRecoveryState.Superseded,
                            Json: null,
                            capturedIdentity);
                    }
                }
            }

            if (capturedIdentity != null &&
                !FileOperationIdentityGuard.Matches(pendingPath, capturedIdentity))
            {
                return new PendingExitLayoutRecoveryResult(
                    PendingExitLayoutRecoveryState.Conflict,
                    Json: null,
                    capturedIdentity);
            }

            try
            {
                if (!string.Equals(
                        File.ReadAllText(pendingPath, Encoding.UTF8),
                        recoveryJson,
                        StringComparison.Ordinal) ||
                    (mainExists && !string.Equals(
                        File.ReadAllText(layoutPath, Encoding.UTF8),
                        mainJson,
                        StringComparison.Ordinal)) ||
                    (!mainExists && File.Exists(layoutPath)))
                {
                    return new PendingExitLayoutRecoveryResult(
                        PendingExitLayoutRecoveryState.Conflict,
                        Json: null,
                        capturedIdentity);
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Pending exit layout generation recheck deferred: {exception}");
                return new PendingExitLayoutRecoveryResult(
                    PendingExitLayoutRecoveryState.Deferred,
                    recoveryJson,
                    capturedIdentity);
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(layoutPath)!);
                File.Move(pendingPath, layoutPath, overwrite: true);
                return new PendingExitLayoutRecoveryResult(
                    PendingExitLayoutRecoveryState.Promoted,
                    recoveryJson,
                    PendingIdentity: null);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Pending exit layout promotion deferred: {exception}");
                return new PendingExitLayoutRecoveryResult(
                    PendingExitLayoutRecoveryState.Deferred,
                    recoveryJson,
                    capturedIdentity);
            }
        }

        public static bool TryQuarantine(string pendingPath, string? expectedIdentity)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(expectedIdentity) ||
                    !File.Exists(pendingPath) ||
                    !FileOperationIdentityGuard.Matches(pendingPath, expectedIdentity))
                {
                    return false;
                }

                File.Move(pendingPath, pendingPath + ".invalid", overwrite: true);
                return true;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Pending exit layout quarantine failed: {exception}");
                return false;
            }
        }

    }
}
