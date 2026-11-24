using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;

namespace FreexComp.Tests.Shared;

/// <summary>
/// The <c>RT_MANIFEST</c> resource of a PE image, read by walking its resource directory (C-167), and the canonical XML form
/// manifests are compared in: the linker's manifest merger re-serialises its input, so the resource equals the input as XML only.
/// </summary>
internal static class PeManifest
{
    /// <summary>The canonical form of a manifest text: the same elements, attributes and text, in any sibling order.</summary>
    public static string Canonical(string xml) => Canonical(XDocument.Parse(xml).Root!);

    /// <summary>Element name, sorted attributes, trimmed text and the sorted canonical forms of its children.</summary>
    private static string Canonical(XElement e) =>
        "<" + e.Name + string.Concat(e.Attributes().Select(a => $" {a.Name}=\"{a.Value}\"").Order(StringComparer.Ordinal)) + ">"
        + string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value.Trim()))
        + string.Concat(e.Elements().Select(Canonical).Order(StringComparer.Ordinal))
        + "</" + e.Name + ">";

    /// <summary>The UTF-8 text of resource type 24 (<c>RT_MANIFEST</c>) name <paramref name="id"/>, first language; null when the image has none.</summary>
    public static string? Read(string exe, uint id = 1)
    {
        var pe = File.ReadAllBytes(exe);
        var header = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(0x3C));
        Assert.Equal(0x00004550u, BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(header)));
        var sections = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(header + 6));
        var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(header + 20));
        var optional = header + 24;
        Assert.Equal(0x20B, BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(optional)));        // PE32+
        var resourceRva = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(optional + 112 + (2 * 8)));
        if (resourceRva == 0)
        {
            return null;
        }

        var table = optional + optionalSize;
        int Offset(uint rva)
        {
            for (var i = 0; i < sections; i++)
            {
                var s = table + (i * 40);
                var size = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(s + 8));
                var address = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(s + 12));
                var raw = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(s + 20));
                if (rva >= address && rva < address + size)
                {
                    return (int)(rva - address + raw);
                }
            }

            throw new InvalidOperationException($"RVA 0x{rva:X} is in no section");
        }

        var root = Offset(resourceRva);
        uint? Child(uint directory, uint? id)
        {
            var at = root + (int)directory;
            var named = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(at + 12));
            var ids = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(at + 14));
            for (var i = 0; i < named + ids; i++)
            {
                var entry = at + 16 + (i * 8);
                var name = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(entry));
                if (id is null || name == id)
                {
                    return BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(entry + 4));
                }
            }

            return null;
        }

        if (Child(0, 24) is not { } type || Child(type & 0x7FFFFFFF, id) is not { } named1 || Child(named1 & 0x7FFFFFFF, null) is not { } language)
        {
            return null;
        }

        Assert.Equal(0u, language & 0x80000000);                                                     // a data entry, not a directory
        var data = root + (int)language;
        var dataRva = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(data));
        var dataSize = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(data + 4));
        return Encoding.UTF8.GetString(pe, Offset(dataRva), dataSize).TrimStart('﻿');
    }
}
