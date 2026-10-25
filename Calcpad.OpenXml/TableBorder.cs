using System;

namespace Calcpad.OpenXml
{
    /// <summary>
    /// Cell border edges as Word understands them.
    /// </summary>
    internal readonly struct TableBorderEdge
    {
        internal TableBorderEdge(bool isVisible, uint size, string color)
        {
            IsVisible = isVisible;
            Size = size;
            Color = color;
        }

        internal bool IsVisible { get; }
        internal uint Size { get; }
        internal string Color { get; }

        internal static TableBorderEdge None => default;
    }

    internal readonly struct TableBorder
    {
        // Mirrors the stock "bordered" look of the report template:
        // th { border: solid 1pt #AAAAAA } and td { border: solid 1pt #CCCCCC }.
        internal const uint DefaultSize = 8u;
        internal const string HeadColor = "AAAAAA";
        internal const string BodyColor = "CCCCCC";
        internal const string TextColor = "000000";

        internal TableBorder(bool isVisible, uint size, string color)
        {
            var edge = new TableBorderEdge(isVisible, size, color);
            Top = edge;
            Right = edge;
            Bottom = edge;
            Left = edge;
        }

        private TableBorder(TableBorderEdge top, TableBorderEdge right, TableBorderEdge bottom, TableBorderEdge left)
        {
            Top = top;
            Right = right;
            Bottom = bottom;
            Left = left;
        }

        internal TableBorderEdge Top { get; }
        internal TableBorderEdge Right { get; }
        internal TableBorderEdge Bottom { get; }
        internal TableBorderEdge Left { get; }

        internal bool IsVisible => Top.IsVisible || Right.IsVisible || Bottom.IsVisible || Left.IsVisible;

        internal static TableBorder None => default;
        internal static TableBorder DefaultHead => new(true, DefaultSize, HeadColor);
        internal static TableBorder DefaultBody => new(true, DefaultSize, BodyColor);

        internal static TableBorder FromEdges(
            TableBorderEdge top,
            TableBorderEdge right,
            TableBorderEdge bottom,
            TableBorderEdge left) => new(top, right, bottom, left);

        /// <summary>
        /// Builds a border from a Css width in points. Word only accepts 2..96 eighths of
        /// a point, so anything thinner than 0.25pt is drawn at the minimum instead of
        /// disappearing, and a width of zero means "no border".
        /// </summary>
        internal static TableBorder FromPoints(double points, string color)
        {
            if (points <= 0)
                return None;

            var size = (uint)Math.Round(points * 8.0);
            if (size < 2u)
                size = 2u;
            else if (size > 96u)
                size = 96u;

            return new TableBorder(true, size, string.IsNullOrEmpty(color) ? TextColor : color);
        }
    }
}
