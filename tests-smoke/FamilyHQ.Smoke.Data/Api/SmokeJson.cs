using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyHQ.Smoke.Data.Api;

/// <summary>
/// The JSON contract the smoke suite reads and writes with.
/// <para>
/// Case-insensitive on the way in because preprod answers camelCase and Google answers camelCase, and
/// null-suppressing on the way out because Google treats a present-but-null member as "clear this
/// field" — sending one on an insert would mean the suite asked for something it did not intend.
/// </para>
/// </summary>
public static class SmokeJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
