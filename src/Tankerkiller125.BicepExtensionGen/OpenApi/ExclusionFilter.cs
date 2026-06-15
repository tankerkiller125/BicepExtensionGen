using System.Text;
using System.Text.RegularExpressions;

namespace Tankerkiller125.BicepExtensionGen.OpenApi;

/// <summary>
/// Drops parts of an OpenAPI document from generation. Patterns are globs matched (case-insensitively,
/// full-string) against either path templates / schema names (<c>--exclude</c>) or operation tags
/// (<c>--exclude-tag</c>).
/// </summary>
/// <remarks>
/// Glob syntax: <c>*</c> matches any run of characters except <c>/</c>, <c>**</c> matches any run
/// including <c>/</c>, and <c>?</c> matches a single character. Everything else is literal. For
/// example <c>**/ai/run/**</c> drops every Workers-AI model endpoint, and <c>Workers AI*</c> (as a
/// tag) drops everything tagged under Workers AI.
/// </remarks>
public sealed class ExclusionFilter
{
    public static readonly ExclusionFilter None = new([], []);

    private readonly List<Regex> _names;
    private readonly List<Regex> _tags;

    public ExclusionFilter(IEnumerable<string> nameGlobs, IEnumerable<string> tagGlobs)
    {
        _names = Compile(nameGlobs);
        _tags = Compile(tagGlobs);
    }

    /// <summary>True when no exclusions are configured (the common case; skips all filtering work).</summary>
    public bool IsEmpty => _names.Count == 0 && _tags.Count == 0;

    /// <summary>Whether a path template (path mode) or component schema name (schema mode) is excluded.</summary>
    public bool MatchesName(string value) => _names.Any(r => r.IsMatch(value));

    /// <summary>Whether an operation tag is excluded.</summary>
    public bool MatchesTag(string tag) => _tags.Any(r => r.IsMatch(tag));

    private static List<Regex> Compile(IEnumerable<string> globs) =>
        globs.Select(g => g.Trim())
            .Where(g => g.Length > 0)
            .Select(GlobToRegex)
            .ToList();

    internal static Regex GlobToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            switch (c)
            {
                case '*' when i + 1 < glob.Length && glob[i + 1] == '*':
                    sb.Append(".*");
                    i++; // consume the second '*'
                    break;
                case '*':
                    sb.Append("[^/]*");
                    break;
                case '?':
                    sb.Append('.');
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
