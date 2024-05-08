using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Contracts;
using Elsa.Workflows.Models;

namespace Elsa.Workflows.Serialization.Converters;

/// <summary>
/// Serializes the <see cref="ActivityNode"/> type and its immediate child nodes.
/// </summary>
public class ActivityNodeConverter : JsonConverter<ActivityNode>
{
    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ActivityNode value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("activity");
        WriteActivity(writer, value.Activity, options);
        writer.WritePropertyName("children");
        foreach (var childNode in value.Children)
        {
            WriteActivity(writer, childNode.Activity, options);
        }
        writer.WriteEndObject();
    }

    /// <inheritdoc />
    public override ActivityNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    private void WriteActivity(Utf8JsonWriter writer, IActivity value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        var properties = value?.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance) ?? [];
        
        foreach (var property in properties)
        {
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                continue;
            
            if(typeof(IActivity).IsAssignableFrom(property.PropertyType))
                continue;
            
            if(typeof(IEnumerable<IActivity>).IsAssignableFrom(property.PropertyType))
                continue;
            
            var propName = options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
            writer.WritePropertyName(propName);
            var input = property.GetValue(value);
            
            if (input == null)
            {
                writer.WriteNullValue();
                continue;
            }
            
            JsonSerializer.Serialize(writer, input, options);
        }

        writer.WriteEndObject();
    }
}