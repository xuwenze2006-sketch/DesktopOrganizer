namespace DesktopOrganizer
{
    internal sealed record LayoutDeserializationResult(
        AppLayoutData Layout,
        int SerializedVersion);

    internal static class LayoutJsonSerializer
    {
        public static LayoutDeserializationResult Deserialize(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            int serializedVersion = 0;

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("FreeIcons", out _))
            {
                if (document.RootElement.TryGetProperty("Version", out JsonElement versionElement) &&
                    versionElement.ValueKind == JsonValueKind.Number)
                {
                    _ = versionElement.TryGetInt32(out serializedVersion);
                }

                AppLayoutData layout = JsonSerializer.Deserialize<AppLayoutData>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new AppLayoutData();
                layout.Version = serializedVersion;
                layout.SaveGeneration = Math.Max(0, layout.SaveGeneration);
                return new LayoutDeserializationResult(layout, serializedVersion);
            }

            Dictionary<string, IconPosition>? oldFormat =
                JsonSerializer.Deserialize<Dictionary<string, IconPosition>>(json);
            return new LayoutDeserializationResult(
                new AppLayoutData
                {
                    Version = 0,
                    FreeIcons = oldFormat ?? new Dictionary<string, IconPosition>()
                },
                SerializedVersion: 0);
        }
    }
}
