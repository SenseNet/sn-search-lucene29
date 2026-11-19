using System.Xml;
using System.Xml.Linq;

namespace IndexIntegrityChecker;

public sealed record NodeTypeDefinition(int Id, int? ParentId, string Name);

public static class ContentTypePolicy
{
    public static bool? ReadAllowIndexing(Stream xml)
    {
        using var reader = XmlReader.Create(xml, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024
        });
        var root = XDocument.Load(reader).Root;
        if (root == null || root.Name.LocalName != "ContentType")
            throw new InvalidDataException("Invalid content type definition: expected a ContentType root element.");
        var value = root.Elements().FirstOrDefault(e => e.Name.LocalName == "AllowIndexing")?.Value.Trim().ToLowerInvariant();
        return value switch { "true" or "yes" or "1" => true, "false" or "no" or "0" => false, _ => null };
    }

    public static HashSet<int> GetExcluded(IEnumerable<NodeTypeDefinition> types, IReadOnlyDictionary<string, bool?> definitions)
    {
        var byId = types.ToDictionary(t => t.Id);
        var resolved = new Dictionary<int, bool>();
        var visiting = new HashSet<int>();
        bool Enabled(int id)
        {
            if (resolved.TryGetValue(id, out var result)) return result;
            if (!byId.TryGetValue(id, out var type)) throw new InvalidDataException($"Unknown parent node type: {id}.");
            if (!visiting.Add(id)) throw new InvalidDataException($"Cyclic node type inheritance at {type.Name}.");
            var explicitValue = definitions.GetValueOrDefault(type.Name);
            result = explicitValue ?? (type.ParentId is > 0 ? Enabled(type.ParentId.Value) : true);
            visiting.Remove(id);
            resolved[id] = result;
            return result;
        }
        return byId.Keys.Where(id => !Enabled(id)).ToHashSet();
    }
}
