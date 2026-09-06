using System.Text.Json;
using System.Text.Json.Serialization;
using RiuTek.Core.Entities.Specifications;

namespace RiuTek.API.Serialization;

public class ComponentSpecificationJsonConverter : JsonConverter<ComponentSpecification>
{
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

        Type targetType = discriminator switch
        {
            "cpu" => typeof(CpuSpecification),
            "motherboard" => typeof(MotherboardSpecification),
            "gpu" => typeof(GpuSpecification),
            "ram" => typeof(RamSpecification),
            "storage" => typeof(StorageSpecification),
            "psu" => typeof(PsuSpecification),
            "case" => typeof(CaseSpecification),
            "cooler" => typeof(CoolerSpecification),
            "accessory" => typeof(AccessorySpecification),
            _ => throw new JsonException("Unsupported type discriminator.")
        };

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
        var discriminator = value switch
        {
            CpuSpecification => "cpu",
            MotherboardSpecification => "motherboard",
            GpuSpecification => "gpu",
            RamSpecification => "ram",
            StorageSpecification => "storage",
            PsuSpecification => "psu",
            CaseSpecification => "case",
            CoolerSpecification => "cooler",
            AccessorySpecification => "accessory",
            _ => throw new JsonException("Unsupported component specification type.")
        };

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
