using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Calcpad.OpenXml;

namespace Calcpad.Tests;

public class DocxExportCompatibilityTests
{
    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace DrawingWord = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    [Fact]
    public void SideSpecificBorder_DoesNotBecomeABox()
    {
        var document = Convert("<style>td { border-bottom: 1pt solid #112233; }</style>" +
                              "<table><tr><td>Cell</td></tr></table>");
        var borders = Assert.Single(document.Descendants(Word + "tcBorders"));

        Assert.Null(borders.Element(Word + "top"));
        Assert.Null(borders.Element(Word + "left"));
        Assert.Null(borders.Element(Word + "right"));
        Assert.Equal("112233", borders.Element(Word + "bottom")?.Attribute(Word + "color")?.Value);
    }

    [Fact]
    public void TableBorderStyle_IsWrittenAsAnOuterTableBorder()
    {
        var document = Convert("<table style=\"border: 1pt solid #123456\"><tr><td>Cell</td></tr></table>");
        var tableProperties = Assert.Single(document.Descendants(Word + "tblPr"));

        Assert.NotNull(tableProperties.Element(Word + "tblBorders"));
        Assert.Empty(document.Descendants(Word + "tcBorders"));
    }

    [Fact]
    public void HeaderBorderWithoutBackground_DoesNotAddDefaultShading()
    {
        var document = Convert("<style>th { border: 1pt solid #123456; }</style>" +
                              "<table><tr><th>Header</th></tr></table>");
        var cellProperties = Assert.Single(document.Descendants(Word + "tcPr"));

        Assert.Null(cellProperties.Element(Word + "shd"));
    }

    [Fact]
    public void CssTextColorsApplyToInlineAndBlockContent()
    {
        var document = Convert("<style>.note { color: #123456; background-color: #FEDCBA; }</style>" +
                              "<p class=\"note\">Block</p><p><span class=\"note\">Inline</span></p>");
        var runs = document.Descendants(Word + "r").ToList();

        Assert.Contains(runs, run => run.Descendants(Word + "t").Any(text => text.Value == "Block") &&
                                    run.Descendants(Word + "color").Any(color => color.Attribute(Word + "val")?.Value == "123456"));
        Assert.Contains(runs, run => run.Descendants(Word + "t").Any(text => text.Value == "Inline") &&
                                    run.Descendants(Word + "shd").Any(shd => shd.Attribute(Word + "fill")?.Value == "FEDCBA"));
    }

    [Fact]
    public void CssFontAndTableAlignmentApplyToDocxProperties()
    {
        var html = "<style>.emphasis { font-family: Georgia; font-size: 14pt; font-weight: bold; " +
                   "font-style: italic; text-decoration: underline; } td.value { text-align: right; }</style>" +
                   "<p><span class=\"emphasis\">Styled</span></p>" +
                   "<table><tr><td class=\"value\">42</td></tr></table>";
        var document = Convert(html);
        var styledRun = Assert.Single(document.Descendants(Word + "r"),
            run => run.Descendants(Word + "t").Any(text => text.Value == "Styled"));
        var properties = styledRun.Element(Word + "rPr")!;

        Assert.Equal("Georgia", properties.Element(Word + "rFonts")?.Attribute(Word + "ascii")?.Value);
        Assert.Equal("28", properties.Element(Word + "sz")?.Attribute(Word + "val")?.Value);
        Assert.NotNull(properties.Element(Word + "b"));
        Assert.NotNull(properties.Element(Word + "i"));
        Assert.NotNull(properties.Element(Word + "u"));
        Assert.Equal("right", document.Descendants(Word + "jc").Single().Attribute(Word + "val")?.Value);
    }

    [Fact]
    public void BlockContainersAndTextNewlinesArePreserved()
    {
        var document = Convert("<blockquote>Quote</blockquote><pre>First\nSecond</pre>");
        var text = string.Concat(document.Descendants(Word + "t").Select(element => element.Value));

        Assert.Contains("Quote", text);
        Assert.Contains("First", text);
        Assert.Contains("Second", text);
        Assert.NotEmpty(document.Descendants(Word + "br"));
    }

    [Fact]
    public void RelativeHyperlinksUseTheProvidedBasePath()
    {
        using var stream = new MemoryStream();
        new OpenXmlWriter([]).Convert("<a href=\"docs/help.html\">Help</a>", stream, Path.GetTempPath());
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var relationships = LoadEntry(archive, "word/_rels/document.xml.rels");

        Assert.Contains(relationships.Descendants().Where(element => element.Name.LocalName == "Relationship"),
            relationship => relationship.Attribute("Target")?.Value.Contains("docs/help.html", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void InlineSvgUsesItsViewBoxForDimensions()
    {
        var document = Convert("<svg viewBox=\"0 0 200 100\"><rect width=\"200\" height=\"100\" /></svg>");
        var extent = Assert.Single(document.Descendants(DrawingWord + "extent"));

        Assert.Equal("1905000", extent.Attribute("cx")?.Value);
        Assert.Equal("952500", extent.Attribute("cy")?.Value);
    }

    [Fact]
    public void SvgDataUri_IsEmbeddedInTheDocx()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"5\"></svg>";
        var data = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        using var stream = new MemoryStream();
        var validation = new OpenXmlWriter([]).Convert(
            $"<img src=\"data:image/svg+xml;base64,{data}\" width=\"10\" height=\"5\" />", stream);
        Assert.Empty(validation);

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("media/", StringComparison.Ordinal) &&
                                                  entry.FullName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));
    }

    private static XDocument Convert(string html)
    {
        using var stream = new MemoryStream();
        var validation = new OpenXmlWriter([]).Convert(html, stream);
        Assert.Empty(validation);
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return LoadEntry(archive, "word/document.xml");
    }

    private static XDocument LoadEntry(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)!.Open();
        return XDocument.Load(stream);
    }
}
