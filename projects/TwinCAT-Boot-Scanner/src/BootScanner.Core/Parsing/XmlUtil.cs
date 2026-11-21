using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BootScanner.Core.Parsing;

/// <summary>Encoding-tolerant XML helpers. Never depends on TwinCAT libraries.</summary>
public static class XmlUtil
{
    private static readonly Regex EncodingDecl = new(
        @"encoding\s*=\s*['""]([^'""]+)['""]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool _encodingsRegistered;

    public static void EnsureEncodings()
    {
        if (_encodingsRegistered)
            return;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _encodingsRegistered = true;
    }

    public static XDocument LoadXDocument(string path)
    {
        EnsureEncodings();
        using var input = File.OpenRead(path);
        if (input.Length > 64L * 1024 * 1024)
            throw new InvalidDataException("XML 超过 64 MiB 读取限制。");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        var attempts = BuildDecodingAttempts(bytes);

        Exception? last = null;
        foreach (var text in attempts)
        {
            try
            {
                return ParseText(text);
            }
            catch (Exception ex) when (ex is XmlException or InvalidOperationException or ArgumentException)
            {
                last = ex;
            }
        }

        throw last ?? new XmlException("无法解析 XML。");
    }

    public static XDocument ParseText(string text)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            CheckCharacters = false,
            IgnoreWhitespace = false,
            MaxCharactersInDocument = 64L * 1024 * 1024
        };

        using var sr = new StringReader(text);
        using var reader = XmlReader.Create(sr, settings);
        var document = XDocument.Load(reader, LoadOptions.SetLineInfo);
        if (document.Descendants().Any(e => e.Ancestors().Take(257).Count() > 256))
            throw new XmlException("XML 嵌套超过 256 层限制。");
        return document;
    }

    public static IEnumerable<string> BuildDecodingAttempts(byte[] bytes)
    {
        var list = new List<string>();
        void Add(string? s)
        {
            if (!string.IsNullOrEmpty(s) && !list.Contains(s))
                list.Add(s);
        }

        Add(DecodeWithBomOrSignature(bytes));

        var decl = TryPeekDeclaration(bytes);
        if (!string.IsNullOrEmpty(decl))
        {
            var encName = TryGetDeclaredEncoding(decl);
            if (encName != null && TryGetEncoding(encName) is { } enc)
            {
                try { Add(enc.GetString(bytes)); } catch { /* ignore */ }
            }
        }

        try { Add(Encoding.UTF8.GetString(bytes)); } catch { }
        try { Add(Encoding.Unicode.GetString(bytes)); } catch { }
        try { Add(Encoding.Latin1.GetString(bytes)); } catch { }
        if (TryGetEncoding("gb18030") is { } gb)
        {
            try { Add(gb.GetString(bytes)); } catch { }
        }
        if (TryGetEncoding("windows-1252") is { } w)
        {
            try { Add(w.GetString(bytes)); } catch { }
        }

        return list;
    }

    public static string DecodeWithBomOrSignature(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        // UTF-16 LE/BE without BOM, starting with "<?"
        if (bytes.Length >= 4 && bytes[0] == 0x3C && bytes[1] == 0x00 && bytes[2] == 0x3F && bytes[3] == 0x00)
            return Encoding.Unicode.GetString(bytes);
        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x3C && bytes[2] == 0x00 && bytes[3] == 0x3F)
            return Encoding.BigEndianUnicode.GetString(bytes);

        var decl = TryPeekDeclaration(bytes);
        if (!string.IsNullOrEmpty(decl))
        {
            var encName = TryGetDeclaredEncoding(decl);
            if (encName != null && TryGetEncoding(encName) is { } enc)
                return enc.GetString(bytes);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string? TryPeekDeclaration(byte[] bytes)
    {
        var n = Math.Min(bytes.Length, 240);
        if (n == 0)
            return null;
        // Prefer ASCII/UTF-8 view; also try UTF-16 if NULs are present.
        if (n >= 4 && bytes[1] == 0 && bytes[0] != 0)
            return Encoding.Unicode.GetString(bytes, 0, n - (n % 2));
        if (n >= 4 && bytes[0] == 0 && bytes[1] != 0)
            return Encoding.BigEndianUnicode.GetString(bytes, 0, n - (n % 2));
        return Encoding.ASCII.GetString(bytes, 0, n);
    }

    public static string? TryGetDeclaredEncoding(string preamble)
    {
        var m = EncodingDecl.Match(preamble);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    public static Encoding? TryGetEncoding(string name)
    {
        try
        {
            return Encoding.GetEncoding(name);
        }
        catch
        {
            return name.Equals("utf-8", StringComparison.OrdinalIgnoreCase) ? Encoding.UTF8
                : name.Equals("utf-16", StringComparison.OrdinalIgnoreCase) ? Encoding.Unicode
                : name.Equals("utf-16LE", StringComparison.OrdinalIgnoreCase) ? Encoding.Unicode
                : name.Equals("utf-16BE", StringComparison.OrdinalIgnoreCase) ? Encoding.BigEndianUnicode
                : null;
        }
    }

    public static IEnumerable<XElement> ElementsLocal(this XElement element, string localName) =>
        element.Elements().Where(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

    public static XElement? ElementLocal(this XElement element, string localName) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<XElement> DescendantsLocal(this XContainer container, string localName) =>
        container.Descendants().Where(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

    public static string? Attr(this XElement element, string name) =>
        element.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    public static string? ChildText(this XElement element, string childLocalName)
    {
        var child = element.ElementLocal(childLocalName);
        var value = child?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static string? DirectText(this XElement? element)
    {
        var value = element?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
