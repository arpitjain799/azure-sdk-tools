using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwaggerApiParser;

public class Header : BaseSchema
{
}

public class SchemaTableItem
{
    public String Model { get; set; }
    public String Field { get; set; }
    public String TypeFormat { get; set; }

    public String Keywords { get; set; }
    public String Description { get; set; }


    public CodeFileToken[] TokenSerialize()
    {
        List<CodeFileToken> ret = new List<CodeFileToken>();
        string[] serializedFields = new[] {"Model", "Field", "TypeFormat", "Keywords", "Description"};
        ret.AddRange(this.TokenSerializeWithOptions(serializedFields));
        return ret.ToArray();
    }

    public CodeFileToken[] TokenSerializeWithOptions(string[] serializedFields)
    {
        List<CodeFileToken> ret = new List<CodeFileToken>();
        foreach (var property in this.GetType().GetProperties())
        {
            if (serializedFields.Contains(property.Name))
            {
                ret.AddRange(TokenSerializer.TableCell(new[] {new CodeFileToken(property.GetValue(this, null)?.ToString(), CodeFileTokenKind.Literal)}));
            }
        }

        return ret.ToArray();
    }
}

public class BaseSchema : ITokenSerializable
{
    public string type { get; set; }
    public string description { get; set; }
    public string format { get; set; }
    public string originalRef { get; set; }

    public List<BaseSchema> allOf { get; set; }
    public List<BaseSchema> anyOf { get; set; }

    public List<BaseSchema> oneOf { get; set; }
    public BaseSchema items { get; set; }

    // public Boolean additionalProperties { get; set; }
    public bool readOnly { get; set; }

    public bool writeOnly { get; set; }

    public string discriminator { get; set; }

    [JsonPropertyName("x-ms-nullable")] public bool xMsNullable { get; set; }

    [JsonPropertyName("x-ms-enum")] public XMSEnum xmsEnum { get; set; }

    public Dictionary<string, BaseSchema> properties { get; set; }
    public Dictionary<string, BaseSchema> allOfProperities { get; set; }

    [JsonPropertyName("x-ms-discriminator-value")]
    public string xMsDiscriminatorValue { get; set; }

    public List<string> required { get; set; }

    [JsonPropertyName("enum")] public List<JsonElement> Enum { get; set; }

    public bool IsPropertyRequired(string propertyName)
    {
        return this.required != null && this.required.Contains(propertyName);
    }

    [JsonPropertyName("$ref")] public string Ref { get; set; }

    private List<SchemaTableItem> tableItems;
    private Queue<(BaseSchema, SerializeContext)> propertyQueue = new();


    public bool IsRefObj()
    {
        return this.Ref != null;
    }

    public List<String> GetKeywords()
    {
        List<string> keywords = new List<string>();
        if (this.readOnly)
        {
            keywords.Add("readOnly");
        }

        if (this.writeOnly)
        {
            keywords.Add("writeOnly");
        }

        if (this.xMsNullable)
        {
            keywords.Add("x-ms-nullable");
        }

        if (this.Enum != null && this.Enum.Count > 0)
        {
            keywords.Add($"enum: [{string.Join(",", this.Enum)}]");
        }

        if (this.xmsEnum != null)
        {
            keywords.Add(this.xmsEnum.ToKeywords());
        }


        return keywords;
    }

    public string GetTypeFormat()
    {
        var typeFormat = this.format != null ? $"/{this.format}" : "";


        if (this.type is "array" && this.items is not null)
        {
            var reference = this.items.originalRef ?? this.items.Ref;
            var arrayType = Utils.GetDefinitionType(reference) ?? this.items.type;
            return this.type + $"<{arrayType}>";
        }

        if (this.originalRef != null)
        {
            return Utils.GetDefinitionType(this.originalRef) + typeFormat;
        }

        return this.type + typeFormat;
    }

    private CodeFileToken[] TokenSerializeInternal(SerializeContext context, BaseSchema schema, ref List<SchemaTableItem> flattenedTableItems, Boolean serializeRef = true)
    {
        List<CodeFileToken> ret = new List<CodeFileToken>();
        if (serializeRef)
        {
            ret.Add(new CodeFileToken(Utils.GetDefinitionType(schema.originalRef), CodeFileTokenKind.TypeName));
            flattenedTableItems.Add(new SchemaTableItem() {Model = Utils.GetDefinitionType(schema.originalRef), TypeFormat = schema.type, Description = schema.description});
            ret.Add(TokenSerializer.NewLine());
            context.intent++;
        }


        if (schema.properties?.Count != 0)
        {
            // BUGBUG: Herein lies the problem. We're recursing down into child objects when we should be queuing them instead.
            TokenSerializeProperties(context, schema, schema.properties, ret, ref flattenedTableItems, serializeRef);
        }

        if (schema.allOfProperities?.Count != 0 && schema.allOf != null)
        {
            ret.Add(new CodeFileToken("allOf", CodeFileTokenKind.Keyword));
            ret.Add(TokenSerializer.Colon());
            ret.Add(TokenSerializer.NewLine());
            foreach (var allOfSchema in schema.allOf)
            {
                if (allOfSchema != null)
                {
                    ret.Add(new CodeFileToken(Utils.GetDefinitionType(allOfSchema.Ref), CodeFileTokenKind.TypeName));
                    ret.Add(TokenSerializer.NewLine());
                }
            }

            TokenSerializeProperties(new SerializeContext(context.intent + 2, context.IteratorPath), schema, schema.allOfProperities, ret, ref flattenedTableItems, serializeRef);
        }

        if (schema.type == "array" && schema.items is not null)
        {
            SchemaTableItem arrayItem = new SchemaTableItem {Description = schema.description};
            arrayItem.TypeFormat = schema.items.type != null ? $"array<{schema.items.type}>" : $"array<{Utils.GetDefinitionType(schema.items.originalRef)}>";
            flattenedTableItems.Add(arrayItem);
            TokenSerializeArray(context, ret, schema, ref flattenedTableItems, serializeRef);
        }

        if (schema.type == "string")
        {
            if (schema.Enum != null)
            {
                SchemaTableItem enumItem = new SchemaTableItem {Description = schema.description};
                const string enumType = "enum<string>";
                enumItem.TypeFormat = enumType;
                if (schema.xmsEnum != null)
                {
                    enumItem.Keywords = string.Join(",", schema.GetKeywords());
                }

                flattenedTableItems.Add(enumItem);
            }
        }

        // Now recurse into nested model definitions so all properties are grouped with their models.
        while (this.propertyQueue.TryDequeue(out var property))
        {
            var (item, childContext) = property;
            ret.AddRange(item.TokenSerializeInternal(childContext, item, ref flattenedTableItems, serializeRef));
        }

        return ret.ToArray();
    }

    private static List<string> GetPropertyKeywordsFromBaseSchema(BaseSchema baseSchema, string propertyName, BaseSchema schema)
    {
        var keywords = new HashSet<string>();
        if (baseSchema.IsPropertyRequired(propertyName))
        {
            keywords.Add("required");
        }

        foreach (var it in schema.GetKeywords())
        {
            keywords.Add(it);
        }

        return keywords.ToList();
    }

    private void TokenSerializeProperties(SerializeContext context, BaseSchema schema, Dictionary<string, BaseSchema> properties, List<CodeFileToken> ret, ref List<SchemaTableItem> flattenedTableItems,
        Boolean serializeRef = true)
    {
        if (properties == null)
        {
            return;
        }

        foreach (var kv in properties)
        {
            ret.Add(new CodeFileToken(kv.Key, CodeFileTokenKind.Literal));
            ret.Add(TokenSerializer.Colon());
            if (kv.Value == null)
            {
                continue;
            }

            // Normal case: If properties is has values. Serialize each key value pair in properties.
            if ((kv.Value.properties != null && kv.Value.properties?.Count != 0))
            {
                var keywords = GetPropertyKeywordsFromBaseSchema(schema, kv.Key, kv.Value);
                SchemaTableItem item = new SchemaTableItem {Field = kv.Key, Description = kv.Value.description, Keywords = String.Join(",", keywords), TypeFormat = kv.Value.GetTypeFormat()};
                flattenedTableItems.Add(item);
                ret.Add(TokenSerializer.NewLine());
                if (serializeRef)
                {
                    this.propertyQueue.Enqueue((kv.Value, new SerializeContext(context.intent + 1, context.IteratorPath)));
                }
            }
            // Circular reference case: the ref won't be expanded. 
            else if (kv.Value.Ref != null)
            {
                ret.Add(TokenSerializer.NewLine());
                ret.Add(new CodeFileToken("<", CodeFileTokenKind.Punctuation));
                var refName = kv.Value.Ref;
                ret.Add(new CodeFileToken(refName.Split("/").Last(), CodeFileTokenKind.TypeName));
                ret.Add(new CodeFileToken(">", CodeFileTokenKind.Punctuation));
            }
            // Array case: Serialize array.
            else if (kv.Value.type == "array")
            {
                SchemaTableItem arrayItem = new SchemaTableItem();
                arrayItem.Field = kv.Key;
                arrayItem.Description = kv.Value.description;
                var arrayType = "array";
                if (kv.Value.items != null)
                {
                    arrayType = (kv.Value.items.originalRef == null && kv.Value.items.Ref == null)
                        ? $"array<{kv.Value.items.type}>"
                        : $"array<{Utils.GetDefinitionType(kv.Value.items.originalRef ?? Utils.GetDefinitionType(kv.Value.items.Ref))}>";
                }

                arrayItem.TypeFormat = arrayType;
                var keywords = GetPropertyKeywordsFromBaseSchema(schema, kv.Key, kv.Value);
                arrayItem.Keywords = string.Join(",", keywords);
                flattenedTableItems.Add(arrayItem);
                TokenSerializeArray(context, ret, kv.Value, ref flattenedTableItems, serializeRef);
            }
            else
            {
                var keywords = GetPropertyKeywordsFromBaseSchema(schema, kv.Key, kv.Value);
                SchemaTableItem item = new SchemaTableItem {Field = kv.Key, Description = kv.Value.description, TypeFormat = kv.Value.GetTypeFormat(), Keywords = string.Join(",", keywords)};
                flattenedTableItems.Add(item);
                ret.Add(new CodeFileToken(kv.Value.type, CodeFileTokenKind.Keyword));
                ret.Add(TokenSerializer.NewLine());
            }
        }
    }

    private void TokenSerializeArray(SerializeContext context, List<CodeFileToken> ret, BaseSchema arraySchema, ref List<SchemaTableItem> flattenedTableItems, Boolean serializeRef)
    {
        ret.Add(new CodeFileToken("array", CodeFileTokenKind.Keyword));
        if (arraySchema.items == null)
        {
            return;
        }

        if (arraySchema.items.type != null && arraySchema.items.type != "object")
        {
            ret.Add(new CodeFileToken("<", CodeFileTokenKind.Punctuation));
            ret.Add(new CodeFileToken(arraySchema.items.type, CodeFileTokenKind.TypeName));
            ret.Add(new CodeFileToken(">", CodeFileTokenKind.Punctuation));
            ret.Add(TokenSerializer.NewLine());
        }
        else
        {
            ret.Add(new CodeFileToken("<", CodeFileTokenKind.Punctuation));
            var refName = arraySchema.items.originalRef ?? arraySchema.items.Ref ?? "";
            ret.Add(new CodeFileToken(refName.Split("/").Last(), CodeFileTokenKind.TypeName));
            ret.Add(new CodeFileToken(">", CodeFileTokenKind.Punctuation));
            ret.Add(TokenSerializer.NewLine());

            // circular reference
            if (arraySchema.items.Ref != null)
            {
                return;
            }

            if (serializeRef)
            {
                this.propertyQueue.Enqueue((arraySchema.items, new SerializeContext(context.intent + 1, context.IteratorPath)));
            }
        }
    }

    public void TokenSerializePropertyIntoTableItems(SerializeContext context, ref List<SchemaTableItem> retTableItems, Boolean serializeRef = true, string[] columns = null)
    {
        if (retTableItems == null)
        {
            retTableItems = new List<SchemaTableItem>();
            this.TokenSerializeInternal(context, this, ref retTableItems, serializeRef);
        }
    }

    public CodeFileToken[] TokenSerialize(SerializeContext context)
    {
        string[] columns = new[] {"Model", "Field", "Type/Format", "Keywords", "Description"};
        this.TokenSerializePropertyIntoTableItems(context, ref this.tableItems);
        var tableRet = new List<CodeFileToken>();

        var tableRows = new List<CodeFileToken>();
        foreach (var tableItem in this.tableItems)
        {
            tableRows.AddRange(tableItem.TokenSerialize());
        }

        tableRet.AddRange(TokenSerializer.TokenSerializeAsTableFormat(this.tableItems.Count, columns.Length, columns, tableRows.ToArray(), context.IteratorPath.CurrentNextPath("table")));
        tableRet.Add(TokenSerializer.NewLine());
        return tableRet.ToArray();
    }
}
