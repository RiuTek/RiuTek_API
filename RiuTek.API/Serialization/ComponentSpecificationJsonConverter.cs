using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using RiuTek.Core.Entities.Specifications;

namespace RiuTek.API.Serialization;

public class ComponentSpecificationJsonConverter : JsonConverter<ComponentSpecification>
{
    private static readonly Dictionary<string, Type> DiscriminatorToType;
    private static readonly Dictionary<Type, string> TypeToDiscriminator;

    static ComponentSpecificationJsonConverter()
    {
        var attributes = typeof(ComponentSpecification)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .ToList();

        // 1. Fail fast: No JsonDerivedTypeAttribute declared
        if (attributes.Count == 0)
        {
            throw new InvalidOperationException(
                "ComponentSpecification metadata configuration error: No JsonDerivedTypeAttribute declared on ComponentSpecification.");
        }

        var discToType = new Dictionary<string, Type>(StringComparer.Ordinal);
        var typeToDisc = new Dictionary<Type, string>();

        foreach (var attr in attributes)
        {
            // 2. Fail fast: Discriminator must be a non-empty, non-whitespace string
            if (attr.TypeDiscriminator is not string discriminator || string.IsNullOrWhiteSpace(discriminator))
            {
                throw new InvalidOperationException(
                    "ComponentSpecification metadata configuration error: TypeDiscriminator must be a non-empty string.");
            }

            var derivedType = attr.DerivedType;

            // 3. Fail fast: Derived type must inherit from ComponentSpecification
            if (derivedType == null || !typeof(ComponentSpecification).IsAssignableFrom(derivedType))
            {
                throw new InvalidOperationException(
                    "ComponentSpecification metadata configuration error: DerivedType must inherit from ComponentSpecification.");
            }

            // 4. Fail fast: Derived type must be concrete
            if (derivedType.IsAbstract || derivedType.IsInterface)
            {
                throw new InvalidOperationException(
                    "ComponentSpecification metadata configuration error: DerivedType must be a concrete type.");
            }

            // 5. Fail fast: Duplicate discriminator
            if (!discToType.TryAdd(discriminator, derivedType))
            {
                throw new InvalidOperationException(
                    "ComponentSpecification metadata configuration error: Duplicate type discriminator declared.");
            }

            // 6. Fail fast: Multiple discriminators mapped to the same derived type
            if (!typeToDisc.TryAdd(derivedType, discriminator))
            {
                throw new InvalidOperationException(
                    "ComponentSpecification metadata configuration error: Multiple discriminators mapped to the same derived type.");
            }
        }

        DiscriminatorToType = discToType;
        TypeToDiscriminator = typeToDisc;
    }

    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(ComponentSpecification);

    public override ComponentSpecification? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object for component specification.");
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("$type", out var typeProp))
        {
            throw new JsonException("Missing required type discriminator.");
        }

        if (typeProp.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Type discriminator must be a valid string.");
        }

        var discriminator = typeProp.GetString();
        if (string.IsNullOrWhiteSpace(discriminator))
        {
            throw new JsonException("Type discriminator must not be empty.");
        }

        if (!DiscriminatorToType.TryGetValue(discriminator, out var targetType))
        {
            throw new JsonException("Unsupported type discriminator.");
        }

        var rawText = root.GetRawText();
        var result = (ComponentSpecification?)JsonSerializer.Deserialize(rawText, targetType, options);
        if (result == null)
        {
            throw new JsonException("Failed to deserialize component specification.");
        }

        return result;
    }

    public override void Write(
        Utf8JsonWriter writer,
        ComponentSpecification value,
        JsonSerializerOptions options)
    {
        if (!TypeToDiscriminator.TryGetValue(value.GetType(), out var discriminator))
        {
            throw new JsonException("Unsupported component specification type.");
        }

        using var doc = JsonSerializer.SerializeToDocument(value, value.GetType(), options);
        writer.WriteStartObject();
        writer.WriteString("$type", discriminator);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.NameEquals("$type"))
            {
                continue;
            }
            property.WriteTo(writer);
        }
        writer.WriteEndObject();
    }
}
