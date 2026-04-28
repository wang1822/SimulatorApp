using ClosedXML.Excel;

namespace SimulatorApp.Shared.Helpers;

/// <summary>
/// Excel 工具类。
/// 当前保留：
///   1. 主站轮询数据导出/导入
///   2. 从站协议文档格式寄存器导入（地址|中文名|英文名|读写|单位|描述）
/// </summary>
public static class ExcelHelper
{
    /// <summary>
    /// 供主站"导出轮询数据"使用。
    /// </summary>
    public static void ExportDeviceData(string filePath,
        IEnumerable<(int Address, string Description, ushort RawValue, string HexValue, string PhysicalValue, string LastUpdated)> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("轮询数据");

        string[] headers = { "地址", "说明", "原始值", "十六进制", "物理值", "更新时间" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        var headerRange = ws.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563EB");
        headerRange.Style.Font.FontColor = XLColor.White;

        int row = 2;
        foreach (var (addr, desc, raw, hex, phys, updated) in rows)
        {
            ws.Cell(row, 1).Value = addr;
            ws.Cell(row, 2).Value = desc;
            ws.Cell(row, 3).Value = raw;
            ws.Cell(row, 4).Value = hex;
            ws.Cell(row, 5).Value = phys;
            ws.Cell(row, 6).Value = updated;
            row++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    /// <summary>
    /// 从主站导出的 Excel 重新导入轮询数据。
    /// </summary>
    public static List<(int Address, string Description, ushort RawValue)> ImportPollData(string filePath)
    {
        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheet(1);

        var result = new List<(int, string, ushort)>();
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (int row = 2; row <= lastRow; row++)
        {
            var addrCell = ws.Cell(row, 1);
            if (addrCell.IsEmpty()) continue;
            if (!int.TryParse(addrCell.GetValue<string>(), out int addr)) continue;

            var desc = ws.Cell(row, 2).GetValue<string>();
            ushort.TryParse(ws.Cell(row, 3).GetValue<string>(), out ushort rawVal);
            result.Add((addr, desc, rawVal));
        }

        return result;
    }

    // ----------------------------------------------------------------
    // 协议文档格式解析
    // 列格式：地址 | 中文名 | 英文名 | 读写属性 | 单位 | 描述
    // 地址支持十进制、0x 前缀、H 后缀十六进制
    // ----------------------------------------------------------------

    /// <summary>
    /// 从剪贴板 TSV 文本解析 Modbus 协议文档寄存器数据。
    /// 支持 Excel 复制的带引号多行单元格（Alt+Enter 换行）。
    /// </summary>
    public static List<(string ChineseName, string EnglishName, int Address, string ReadWrite, string Range, string Unit, string Note)>
        ParseProtocolRowsFromClipboard(string clipboardText)
    {
        var result = new List<(string, string, int, string, string, string, string)>();
        var rows = ParseTsvRows(clipboardText);
        if (rows.Count == 0) return result;

        // 固定 7 列默认映射：地址=0, 中文=1, 英文=2, 读写=3, 范围=4, 单位=5, 描述=6
        int addrCol = 0, chineseCol = 1, englishCol = 2, rwCol = 3, rangeCol = 4, unitCol = 5, noteCol = 6;
        int headerIdx = -1;

        // 尝试在前 10 行中找标题行
        for (int i = 0; i < Math.Min(rows.Count, 10); i++)
        {
            var hcols = rows[i];
            if (hcols.Length > 0 && TryParseAddress(hcols[0].Trim(), out _)) continue;
            int tempAddr = FindColIndex(hcols, IsAddrHeader);
            if (tempAddr < 0) continue;

            headerIdx = i;
            addrCol = tempAddr;
            int c;
            c = FindColIndex(hcols, IsChineseHeader);   if (c >= 0) chineseCol = c;
            c = FindColIndex(hcols, IsEnglishHeader);   if (c >= 0) englishCol = c;
            c = FindColIndex(hcols, IsReadWriteHeader); if (c >= 0) rwCol      = c;
            c = FindColIndex(hcols, IsRangeHeader);     if (c >= 0) rangeCol   = c;
            c = FindColIndex(hcols, IsUnitHeader);      if (c >= 0) unitCol    = c;
            c = FindColIndex(hcols, IsNoteHeader);      if (c >= 0) noteCol    = c;
            break;
        }

        int startRow = headerIdx >= 0 ? headerIdx + 1 : 0;
        for (int i = startRow; i < rows.Count; i++)
        {
            var cols = rows[i];
            if (addrCol >= cols.Length) continue;
            if (!TryParseAddress(cols[addrCol].Trim(), out int addr)) continue;

            var chinese = SafeGet(cols, chineseCol).Trim();
            if (string.IsNullOrWhiteSpace(chinese)) continue;

            result.Add((
                chinese,
                SafeGet(cols, englishCol).Trim(),
                addr,
                SafeGet(cols, rwCol).Trim(),
                SafeGet(cols, rangeCol).Trim(),
                SafeGet(cols, unitCol).Trim(),
                SafeGet(cols, noteCol).Trim()));
        }
        return result;
    }

    /// <summary>
    /// RFC 4180 兼容的 TSV 解析器。
    /// 正确处理 Excel 复制产生的带双引号包裹的多行单元格（Alt+Enter）。
    /// </summary>
    private static List<string[]> ParseTsvRows(string text)
    {
        var rows   = new List<string[]>();
        var fields = new List<string>();
        var sb     = new System.Text.StringBuilder();
        int i      = 0;

        while (i < text.Length)
        {
            char ch = text[i];

            if (ch == '"')
            {
                // 引号字段：消费到配对的闭合引号
                i++;
                while (i < text.Length)
                {
                    if (text[i] == '"')
                    {
                        // "" 转义为单个引号
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        { sb.Append('"'); i += 2; }
                        else
                        { i++; break; }   // 闭合引号
                    }
                    else
                    {
                        sb.Append(text[i++]);
                    }
                }
            }
            else if (ch == '\t')
            {
                fields.Add(sb.ToString());
                sb.Clear();
                i++;
            }
            else if (ch == '\r' || ch == '\n')
            {
                fields.Add(sb.ToString());
                sb.Clear();
                if (fields.Exists(f => f.Length > 0))
                    rows.Add([.. fields]);
                fields.Clear();
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                i++;
            }
            else
            {
                sb.Append(ch);
                i++;
            }
        }

        // 最后一行（无结尾换行符）
        fields.Add(sb.ToString());
        if (fields.Exists(f => f.Length > 0))
            rows.Add([.. fields]);

        return rows;
    }

    /// <summary>
    /// 从协议文档 Excel 文件解析寄存器数据。
    /// 自动定位标题行，按列名识别地址/中文名/英文名/读写/单位/描述各列。
    /// </summary>
    public static (string DeviceName, List<(string ChineseName, string EnglishName, int Address, string ReadWrite, string Range, string Unit, string Note)> Rows)
        ParseProtocolRowsFromFile(string filePath)
    {
        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheet(1);
        var deviceName = System.IO.Path.GetFileNameWithoutExtension(filePath);

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;

        // 找含 Addr/地址 的标题列
        int headerRow = -1, addrCol = -1;
        for (int c = 1; c <= Math.Min(lastCol, 12) && addrCol < 0; c++)
            for (int r = 1; r <= Math.Min(lastRow, 20); r++)
                if (IsAddrHeader(ws.Cell(r, c).GetValue<string>().Trim()))
                { addrCol = c; headerRow = r; break; }

        var rows = new List<(string, string, int, string, string, string, string)>();
        if (addrCol < 0) return (deviceName, rows);

        // 默认相对位置：英文在 Addr+2，其余在标题行找
        int chineseCol = addrCol + 1, englishCol = addrCol + 2,
            rwCol = addrCol + 3, rangeCol = addrCol + 4, unitCol = addrCol + 5, noteCol = addrCol + 6;

        for (int c = 1; c <= lastCol; c++)
        {
            var h = ws.Cell(headerRow, c).GetValue<string>().Trim();
            if      (IsChineseHeader(h))   chineseCol = c;
            else if (IsEnglishHeader(h))   englishCol = c;
            else if (IsReadWriteHeader(h)) rwCol      = c;
            else if (IsRangeHeader(h))   rangeCol   = c;
            else if (IsUnitHeader(h))      unitCol    = c;
            else if (IsNoteHeader(h))      noteCol    = c;
        }

        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var addrStr = ws.Cell(r, addrCol).GetValue<string>().Trim();
            if (!TryParseAddress(addrStr, out int addr)) continue;

            var chinese = ws.Cell(r, chineseCol).GetValue<string>().Trim();
            if (string.IsNullOrWhiteSpace(chinese)) continue;

            rows.Add((
                chinese,
                englishCol <= lastCol ? ws.Cell(r, englishCol).GetValue<string>().Trim() : string.Empty,
                addr,
                rwCol     <= lastCol ? ws.Cell(r, rwCol    ).GetValue<string>().Trim() : string.Empty,
                rangeCol  <= lastCol ? ws.Cell(r, rangeCol ).GetValue<string>().Trim() : string.Empty,
                unitCol   <= lastCol ? ws.Cell(r, unitCol  ).GetValue<string>().Trim() : string.Empty,
                noteCol   <= lastCol ? ws.Cell(r, noteCol  ).GetValue<string>().Trim() : string.Empty));
        }
        return (deviceName, rows);
    }

    // ---- 地址解析：支持十进制、0x 前缀、H 后缀十六进制 ----
    private static bool TryParseAddress(string s, out int addr)
    {
        addr = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (int.TryParse(s, out addr)) return true;
        s = s.Trim().TrimEnd('H', 'h');
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
        return int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out addr);
    }

    // ---- 列名识别 ----
    private static bool IsAddrHeader(string h) =>
        h.Equals("Addr", StringComparison.OrdinalIgnoreCase) || h.Contains("地址");
    private static bool IsChineseHeader(string h) =>
        h.Contains("中文") || h == "名称" || h.Contains("点名");
    private static bool IsEnglishHeader(string h) =>
        h.Contains("English", StringComparison.OrdinalIgnoreCase) ||
        h.Contains("英文") || h.Contains("Register meaning", StringComparison.OrdinalIgnoreCase);
    private static bool IsReadWriteHeader(string h) =>
        h.Equals("R/W", StringComparison.OrdinalIgnoreCase) ||
        h.Contains("读写") || h.Equals("RW", StringComparison.OrdinalIgnoreCase);
    private static bool IsRangeHeader(string h) =>
        h.Contains("范围") || h.Equals("Range", StringComparison.OrdinalIgnoreCase) ||
        h.Contains("量程") || h.Equals("Scaling", StringComparison.OrdinalIgnoreCase);
    private static bool IsUnitHeader(string h) =>
        h.Contains("单位") || h.Equals("Unit", StringComparison.OrdinalIgnoreCase);
    private static bool IsNoteHeader(string h) =>
        h.Contains("备注") || h.Contains("描述") || h.Contains("说明") ||
        h.Equals("Note", StringComparison.OrdinalIgnoreCase) || h.Contains("注释");

    private static int FindColIndex(string[] cols, Func<string, bool> predicate)
    {
        for (int i = 0; i < cols.Length; i++)
            if (predicate(cols[i].Trim())) return i;
        return -1;
    }

    private static string SafeGet(string[] cols, int idx)
        => idx >= 0 && idx < cols.Length ? cols[idx] : string.Empty;
}
