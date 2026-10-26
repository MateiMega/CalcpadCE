using DocumentFormat.OpenXml.Wordprocessing;
using System.Collections.Generic;


namespace Calcpad.OpenXml
{
    internal class TableBuilder
    {
        private readonly struct CellData(int rowSpan, bool isHead)
        {
            private readonly int _rowSpan = rowSpan;
            public bool IsMerged => _rowSpan > 0;
            public bool IsHead { get; } = isHead;

            public CellData CopyDown()
            {
                var newSpan = _rowSpan - 1;
                if (newSpan < 0)
                    newSpan = 0;
                return new CellData(newSpan, IsHead);
            }
        }

        public bool IsBorderedTable;
        public TableBorder OuterBorder = TableBorder.None;
        public TableBorder HeadBorder = TableBorder.DefaultHead;
        public TableBorder BodyBorder = TableBorder.DefaultBody;
        public string HeadShading;
        public string BodyShading;
        public JustificationValues? HeadAlignment;
        public JustificationValues? BodyAlignment;
        private List<CellData> _currentRowSpan = [];
        private List<CellData> _prevRowSpan = [];
        private TableRow _currentRow;

        public Table AddTable()
        {
            _currentRowSpan.Clear();
            _prevRowSpan.Clear();
            _currentRow = null;
            var table = new Table();
            var properties = new TableProperties(new TableStyle() { Val = IsBorderedTable ? "bcpd" : "cpd" });
            if (OuterBorder.IsVisible)
            {
                properties.TableBorders = new TableBorders
                {
                    TopBorder = CreateBorder<TopBorder>(OuterBorder.Top),
                    RightBorder = CreateBorder<RightBorder>(OuterBorder.Right),
                    BottomBorder = CreateBorder<BottomBorder>(OuterBorder.Bottom),
                    LeftBorder = CreateBorder<LeftBorder>(OuterBorder.Left)
                };
            }
            table.AppendChild(properties);
            table.AppendChild(new TableGrid());
            return table;
        }

        public TableRow AddTableRow()
        {
            (_currentRowSpan, _prevRowSpan) = (_prevRowSpan, _currentRowSpan);
            _currentRowSpan.Clear();
            _currentRow = new TableRow();
            return _currentRow;
        }

        public TableCell AddTableCell(
            bool isHead,
            int colSpan,
            int rowSpan,
            TableBorder border,
            string shading = null,
            JustificationValues? alignment = null)
        {
            AddMergedCells();
            var tc = CreateTableCell(isHead, border, shading, alignment);
            var tcp = tc.TableCellProperties;
            var n = colSpan == 0 ? 1 : colSpan;
            for (int i = 0; i < n; ++i)
            {
                _currentRowSpan.Add(new CellData(rowSpan, isHead));
            }
            if (colSpan > 1)
            {
                tcp.GridSpan = new GridSpan()
                {
                    Val = colSpan
                };
            }
            if (rowSpan > 1)
            {
                tcp.VerticalMerge = new VerticalMerge()
                {
                    Val = MergedCellValues.Restart
                };
            }
            return tc;
        }

        private TableCell CreateTableCell(
            bool isHead,
            TableBorder border,
            string shading = null,
            JustificationValues? alignment = null)
        {
            var tc = new TableCell();
            var tcp = new TableCellProperties();
            tc.TableCellProperties = tcp;
            if (border.IsVisible)
            {
                tcp.TableCellBorders = new TableCellBorders()
                {
                    LeftBorder = CreateBorder<LeftBorder>(border.Left),
                    RightBorder = CreateBorder<RightBorder>(border.Right),
                    TopBorder = CreateBorder<TopBorder>(border.Top),
                    BottomBorder = CreateBorder<BottomBorder>(border.Bottom),
                };
            }
            if (!string.IsNullOrEmpty(shading))
            {
                tcp.Shading = new Shading()
                {
                    Fill = shading,
                    Val = ShadingPatternValues.Clear
                };
            }
            if (alignment.HasValue)
            {
                tc.AppendChild(new Paragraph
                {
                    ParagraphProperties = new ParagraphProperties
                    {
                        Justification = new Justification { Val = alignment.Value }
                    }
                });
            }
            return tc;

            static T CreateBorder<T>(TableBorderEdge edge) where T : BorderType, new() =>
                edge.IsVisible
                    ? new T
                    {
                        Val = BorderValues.Single,
                        Size = edge.Size,
                        Space = 0u,
                        Color = edge.Color
                    }
                    : null;
        }

        private void AddMergedCells()
        {
            var i1 = _currentRowSpan.Count;
            var n = _prevRowSpan.Count;
            for (int i = i1; i < n; ++i)
            {
                var cellData = _prevRowSpan[i].CopyDown();
                if (!cellData.IsMerged)
                    break;

                _currentRowSpan.Add(cellData);
                var tc = CreateTableCell(
                    cellData.IsHead,
                    cellData.IsHead ? HeadBorder : BodyBorder,
                    cellData.IsHead ? HeadShading : BodyShading,
                    cellData.IsHead ? HeadAlignment : BodyAlignment);
                tc.TableCellProperties.VerticalMerge = new VerticalMerge()
                {
                    Val = MergedCellValues.Continue
                };
                _currentRow.AppendChild(tc);
            }
        }

        private static T CreateBorder<T>(TableBorderEdge edge) where T : BorderType, new() =>
            edge.IsVisible
                ? new T
                {
                    Val = BorderValues.Single,
                    Size = edge.Size,
                    Space = 0u,
                    Color = edge.Color
                }
                : null;
    }
}
