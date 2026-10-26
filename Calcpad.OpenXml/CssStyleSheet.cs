using HtmlAgilityPack;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Calcpad.OpenXml
{
    /// <summary>
    /// Recovers the borders of a table from the Html of a report.
    /// A Calcpad script can border a table in three ways: the <c>bordered</c> class, the
    /// legacy <c>border</c> attribute, or Css — inline on the table/cells, or in a
    /// <c>&lt;style&gt;</c> block. Word knows nothing about any of them, so the rules that
    /// touch table cells are resolved here and written out as explicit cell borders.
    /// Only the properties that decide whether a border is drawn are considered: width,
    /// line style and colour.
    /// </summary>
    internal sealed class CssStyleSheet
    {
        private readonly List<CssRule> _rules;

        private CssStyleSheet(List<CssRule> rules) => _rules = rules;

        internal static readonly CssStyleSheet Empty = new(new List<CssRule>());

        /// <summary>
        /// Collects every <c>&lt;style&gt;</c> block of the document. The report template
        /// keeps its own rules in the head, so the whole document is scanned rather than
        /// only the body that is converted.
        /// </summary>
        internal static CssStyleSheet FromDocument(HtmlNode document)
        {
            if (document is null)
                return Empty;

            var css = new StringBuilder();
            try
            {
                var styleNodes = document.SelectNodes("//style");
                if (styleNodes is not null)
                {
                    foreach (var styleNode in styleNodes)
                        css.AppendLine(styleNode.InnerText);
                }
            }
            catch (Exception)
            {
                // Malformed markup is not a reason to fail the export.
                return Empty;
            }
            return new CssStyleSheet(Parse(css.ToString()));
        }

        /// <summary>
        /// Resolves the borders of one table, for its header and for its body cells.
        /// The first item of the result is true when at least one of them is drawn, which
        /// is what selects the table style and the cell padding in Word.
        /// </summary>
        internal (bool IsBordered, TableBorder Table, TableBorder Head, TableBorder Body, string HeadShading, string BodyShading) GetTableBorders(HtmlNode table)
        {
            var head = GetBorder(table.SelectSingleNode(".//th"));
            var body = GetBorder(table.SelectSingleNode(".//td"));
            var own = GetBorder(table);
            var headShading = GetBackground(table.SelectSingleNode(".//th"));
            var bodyShading = GetBackground(table.SelectSingleNode(".//td"));
            if (table.HasClass("bordered") || HasLegacyBorder(table))
            {
                head ??= TableBorder.DefaultHead;
                body ??= TableBorder.DefaultBody;
                headShading ??= table.HasClass("bordered") ? "F0F0F0" : null;
            }
            head ??= TableBorder.None;
            body ??= TableBorder.None;
            var headBorder = head.Value;
            var bodyBorder = body.Value;
            var tableBorder = own ?? TableBorder.None;
            return (tableBorder.IsVisible || headBorder.IsVisible || bodyBorder.IsVisible,
                tableBorder, headBorder, bodyBorder, headShading, bodyShading);
        }

        /// <summary>
        /// Cascades the Css that applies to a single node: the matching rules in source
        /// order, then the inline style, which wins over the style sheet.
        /// Returns null when nothing declares a border for the node at all.
        /// </summary>
        internal TableBorder? GetBorder(HtmlNode node)
        {
            if (node is null)
                return null;

            var state = new BorderState();
            foreach (var rule in _rules)
            {
                if (rule.Matches(node))
                    state.Apply(rule.Declarations);
            }
            state.Apply(node.GetAttributeValue("style", null));
            return state.ToBorder();
        }

        internal (string Foreground, string Background) GetColors(HtmlNode node)
        {
            if (node is null)
                return (null, null);

            var state = new ColorState();
            foreach (var rule in _rules)
            {
                if (rule.Matches(node))
                    state.Apply(rule.Declarations);
            }
            state.Apply(node.GetAttributeValue("style", null));
            return state.Values;
        }

        internal string GetBackground(HtmlNode node) => GetColors(node).Background;

        internal TextStyle GetTextStyle(HtmlNode node)
        {
            if (node is null)
                return default;

            var state = new TextStyleState();
            foreach (var rule in _rules)
            {
                if (rule.Matches(node))
                    state.Apply(rule.Declarations);
            }
            state.Apply(node.GetAttributeValue("style", null));
            return state.ToStyle();
        }

        /// <summary>
        /// <c>&lt;table border="1"&gt;</c> and friends. A bare or empty attribute means the
        /// same as <c>border="1"</c>; only an explicit zero removes the border.
        /// </summary>
        private static bool HasLegacyBorder(HtmlNode table)
        {
            var value = table.GetAttributeValue("border", null);
            if (value is null)
                return false;

            value = value.Trim();
            if (value.Length == 0)
                return true;

            return !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n > 0;
        }

        private static List<CssRule> Parse(string css)
        {
            var rules = new List<CssRule>();
            if (string.IsNullOrWhiteSpace(css))
                return rules;

            css = StripComments(css);
            var i = 0;
            var n = css.Length;
            while (i < n)
            {
                while (i < n && char.IsWhiteSpace(css[i]))
                    ++i;

                // @charset and @import are statements that end with ';' and would
                // otherwise be glued to the selector of the rule that follows them.
                if (i < n && css[i] == '@')
                {
                    var semi = css.IndexOf(';', i);
                    var brace = css.IndexOf('{', i);
                    if (semi >= 0 && (brace < 0 || semi < brace))
                    {
                        i = semi + 1;
                        continue;
                    }
                }

                var open = css.IndexOf('{', i);
                if (open < 0)
                    break;

                var selector = css[i..open].Trim();
                var depth = 1;
                var j = open + 1;
                while (j < n && depth > 0)
                {
                    var c = css[j];
                    if (c == '{')
                        ++depth;
                    else if (c == '}')
                        --depth;

                    ++j;
                }
                var body = j - 1 > open ? css[(open + 1)..(j - 1)] : string.Empty;
                i = j;

                if (selector.Length == 0)
                    continue;

                if (selector[0] == '@')
                {
                    // Word is a print target, so rules wrapped in @media screen do not
                    // apply, while print and unconditional blocks do. @font-face and
                    // @import have no nested block to keep.
                    var isScreenOnly = selector.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isScreenOnly && body.IndexOf('{') >= 0)
                        rules.AddRange(Parse(body));

                    continue;
                }
                rules.Add(new CssRule(selector, body));
            }
            return rules;
        }

        private static string StripComments(string css)
        {
            var sb = new StringBuilder(css.Length);
            var i = 0;
            while (i < css.Length)
            {
                var start = css.IndexOf("/*", i, StringComparison.Ordinal);
                if (start < 0)
                {
                    sb.Append(css, i, css.Length - i);
                    break;
                }
                sb.Append(css, i, start - i);
                var end = css.IndexOf("*/", start + 2, StringComparison.Ordinal);
                if (end < 0)
                    break;

                i = end + 2;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Css lengths to points. Only absolute units are converted; anything else
        /// (percentages, calc()) yields zero, which reads as "no border".
        /// </summary>
        private static double ToPoints(string value)
        {
            value = value.Trim().ToLowerInvariant();
            switch (value)
            {
                case "thin": return 0.75;    // 1px
                case "medium": return 2.25;  // 3px
                case "thick": return 3.75;   // 5px
            }

            var i = 0;
            while (i < value.Length &&
                  (char.IsDigit(value[i]) || value[i] == '.' || value[i] == '-' || value[i] == '+'))
                ++i;

            if (i == 0)
                return 0;

            if (!double.TryParse(value[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return 0;

            return value[i..].Trim() switch
            {
                "px" => d * 72.0 / 96.0,
                "pt" => d,
                "pc" => d * 12.0,
                "in" => d * 72.0,
                "cm" => d * 72.0 / 2.54,
                "mm" => d * 72.0 / 25.4,
                "q" => d * 72.0 / 101.6,
                "em" => d * 12.0,
                "rem" => d * 12.0,
                "" => d,
                _ => 0
            };
        }

        private static bool IsStyleKeyword(string token) => token switch
        {
            "none" or "hidden" or "dotted" or "dashed" or "solid" or "double"
                or "groove" or "ridge" or "inset" or "outset" => true,
            _ => false
        };

        private static bool IsWidthKeyword(string token) => token is "thin" or "medium" or "thick";

        private static bool IsLength(string token) =>
            token.Length > 0 &&
            (char.IsDigit(token[0]) || token[0] == '.' || token[0] == '-' || token[0] == '+');

        private static string ToHexColor(string value)
        {
            value = value.Trim();
            if (value.Length == 0)
                return null;

            if (value[0] == '#')
            {
                var hex = value[1..];
                if (hex.Length == 3)
                    return $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}".ToUpperInvariant();

                return hex.Length >= 6 ? hex[..6].ToUpperInvariant() : null;
            }

            if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return CssColor.RgbToHex(value);
                }
                catch (Exception)
                {
                    return null;
                }
            }
            return CssColor.NameToHex(value);
        }

        private static List<string> SplitValues(string value)
        {
            var values = new List<string>();
            var start = 0;
            var depth = 0;
            for (var i = 0; i < value.Length; ++i)
            {
                if (value[i] == '(')
                    ++depth;
                else if (value[i] == ')')
                    depth = Math.Max(0, depth - 1);
                else if (depth == 0 && char.IsWhiteSpace(value[i]))
                {
                    if (i > start)
                        values.Add(value[start..i]);
                    while (i + 1 < value.Length && char.IsWhiteSpace(value[i + 1]))
                        ++i;
                    start = i + 1;
                }
            }
            if (start < value.Length)
                values.Add(value[start..]);
            return values;
        }

        private static List<string> SplitTokens(string value) => SplitValues(value);

        /// <summary>
        /// The border properties of one element, accumulated while the Css cascades.
        /// A later declaration of the same property replaces an earlier one.
        /// </summary>
        private struct BorderState
        {
            private BorderSide _top;
            private BorderSide _right;
            private BorderSide _bottom;
            private BorderSide _left;

            internal void Apply(string declarations)
            {
                if (string.IsNullOrWhiteSpace(declarations))
                    return;

                foreach (var prop in declarations.Split(';'))
                {
                    var index = prop.IndexOf(':');
                    if (index < 0)
                        continue;

                    var name = prop[..index].Trim().ToLowerInvariant();
                    var value = prop[(index + 1)..].Trim();
                    if (value.Length == 0)
                        continue;

                    switch (name)
                    {
                        case "border":
                            _top.ApplyShorthand(value);
                            _right.ApplyShorthand(value);
                            _bottom.ApplyShorthand(value);
                            _left.ApplyShorthand(value);
                            break;
                        case "border-top": _top.ApplyShorthand(value); break;
                        case "border-right": _right.ApplyShorthand(value); break;
                        case "border-bottom": _bottom.ApplyShorthand(value); break;
                        case "border-left": _left.ApplyShorthand(value); break;
                        case "border-block":
                            _top.ApplyShorthand(value);
                            _bottom.ApplyShorthand(value);
                            break;
                        case "border-inline":
                            _left.ApplyShorthand(value);
                            _right.ApplyShorthand(value);
                            break;
                        case "border-width": ApplyQuad(value, 0); break;
                        case "border-style": ApplyQuad(value, 1); break;
                        case "border-color": ApplyQuad(value, 2); break;
                        case "border-top-width": _top.ApplyWidth(value); break;
                        case "border-right-width": _right.ApplyWidth(value); break;
                        case "border-bottom-width": _bottom.ApplyWidth(value); break;
                        case "border-left-width": _left.ApplyWidth(value); break;
                        case "border-top-style": _top.ApplyStyle(value); break;
                        case "border-right-style": _right.ApplyStyle(value); break;
                        case "border-bottom-style": _bottom.ApplyStyle(value); break;
                        case "border-left-style": _left.ApplyStyle(value); break;
                        case "border-top-color": _top.ApplyColor(value); break;
                        case "border-right-color": _right.ApplyColor(value); break;
                        case "border-bottom-color": _bottom.ApplyColor(value); break;
                        case "border-left-color": _left.ApplyColor(value); break;
                    }
                }
            }

            private void ApplyQuad(string value, int kind)
            {
                var values = SplitValues(value);
                if (values.Count == 0)
                    return;

                ApplyValue(values[0], 0, kind);
                if (values.Count == 1)
                {
                    ApplyValue(values[0], 1, kind);
                    ApplyValue(values[0], 2, kind);
                    ApplyValue(values[0], 3, kind);
                }
                else if (values.Count == 2)
                {
                    ApplyValue(values[1], 1, kind);
                    ApplyValue(values[0], 2, kind);
                    ApplyValue(values[1], 3, kind);
                }
                else if (values.Count == 3)
                {
                    ApplyValue(values[1], 1, kind);
                    ApplyValue(values[2], 2, kind);
                    ApplyValue(values[1], 3, kind);
                }
                else
                {
                    ApplyValue(values[1], 1, kind);
                    ApplyValue(values[2], 2, kind);
                    ApplyValue(values[3], 3, kind);
                }
            }

            private void ApplyValue(string value, int side, int kind)
            {
                if (kind == 0)
                    ApplyWidth(value, side);
                else if (kind == 1)
                    ApplyStyle(value, side);
                else
                    ApplyColor(value, side);
            }

            private void ApplyWidth(string value, int side)
            {
                switch (side)
                {
                    case 0: _top.ApplyWidth(value); break;
                    case 1: _right.ApplyWidth(value); break;
                    case 2: _bottom.ApplyWidth(value); break;
                    default: _left.ApplyWidth(value); break;
                }
            }

            private void ApplyStyle(string value, int side)
            {
                switch (side)
                {
                    case 0: _top.ApplyStyle(value); break;
                    case 1: _right.ApplyStyle(value); break;
                    case 2: _bottom.ApplyStyle(value); break;
                    default: _left.ApplyStyle(value); break;
                }
            }

            private void ApplyColor(string value, int side)
            {
                switch (side)
                {
                    case 0: _top.ApplyColor(value); break;
                    case 1: _right.ApplyColor(value); break;
                    case 2: _bottom.ApplyColor(value); break;
                    default: _left.ApplyColor(value); break;
                }
            }

            internal TableBorder? ToBorder()
            {
                if (!_top.Declared && !_right.Declared && !_bottom.Declared && !_left.Declared)
                    return null;

                return TableBorder.FromEdges(
                    _top.ToEdge(), _right.ToEdge(), _bottom.ToEdge(), _left.ToEdge());
            }
        }

        private struct BorderSide
        {
            internal bool Declared;
            private bool _hasStyle;
            private bool _styleVisible;
            private bool _hasWidth;
            private double _points;
            private bool _transparent;
            private string _color;

            internal void ApplyShorthand(string value)
            {
                Declared = true;
                _hasStyle = false;
                _styleVisible = false;
                _hasWidth = false;
                _points = 0;
                _transparent = false;
                _color = null;

                foreach (var token in SplitTokens(value))
                {
                    if (IsStyleKeyword(token))
                    {
                        _hasStyle = true;
                        _styleVisible = !IsNone(token);
                    }
                    else if (IsWidthKeyword(token) || IsLength(token))
                    {
                        _hasWidth = true;
                        _points = ToPoints(token);
                    }
                    else if (token.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                    {
                        _transparent = true;
                        _color = null;
                    }
                    else
                    {
                        _transparent = false;
                        _color = ToHexColor(token);
                    }
                }

                if (_hasStyle && _styleVisible && !_hasWidth)
                    _points = 2.25;
            }

            internal void ApplyWidth(string value)
            {
                Declared = true;
                _hasWidth = true;
                _points = ToPoints(value);
            }

            internal void ApplyStyle(string value)
            {
                Declared = true;
                _hasStyle = true;
                _styleVisible = !IsNone(value);
                if (_styleVisible && !_hasWidth)
                    _points = 2.25;
            }

            internal void ApplyColor(string value)
            {
                Declared = true;
                _transparent = value.Equals("transparent", StringComparison.OrdinalIgnoreCase);
                _color = ToHexColor(value);
            }

            internal TableBorderEdge ToEdge()
            {
                if (!Declared || !_hasStyle || !_styleVisible || _transparent || _points <= 0)
                    return TableBorderEdge.None;

                var border = TableBorder.FromPoints(_points, _color);
                return new TableBorderEdge(true, border.Top.Size, border.Top.Color);
            }

            private static bool IsNone(string value) =>
                value.Trim().ToLowerInvariant() is "none" or "hidden";
        }

        private struct ColorState
        {
            private string _foreground;
            private string _background;

            internal (string Foreground, string Background) Values => (_foreground, _background);

            internal void Apply(string declarations)
            {
                if (string.IsNullOrWhiteSpace(declarations))
                    return;

                foreach (var prop in declarations.Split(';'))
                {
                    var index = prop.IndexOf(':');
                    if (index < 0)
                        continue;

                    var name = prop[..index].Trim().ToLowerInvariant();
                    var value = prop[(index + 1)..].Trim();
                    if (name == "color")
                        _foreground = ToHexColor(value);
                    else if (name == "background-color")
                        _background = ToHexColor(value);
                    else if (name == "background")
                    {
                        foreach (var token in SplitTokens(value))
                        {
                            var color = ToHexColor(token);
                            if (color is not null)
                            {
                                _background = color;
                                break;
                            }
                        }
                    }
                }
            }
        }

        internal struct TextStyle
        {
            internal string Foreground { get; set; }
            internal string Background { get; set; }
            internal string FontFamily { get; set; }
            internal double FontSize { get; set; }
            internal bool? Bold { get; set; }
            internal bool? Italic { get; set; }
            internal bool? Underline { get; set; }
            internal bool? Strike { get; set; }
            internal VerticalPositionValues? Vertical { get; set; }
            internal JustificationValues? Alignment { get; set; }
        }

        private struct TextStyleState
        {
            private TextStyle _style;

            internal void Apply(string declarations)
            {
                if (string.IsNullOrWhiteSpace(declarations))
                    return;

                foreach (var prop in declarations.Split(';'))
                {
                    var index = prop.IndexOf(':');
                    if (index < 0)
                        continue;

                    var name = prop[..index].Trim().ToLowerInvariant();
                    var value = prop[(index + 1)..].Trim();
                    if (value.Length == 0)
                        continue;

                    switch (name)
                    {
                        case "color":
                            _style.Foreground = ToHexColor(value);
                            break;
                        case "background-color":
                            _style.Background = ToHexColor(value);
                            break;
                        case "background":
                            foreach (var token in SplitTokens(value))
                            {
                                var color = ToHexColor(token);
                                if (color is not null)
                                {
                                    _style.Background = color;
                                    break;
                                }
                            }
                            break;
                        case "font-family":
                            _style.FontFamily = SplitValues(value).FirstOrDefault()?.Trim('"', '\'', ',');
                            break;
                        case "font-size":
                            _style.FontSize = ToPoints(value);
                            break;
                        case "font-weight":
                            _style.Bold = value.Equals("bold", StringComparison.OrdinalIgnoreCase) ||
                                          value.Equals("bolder", StringComparison.OrdinalIgnoreCase) ||
                                          int.TryParse(value, out var weight) && weight >= 600;
                            break;
                        case "font-style":
                            _style.Italic = value.Equals("italic", StringComparison.OrdinalIgnoreCase) ||
                                            value.Equals("oblique", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "text-decoration":
                            _style.Underline = value.Contains("underline", StringComparison.OrdinalIgnoreCase);
                            _style.Strike = value.Contains("line-through", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "text-align":
                            _style.Alignment = value.ToLowerInvariant() switch
                            {
                                "left" or "start" => JustificationValues.Left,
                                "right" or "end" => JustificationValues.Right,
                                "center" => JustificationValues.Center,
                                "justify" => JustificationValues.Both,
                                _ => null
                            };
                            break;
                        case "vertical-align":
                            _style.Vertical = value.ToLowerInvariant() switch
                            {
                                "super" => VerticalPositionValues.Superscript,
                                "sub" => VerticalPositionValues.Subscript,
                                _ => null
                            };
                            break;
                    }
                }
            }

            internal TextStyle ToStyle() => _style;
        }

        private readonly struct CssRule
        {
            private readonly string[][] _selectors;
            private readonly string _declarations;

            internal CssRule(string selector, string declarations)
            {
                _selectors = SplitSelectors(selector);
                _declarations = declarations;
            }

            internal string Declarations => _declarations;

            internal bool Matches(HtmlNode node)
            {
                if (_selectors is null)
                    return false;

                foreach (var selector in _selectors)
                {
                    if (MatchesSelector(selector, node))
                        return true;
                }
                return false;
            }

            /// <summary>
            /// Splits a selector list into its comma separated selectors, each already cut
            /// into compounds, so matching a node allocates nothing. Selectors that use
            /// anything beyond descendant combinators are dropped here rather than
            /// guessed at, so a rule never borders a table it was not meant for.
            /// Dark theme rules are dropped too: a Word document is always a light page.
            /// </summary>
            private static string[][] SplitSelectors(string selector)
            {
                var parts = selector.Split(',');
                var result = new List<string[]>(parts.Length);
                foreach (var part in parts)
                {
                    var s = part.Trim();
                    if (s.Length == 0 ||
                        s.IndexOf(':') >= 0 || s.IndexOf('[') >= 0 ||
                        s.IndexOf('+') >= 0 || s.IndexOf('~') >= 0 ||
                        s.IndexOf("dark-theme", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    var compounds = s.Split(
                        new[] { ' ', '\t', '\n', '\r', '>' },
                        StringSplitOptions.RemoveEmptyEntries);

                    if (compounds.Length != 0)
                        result.Add(compounds);
                }
                return result.Count == 0 ? null : result.ToArray();
            }

            private static bool MatchesSelector(string[] compounds, HtmlNode node)
            {
                var i = compounds.Length - 1;
                if (!MatchesCompound(compounds[i], node))
                    return false;

                --i;
                var parent = node.ParentNode;
                while (i >= 0 && parent is not null)
                {
                    if (parent.NodeType == HtmlNodeType.Element && MatchesCompound(compounds[i], parent))
                        --i;

                    parent = parent.ParentNode;
                }
                return i < 0;
            }

            private static bool MatchesCompound(string compound, HtmlNode node)
            {
                if (compound == "*")
                    return true;

                var i = 0;
                if (compound[0] != '.' && compound[0] != '#')
                {
                    while (i < compound.Length && compound[i] != '.' && compound[i] != '#')
                        ++i;

                    var tag = compound[..i];
                    if (!string.Equals(tag, node.Name, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                while (i < compound.Length)
                {
                    var c = compound[i];
                    var j = i + 1;
                    while (j < compound.Length && compound[j] != '.' && compound[j] != '#')
                        ++j;

                    var name = compound[(i + 1)..j];
                    if (name.Length == 0)
                        return false;

                    if (c == '.')
                    {
                        if (!node.HasClass(name))
                            return false;
                    }
                    else if (!string.Equals(node.GetAttributeValue("id", string.Empty), name, StringComparison.Ordinal))
                        return false;

                    i = j;
                }
                return true;
            }
        }
    }
}
