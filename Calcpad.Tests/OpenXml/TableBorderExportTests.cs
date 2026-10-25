using System.IO.Compression;
using System.Xml.Linq;
using Calcpad.OpenXml;

namespace Calcpad.Tests;

public class TableBorderExportTests
{
    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void BorderedClass_WritesBordersToDocxCells()
    {
        var document = Convert("<table class=\"bordered\"><tr><th>H</th><td>C</td></tr></table>");

        var borders = document.Descendants(Word + "tcBorders").ToList();

        Assert.Equal(2, borders.Count);
        Assert.Contains(borders, border => border.Element(Word + "top")?.Attribute(Word + "color")?.Value == "AAAAAA");
        Assert.Contains(borders, border => border.Element(Word + "top")?.Attribute(Word + "color")?.Value == "CCCCCC");
    }

    [Fact]
    public void LonghandSolidStyle_WritesTheDefaultCssWidth()
    {
        var html = "<style>td { border-style: solid; border-color: #123456; }</style>" +
                   "<table><tr><td>C</td></tr></table>";

        var document = Convert(html);
        var border = Assert.Single(document.Descendants(Word + "tcBorders"));

        Assert.Equal("123456", border.Element(Word + "top")?.Attribute(Word + "color")?.Value);
        Assert.Equal("18", border.Element(Word + "top")?.Attribute(Word + "sz")?.Value);
    }

    [Fact]
    public void StyleBlockTableRules_WritesBordersToHeaderAndBodyCells()
    {
        var html = "<style>thead th { border: 1pt solid #AAAAAA; } tbody td { border: 1pt solid #CCCCCC; }</style>" +
                   "<table><thead><tr><th>H</th></tr></thead><tbody><tr><td>C</td></tr></tbody></table>";

        var document = Convert(html);
        var borders = document.Descendants(Word + "tcBorders").ToList();

        Assert.Equal(2, borders.Count);
        Assert.Contains(borders, border => border.Element(Word + "top")?.Attribute(Word + "color")?.Value == "AAAAAA");
        Assert.Contains(borders, border => border.Element(Word + "top")?.Attribute(Word + "color")?.Value == "CCCCCC");
    }

    [Fact]
    public void WidthWithoutStyle_DoesNotCreateABorder()
    {
        var html = "<style>td { border-width: 1pt; }</style>" +
                   "<table><tr><td>C</td></tr></table>";

        var document = Convert(html);

        Assert.Empty(document.Descendants(Word + "tcBorders"));
    }

    [Fact]
    public void OrderedList_ReferencesDefinedNumberingInstance()
    {
        using var stream = new MemoryStream();
        var validation = new OpenXmlWriter([]).Convert(
            "<ol><li>First</li><li>Second</li></ol><ol><li>Restarted</li></ol>", stream);
        Assert.Empty(validation);

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadEntry(archive, "word/document.xml");
        var numbering = LoadEntry(archive, "word/numbering.xml");

        var numIds = document.Descendants(Word + "numId")
            .Select(element => (int)element.Attribute(Word + "val")!)
            .Distinct()
            .ToList();
        var definedIds = numbering.Descendants(Word + "num")
            .Select(element => (int)element.Attribute(Word + "numId")!)
            .ToHashSet();

        Assert.NotEmpty(numIds);
        Assert.All(numIds, id => Assert.Contains(id, definedIds));
        Assert.Equal(2, numIds.Count);
        Assert.NotEqual(numIds[0], numIds[1]);
    }

    private static XDocument Convert(string html)
    {
        using var stream = new MemoryStream();
        new OpenXmlWriter([]).Convert(html, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return LoadEntry(archive, "word/document.xml");
    }

    private static XDocument LoadEntry(ZipArchive archive, string name)
    {
        using var document = archive.GetEntry(name)!.Open();
        return XDocument.Load(document);
    }
}
