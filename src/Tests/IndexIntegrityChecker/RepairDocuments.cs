using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;

namespace IndexIntegrityChecker;

// TODO(refactor): Experimental repair data contract; consolidate with supported indexing APIs after validation.
internal sealed record RepairVersion(int VersionId, int NodeId, int NodeTypeId, int ParentId, string Path,
    bool IsSystem, long NodeTimestamp, long VersionTimestamp, int? LastPublic, int? LastDraft,
    string? SerializedDocument, bool Excluded = false, int Status = 1);

// TODO(refactor): Experimental field metadata; replace with a supported offline schema API.
internal sealed record RepairFieldRule(string? Mode, string? Store, string? TermVector, string Analyzer, string? Error = null, string? DeclaredAnalyzer = null);

// TODO(refactor): Experimental offline schema; extract a supported shared resolver after the experiment.
internal sealed class RepairSchema
{
    private readonly Dictionary<string, RepairFieldRule> _fields = new(StringComparer.Ordinal);

    // TODO(refactor): Local adaptation of sensenet@91be9c5c3f7d5f5aa03c9a72cde4a935f106584f
    // src/ContentRepository/Schema/FieldDescriptor.cs::Parse and ContentTypeManager.cs::SetPerFieldIndexingInfo.
    // Adaptation: parse CTDs/aspects without loading repository field handlers; reject unresolved settings.
    internal void AddDefinition(string xml, string prefix = "")
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("Empty indexing definition.");
        var fields = root.Elements().SingleOrDefault(x => x.Name.LocalName == "Fields");
        if (fields == null) return;
        foreach (var field in fields.Elements().Where(x => x.Name.LocalName is "Field" or "AspectField" or "ContentListField"))
        {
            var name = prefix + (string?)field.Attribute("name");
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("An indexing field has no name.");
            var indexing = field.Elements().SingleOrDefault(x => x.Name.LocalName == "Indexing");
            string? Setting(string key)
            {
                var value = indexing?.Elements().SingleOrDefault(x => x.Name.LocalName == key)?.Value.Trim();
                return key == "IndexHandler" ? value : Normalize(value);
            }
            var analyzer = Setting("Analyzer");
            var declaredAnalyzer = analyzer is null or "" or "Default" ? null : analyzer;
            var fieldType = (string?)field.Attribute("type");
            var fieldHandler = (string?)field.Attribute("handler");
            if (string.IsNullOrEmpty(fieldType) && fieldHandler?.StartsWith("SenseNet.ContentRepository.Fields.", StringComparison.Ordinal) == true)
                fieldType = fieldHandler.Split(',')[0].Split('.').Last().Replace("Field", "", StringComparison.Ordinal);
            var handler = Setting("IndexHandler");
            var defaultAnalyzer = DefaultAnalyzer(fieldType, handler);
            string? error = defaultAnalyzer == null ? $"Unsupported handler for field '{name}' ({handler ?? fieldHandler ?? fieldType})." : null;
            if (!string.IsNullOrEmpty(fieldHandler) && fieldHandler.Split(',')[0].Trim() != $"SenseNet.ContentRepository.Fields.{fieldType}Field")
                error = $"Custom field handler for '{name}' is unavailable offline: {fieldHandler}.";
            var configurationHandler = (string?)field.Elements().SingleOrDefault(x => x.Name.LocalName == "Configuration")?.Attribute("handler");
            if (!string.IsNullOrEmpty(configurationHandler)) error = $"Custom field setting for '{name}' is unavailable offline: {configurationHandler}.";
            if (analyzer is null or "" or "Default") analyzer = defaultAnalyzer ?? "Keyword";
            if (analyzer is not ("Keyword" or "Standard" or "Whitespace")) error = $"Unsupported analyzer for field '{name}': {analyzer}.";
            var rule = new RepairFieldRule(Setting("Mode"), Setting("Store"), Setting("TermVector"), analyzer, error, declaredAnalyzer);
            if (_fields.TryGetValue(name, out var existing))
            {
                rule = rule with
                {
                    Mode = Merge(existing.Mode, rule.Mode, name), Store = Merge(existing.Store, rule.Store, name),
                    TermVector = Merge(existing.TermVector, rule.TermVector, name),
                    Analyzer = existing.DeclaredAnalyzer ?? rule.DeclaredAnalyzer ?? existing.Analyzer,
                    DeclaredAnalyzer = existing.DeclaredAnalyzer ?? rule.DeclaredAnalyzer,
                    Error = existing.Error ?? rule.Error ?? (existing.DeclaredAnalyzer != null && rule.DeclaredAnalyzer != null && existing.DeclaredAnalyzer != rule.DeclaredAnalyzer
                        ? $"Conflicting analyzers for '{name}'." : null)
                };
            }
            _fields[name] = rule;
        }
    }

    // TODO(refactor): Experimental XML enum normalization; consolidate with the canonical CTD parser later.
    private static string? Normalize(string? value) => value?.ToLowerInvariant() switch
    {
        null or "" => null, "default" => "Default", "yes" => "Yes", "no" => "No",
        "analyzed" => "Analyzed", "analyzednonorms" => "AnalyzedNoNorms",
        "notanalyzed" => "NotAnalyzed", "notanalyzednonorms" => "NotAnalyzedNoNorms",
        "withoffsets" => "WithOffsets", "withpositions" => "WithPositions", "withpositionsoffsets" => "WithPositionsOffsets",
        "keyword" => "Keyword", "standard" => "Standard", "whitespace" => "Whitespace", _ => value
    };

    // TODO(refactor): Experimental adaptation of sensenet@91be9c5c3f7d5f5aa03c9a72cde4a935f106584f
    // src/ContentRepository/Schema/ContentTypeManager.cs::SetPerFieldIndexingInfo; share validation after the experiment.
    private static string? Merge(string? oldValue, string? value, string name)
    {
        if (string.IsNullOrEmpty(oldValue) || oldValue == "Default") return value;
        if (!string.IsNullOrEmpty(value) && value != "Default" && value != oldValue)
            throw new InvalidDataException($"Conflicting indexing settings for '{name}'.");
        return oldValue;
    }

    // TODO(refactor): Experimental copy of defaults from sensenet@91be9c5c3f7d5f5aa03c9a72cde4a935f106584f
    // src/ContentRepository/Fields/*FieldSetting.cs::CreateDefaultIndexFieldHandler and
    // src/ContentRepository/Search/Indexing/FieldIndexHandler.cs::GetDefaultAnalyzer.
    // Sharing defaults: Fields/SharingField.cs selects NullFieldSetting, whose fallback is
    // Schema/FieldSetting.cs::CreateDefaultIndexFieldHandler (LowerStringIndexHandler/Keyword).
    // Search/Indexing/FieldIndexHandlers/SharingIndexHandler.cs: all five handlers inherit Keyword;
    // GetIndexFields already stores Sharing token combinations in the SQL index document.
    // Replay those serialized terms; do not regenerate them from repository sharing objects.
    // Adaptation: explicit whitelist instead of repository reflection; publish shared defaults later.
    private static string? DefaultAnalyzer(string? type, string? handler)
    {
        if (!string.IsNullOrWhiteSpace(handler))
        {
            var full = handler.Split(',')[0].Trim();
            if (!full.StartsWith("SenseNet.Search.Indexing.", StringComparison.Ordinal)) return null;
            return full.Split('.').Last() switch
            {
                "LongTextIndexHandler" or "RichTextIndexHandler" or "BinaryIndexHandler" or "GeneralJsonIndexHandler" => "Standard",
                "LowerStringIndexHandler" or "BooleanIndexHandler" or "IntegerIndexHandler" or "NumberIndexHandler" or
                "DateTimeIndexHandler" or "ReferenceIndexHandler" or "ChoiceIndexHandler" or "PermissionChoiceIndexHandler" or
                "HyperLinkIndexHandler" or "TypeTreeIndexHandler" or "ContentTypeEnumerableIndexHandler" or
                "ExclusiveTypeIndexHandler" or "InFolderIndexHandler" or "InTreeIndexHandler" or "TagIndexHandler" or
                "SystemContentIndexHandler" or "NotIndexedIndexFieldHandler" or
                "SharingIndexHandler" or "SharedWithIndexHandler" or "SharedByIndexHandler" or
                "SharingModeIndexHandler" or "SharingLevelIndexHandler" => "Keyword",
                _ => null
            };
        }
        return type switch
        {
            "LongText" or "RichText" or "Binary" => "Standard",
            "ShortText" or "Integer" or "Number" or "Currency" or "Boolean" or "DateTime" or "Choice" or
            "Reference" or "HyperLink" or "Password" or "WhoAndWhen" or "Version" or "Null" or "Color" or
            "PermissionChoice" or "AllowedChildTypes" or "Rating" or "AllRoles" or "DirectRoles" or "ApprovingMode" or
            "InheritableApprovingMode" or "InheritableVersioningMode" or "VersioningMode" or "Image" or "Lock" or
            "Security" or "UrlList" or "SiteRelativeUrl" or "NodeType" or "Captcha" or "Sharing" => "Keyword",
            _ => null
        };
    }

    // TODO(refactor): Experimental offline field lookup; replace with the shared schema resolver after validation.
    internal RepairFieldRule Get(string name, bool required = false)
    {
        if (_fields.TryGetValue(name, out var rule))
        {
            if (rule.Error != null) throw new InvalidDataException(rule.Error);
            return rule;
        }
        if (required || name.Contains('.') || name.StartsWith('#'))
            throw new InvalidDataException($"Indexing settings are unavailable offline for field '{name}'.");
        // The normal Lucene wrapper uses Keyword for generated fields absent from the schema.
        return new(null, null, null, "Keyword");
    }

    // TODO(refactor): Experimental local adaptation of sn-search-lucene29@a90399d7599b090c92781dedefa3c9fd657adaf6
    // src/SenseNet.Search.Lucene29/SnPerFieldAnalyzerWrapper.cs::GetAnalyzer.
    // Adaptation: immutable per-document wrapper and explicit supported analyzers; share after the experiment.
    internal Analyzer CreateAnalyzer(IEnumerable<string> names)
    {
        var wrapper = new PerFieldAnalyzerWrapper(new KeywordAnalyzer());
        foreach (var name in names.Distinct(StringComparer.Ordinal))
        {
            var analyzer = name == "_Text" ? "Standard" : Get(name).Analyzer;
            wrapper.AddAnalyzer(name, analyzer switch
            {
                "Standard" => new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29),
                "Whitespace" => new WhitespaceAnalyzer(),
                "Keyword" => new KeywordAnalyzer(),
                _ => throw new InvalidDataException($"Unsupported analyzer for '{name}'.")
            });
        }
        return wrapper;
    }
}

// TODO(refactor): Experimental local document conversion; replace with supported offline indexing APIs.
internal static class RepairDocuments
{
    // TODO(refactor): Experimental adaptation of sensenet@91be9c5c3f7d5f5aa03c9a72cde4a935f106584f
    // src/Search/Indexing/IndexDocument.cs::Deserialize and
    // src/ContentRepository/Search/Indexing/IndexManager.cs::CompleteIndexDocument/GetParentPaths.
    // Adaptation: System.Text.Json and explicit SQL metadata instead of repository objects; share later.
    internal static Document Convert(RepairVersion version, RepairSchema schema)
    {
        if (string.IsNullOrWhiteSpace(version.SerializedDocument))
            throw new InvalidDataException($"SQL index document is missing for version {version.VersionId}.");
        using var serialized = JsonDocument.Parse(version.SerializedDocument);
        if (serialized.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("SQL index document must be an array.");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in serialized.RootElement.EnumerateArray())
        {
            var properties = new HashSet<string>();
            foreach (var property in field.EnumerateObject())
                if (!properties.Add(property.Name) || property.Name is not ("Name" or "Type" or "Mode" or "Store" or "TermVector" or "Value"))
                    throw new InvalidDataException("Invalid or duplicate SQL index field property.");
            var name = field.GetProperty("Name").GetString();
            if (string.IsNullOrEmpty(name) || !fields.TryAdd(name, field)) throw new InvalidDataException("Invalid or duplicate SQL index field.");
        }
        foreach (var (name, expected) in new[] { ("Id", version.NodeId), ("VersionId", version.VersionId) })
            if (!fields.TryGetValue(name, out var field) || field.GetProperty("Type").GetString() != "Int" ||
                field.GetProperty("Value").GetInt32() != expected || Optional(field, "Mode") == "No")
                throw new InvalidDataException($"SQL index document {name} mismatch for version {version.VersionId}.");
        if (version.Path.Length == 0 || !version.Path.StartsWith('/') || version.NodeId <= 0 || version.VersionId <= 0)
            throw new InvalidDataException($"Invalid SQL metadata for version {version.VersionId}.");
        if (version.LastPublic == version.VersionId && version.Status != 1)
            throw new InvalidDataException($"SQL last public version {version.VersionId} is not Approved; SQL correction is outside this tool.");
        var doc = new Document();
        var completed = new HashSet<string>(["Name", "Path", "Depth", "InTree", "InFolder", "ParentId", "IsSystem", "IsLastPublic", "IsLastDraft", "NodeTimestamp", "VersionTimestamp"]);
        foreach (var (name, field) in fields)
        {
            if (completed.Contains(name)) continue;
            schema.Get(name);
            AddField(doc, name, field.GetProperty("Type").GetString()!, field.GetProperty("Value"),
                Optional(field, "Mode"), Optional(field, "Store"), Optional(field, "TermVector"));
        }
        var path = version.Path.ToLowerInvariant();
        var slash = path.LastIndexOf('/');
        var parent = slash <= 0 ? "/" : path[..slash];
        var paths = path.Split('/');
        var ancestors = Enumerable.Range(0, paths.Length).Select(i => string.Join('/', paths.Take(i + 1))).ToArray();
        foreach (var (name, value) in new (string, object)[] { ("Name", path[(slash + 1)..]), ("Path", path), ("InFolder", parent), ("InTree", ancestors) })
        {
            var rule = schema.Get(name, required: true);
            AddField(doc, name, value is string[] ? "StringArray" : "String", JsonSerializer.SerializeToElement(value), rule.Mode, rule.Store, rule.TermVector);
        }
        AddField(doc, "Depth", "Int", JsonSerializer.SerializeToElement(path.Count(c => c == '/') - 1), "AnalyzedNoNorms", "Yes", "No");
        AddField(doc, "ParentId", "Int", JsonSerializer.SerializeToElement(version.ParentId), "AnalyzedNoNorms", "No", "No");
        foreach (var (name, value) in new[] { ("IsSystem", version.IsSystem), ("IsLastPublic", version.LastPublic == version.VersionId), ("IsLastDraft", version.LastDraft == version.VersionId) })
            AddField(doc, name, "Bool", JsonSerializer.SerializeToElement(value), "NotAnalyzed", "Yes", "No");
        foreach (var (name, value) in new[] { ("NodeTimestamp", version.NodeTimestamp), ("VersionTimestamp", version.VersionTimestamp) })
            AddField(doc, name, "Long", JsonSerializer.SerializeToElement(value), "AnalyzedNoNorms", "Yes", "No");
        return doc;
    }

    // TODO(refactor): Experimental JSON adapter; consolidate with the canonical index document parser.
    private static string? Optional(JsonElement field, string name) => field.TryGetProperty(name, out var value) ? value.GetString() : null;

    // TODO(refactor): Experimental local adaptation of sn-search-lucene29@a90399d7599b090c92781dedefa3c9fd657adaf6
    // src/SenseNet.Search.Lucene29/LuceneSearchManager.cs::AddFieldToDocument and EnumConverter.cs.
    // Adaptation: serialized field values instead of SenseNet types; extract supported conversion later.
    private static void AddField(Document doc, string name, string type, JsonElement value, string? mode, string? store, string? vector)
    {
        var storing = store switch { null or "Default" or "No" => Field.Store.NO, "Yes" => Field.Store.YES, _ => throw new InvalidDataException($"Invalid Store: {store}.") };
        var indexing = mode switch
        {
            null or "Default" or "Analyzed" => Field.Index.ANALYZED, "AnalyzedNoNorms" => Field.Index.ANALYZED_NO_NORMS,
            "NotAnalyzed" => Field.Index.NOT_ANALYZED, "NotAnalyzedNoNorms" => Field.Index.NOT_ANALYZED_NO_NORMS,
            "No" => Field.Index.NO, _ => throw new InvalidDataException($"Invalid Mode: {mode}.")
        };
        var termVector = vector switch
        {
            null or "Default" or "No" => Field.TermVector.NO, "Yes" => Field.TermVector.YES,
            "WithOffsets" => Field.TermVector.WITH_OFFSETS, "WithPositions" => Field.TermVector.WITH_POSITIONS,
            "WithPositionsOffsets" => Field.TermVector.WITH_POSITIONS_OFFSETS,
            _ => throw new InvalidDataException($"Invalid TermVector: {vector}.")
        };
        switch (type)
        {
            case "String": doc.Add(new Field(name, value.GetString() ?? "", storing, indexing, termVector)); break;
            case "StringArray": foreach (var v in value.EnumerateArray()) doc.Add(new Field(name, v.GetString() ?? "", storing, indexing, termVector)); break;
            case "Bool": doc.Add(new Field(name, value.GetBoolean() ? "yes" : "no", storing, indexing, termVector)); break;
            case "Int": doc.Add(new NumericField(name, storing, mode != "No").SetIntValue(value.GetInt32())); break;
            case "IntArray": foreach (var v in value.EnumerateArray()) doc.Add(new NumericField(name, storing, mode != "No").SetIntValue(v.GetInt32())); break;
            case "Long": doc.Add(new NumericField(name, storing, mode != "No").SetLongValue(value.GetInt64())); break;
            case "Float": doc.Add(new NumericField(name, storing, mode != "No").SetFloatValue(value.GetSingle())); break;
            case "Double": doc.Add(new NumericField(name, storing, mode != "No").SetDoubleValue(value.GetDouble())); break;
            case "DateTime": doc.Add(new NumericField(name, storing, mode != "No").SetLongValue(DateTime.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).Ticks)); break;
            default: throw new InvalidDataException($"Unsupported index value type: {type}.");
        }
    }
}
