using System.Text.Json;

namespace Envoy.Discovery.Internal;

internal static class Json
{
    public static string Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public static bool TryObj(JsonElement el, string prop, out JsonElement obj)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Object)
        {
            obj = v;
            return true;
        }
        obj = default;
        return false;
    }

    public static long Long(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : 0;

    public static bool Bool(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) && v.GetBoolean();
}
