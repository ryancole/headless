using System.Text.Json;

namespace Headless.Protocol;

public static class ProtocolJson
{
    /// <summary>snake_case JSON; the "type" discriminator may appear anywhere in an object.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };
}
