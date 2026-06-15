using System.Globalization;
using System.Text;
using Humanizer;

namespace Tankerkiller125.BicepExtensionGen.Generation;

/// <summary>Helpers for turning arbitrary OpenAPI names into valid, idiomatic C# identifiers.</summary>
public static class NameUtil
{
    /// <summary>
    /// Common BCL type names that are in scope via <c>ImplicitUsings</c>. Generated type and
    /// resource names that match these are suffixed to avoid CS0104 ambiguous references.
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedTypeNames = new HashSet<string>(StringComparer.Ordinal)
    {
        // System core
        "Object", "String", "Boolean", "Byte", "SByte", "Char", "Decimal", "Double", "Single",
        "Int16", "Int32", "Int64", "UInt16", "UInt32", "UInt64", "IntPtr", "UIntPtr",
        "Type", "Enum", "Array", "Attribute", "Exception", "Nullable", "Tuple", "ValueType",
        "Delegate", "Math", "Convert", "Guid", "DateTime", "DateTimeOffset", "TimeSpan",
        "DateOnly", "TimeOnly", "Uri", "Version", "Random", "Buffer", "Activator", "Environment",
        "Console", "GC", "Index", "Range", "Span", "Memory", "Action", "Func", "Predicate",
        "Comparison", "EventHandler", "Lazy", "Progress",
        // Collections
        "List", "Dictionary", "HashSet", "Queue", "Stack", "IEnumerable", "ICollection", "IList",
        "IDictionary", "KeyValuePair", "SortedList", "LinkedList", "Comparer", "EqualityComparer",
        // Threading / IO / Net
        "Task", "ValueTask", "Thread", "Timer", "Monitor", "Mutex", "Semaphore", "CancellationToken",
        "Stream", "File", "Directory", "Path", "FileInfo", "DirectoryInfo", "TextReader",
        "TextWriter", "Encoding", "HttpClient", "HttpMethod", "HttpRequestMessage", "Socket", "IPAddress",
        // Json
        "JsonSerializer", "JsonElement", "JsonDocument", "JsonNode",
    };

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
    };

    /// <summary>Splits an identifier on non-alphanumeric boundaries and camelCase humps.</summary>
    private static IEnumerable<string> SplitWords(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            yield break;

        var word = new StringBuilder();
        char prev = '\0';
        foreach (var ch in raw)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                if (word.Length > 0) { yield return word.ToString(); word.Clear(); }
                prev = ch;
                continue;
            }

            // Break on lower->upper hump (e.g. "petId" -> "pet","Id").
            if (word.Length > 0 && char.IsUpper(ch) && char.IsLower(prev))
            {
                yield return word.ToString();
                word.Clear();
            }

            word.Append(ch);
            prev = ch;
        }

        if (word.Length > 0)
            yield return word.ToString();
    }

    /// <summary>Converts an arbitrary string to a PascalCase C# identifier.</summary>
    public static string Pascal(string raw)
    {
        var sb = new StringBuilder();
        foreach (var w in SplitWords(raw))
        {
            sb.Append(char.ToUpper(w[0], CultureInfo.InvariantCulture));
            if (w.Length > 1)
                sb.Append(w[1..].ToLower(CultureInfo.InvariantCulture) is var lower && IsAllCaps(w) ? lower : w[1..]);
        }

        var result = sb.ToString();
        if (result.Length == 0)
            result = "Item";
        if (char.IsDigit(result[0]))
            result = "_" + result;
        return result;
    }

    private static bool IsAllCaps(string w) => w.All(c => !char.IsLower(c));

    /// <summary>Converts an arbitrary string to a camelCase C# identifier (escaped if a keyword).</summary>
    public static string Camel(string raw)
    {
        var pascal = Pascal(raw);
        var camel = char.ToLower(pascal[0], CultureInfo.InvariantCulture) + pascal[1..];
        return Keywords.Contains(camel) ? "@" + camel : camel;
    }

    /// <summary>Singularizes a collection name, e.g. "pets" -> "Pet", "people" -> "Person".</summary>
    public static string SingularPascal(string raw)
    {
        var pascal = Pascal(raw);
        try
        {
            return Pascal(pascal.Singularize(inputIsKnownToBePlural: false));
        }
        catch
        {
            return pascal;
        }
    }

    /// <summary>Ensures the value is a usable C# namespace (dots preserved, segments PascalCased).</summary>
    public static string Namespace(string raw)
    {
        var segments = raw.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(Pascal)
            .Where(s => s.Length > 0);
        var ns = string.Join('.', segments);
        return ns.Length == 0 ? "GeneratedExtension" : ns;
    }

    /// <summary>Lowercase, dash-separated assembly/binary name, e.g. "Contoso.Api" -> "contoso-api".</summary>
    public static string AssemblyName(string raw)
    {
        var words = raw.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(SplitWords)
            .Select(w => w.ToLower(CultureInfo.InvariantCulture));
        var name = string.Join('-', words);
        return name.Length == 0 ? "extension" : name;
    }
}
