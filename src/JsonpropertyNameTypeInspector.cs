using System.Text.Json.Serialization; // Required for JsonPropertyName
using YamlDotNet.Serialization.TypeInspectors;

namespace YamlDotNet.Serialization
{
    /// <summary>
    /// Applies the JsonPropertyName attributes to another <see cref="ITypeInspector"/>.
    /// </summary>
    public sealed class JsonPropertyNameTypeInspector : TypeInspectorSkeleton
    {
        private readonly ITypeInspector _innerTypeInspector;

        public JsonPropertyNameTypeInspector(ITypeInspector innerTypeInspector)
        {
            _innerTypeInspector = innerTypeInspector;
        }

        public override IEnumerable<IPropertyDescriptor> GetProperties(Type type, object? container)
        {
            return _innerTypeInspector.GetProperties(type, container)
                .Select(p =>
                {
                    var descriptor = new PropertyDescriptor(p);
                    var jsonProperty = p.GetCustomAttribute<JsonPropertyNameAttribute>();

                    if (jsonProperty != null)
                    {
                        descriptor.Name = jsonProperty.Name;
                    }

                    return (IPropertyDescriptor)descriptor;
                })
                .OrderBy(p => p.Order);
        }
    }
}
