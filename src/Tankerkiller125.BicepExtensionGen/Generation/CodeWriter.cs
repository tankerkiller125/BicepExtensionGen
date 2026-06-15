using System.Text;

namespace Tankerkiller125.BicepExtensionGen.Generation;

/// <summary>
/// A small indentation-aware text writer for emitting source code. Replaces hand-managed
/// newlines/indentation/brace bookkeeping in the generators with structured <see cref="Block"/>
/// scopes and <see cref="Line"/> calls.
/// </summary>
public sealed class CodeWriter
{
    private const string IndentUnit = "    ";
    private readonly StringBuilder _sb = new();
    private int _depth;

    /// <summary>Writes a single line at the current indentation (blank line when empty).</summary>
    public CodeWriter Line(string text = "")
    {
        if (text.Length > 0)
        {
            for (var i = 0; i < _depth; i++)
                _sb.Append(IndentUnit);
            _sb.Append(text);
        }

        _sb.Append('\n');
        return this;
    }

    /// <summary>A blank separator line.</summary>
    public CodeWriter Blank() => Line();

    /// <summary>Re-indents and writes a multi-line block (e.g. a verbatim raw-string body) at the current depth.</summary>
    public CodeWriter Lines(string block)
    {
        foreach (var line in block.Replace("\r\n", "\n").Split('\n'))
            Line(line);
        return this;
    }

    /// <summary>Appends text verbatim with no indentation processing (for embedded literals).</summary>
    public CodeWriter Raw(string text)
    {
        _sb.Append(text);
        return this;
    }

    public void Indent() => _depth++;

    public void Outdent() => _depth = Math.Max(0, _depth - 1);

    /// <summary>Opens a braced scope; dispose writes <paramref name="close"/>. Use with <c>using</c>.</summary>
    public Scope Brace(string close = "}")
    {
        Line("{");
        Indent();
        return new Scope(this, close);
    }

    /// <summary>Writes <paramref name="header"/> then opens a braced scope.</summary>
    public Scope Block(string header, string close = "}")
    {
        Line(header);
        return Brace(close);
    }

    public override string ToString() => _sb.ToString();

    /// <summary>Closes a <see cref="Brace"/>/<see cref="Block"/> scope on dispose.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly CodeWriter _writer;
        private readonly string _close;

        public Scope(CodeWriter writer, string close)
        {
            _writer = writer;
            _close = close;
        }

        public void Dispose()
        {
            _writer.Outdent();
            _writer.Line(_close);
        }
    }
}
