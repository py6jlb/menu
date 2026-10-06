using System.Text.Json;
using System.Text.RegularExpressions;

namespace MenuPlanner.Api.Observability;

/// <summary>Откуда оператору известен release-id.</summary>
public enum ReleaseIdentitySource
{
    /// <summary>Ни окружение, ни manifest не дали значения.</summary>
    Unavailable,

    /// <summary>Значение пришло явной переменной окружения.</summary>
    Environment,

    /// <summary>Значение прочитано из manifest релиза.</summary>
    Manifest
}

/// <summary>
/// Идентификатор релиза для корреляции журналов и запросов. Берётся из
/// <c>RELEASE_ID</c>, иначе из manifest (<c>RELEASE_MANIFEST_PATH</c>). Отсутствие
/// manifest не роняет приложение: идентификатор становится <c>unknown</c>, а
/// источник явно виден в стартовой записи. Значение ограничено безопасным
/// алфавитом, чтобы не подмешать произвольный текст в журнал.
/// </summary>
public sealed partial record ReleaseIdentity(string Id, ReleaseIdentitySource Source)
{
    public const string UnknownId = "unknown";
    public const string IdVariable = "RELEASE_ID";
    public const string ManifestPathVariable = "RELEASE_MANIFEST_PATH";

    public static readonly ReleaseIdentity Unknown = new(UnknownId, ReleaseIdentitySource.Unavailable);

    public bool IsKnown => Source != ReleaseIdentitySource.Unavailable;

    public static ReleaseIdentity Resolve(IConfiguration configuration)
    {
        var configured = (configuration[IdVariable] ?? "").Trim();
        if (IsSafeId(configured))
            return new ReleaseIdentity(configured, ReleaseIdentitySource.Environment);

        var path = (configuration[ManifestPathVariable] ?? "").Trim();
        if (path.Length > 0 && TryReadManifest(path, out var manifestId))
            return new ReleaseIdentity(manifestId, ReleaseIdentitySource.Manifest);

        return Unknown;
    }

    private static bool TryReadManifest(string path, out string id)
    {
        id = "";
        try
        {
            if (!File.Exists(path)) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var candidate = ReadString(document.RootElement, "commit")
                ?? ReadString(document.RootElement, "tag");
            if (!IsSafeId(candidate)) return false;
            id = candidate!;
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static bool IsSafeId(string? value) =>
        !string.IsNullOrEmpty(value) && SafeIdRegex().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._-]{1,128}$")]
    private static partial Regex SafeIdRegex();
}
