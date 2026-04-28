using ClosedXML.Excel;
using SimulatorApp.Master.Models;
using System.Globalization;

namespace SimulatorApp.Master.Services;

/// <summary>
/// 主站 Excel 导入工具。
/// 解析通讯协议表格格式（与 GS215 EMS 对外 Modbus 通讯协议一致）：
/// 起始地址 | 寄存器数量 | 变量名 | 中文名 | 读写 | 单位 | 数据类型 | 寄存器数据类型 | 比例系数 | 偏移量 | 取值范围 | 说明
/// </summary>
public static class MasterExcelHelper
{
    /// <summary>
    /// 从 Excel 文件第一个 Sheet 导入主站寄存器配置列表。
    /// 自动跳过表头行、段落标题行（首列非数字）、空行和含删除线的行。
    /// </summary>
    /// <param name="filePath">Excel 文件路径</param>
    /// <param name="category">0=遥测 1=遥控</param>
    public static List<MasterRegisterConfig> ImportRegisterConfigs(string filePath, int category = 0)
    {
        using var wb = new XLWorkbook(filePath);
        // 优先按 category 匹配 Sheet 名，找不到则用第一个 Sheet
        string preferName = category == 1 ? "遥调遥控" : "遥测";
        var ws = wb.Worksheets.FirstOrDefault(s => s.Name == preferName)
                 ?? wb.Worksheet(1);
        return ParseSheet(ws, category);
    }

    /// <summary>
    /// 从 Excel 文件按 Sheet 名称导入。
    /// </summary>
    public static List<MasterRegisterConfig> ImportRegisterConfigs(string filePath, string sheetName, int category = 0)
    {
        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheet(sheetName);
        return ParseSheet(ws, category);
    }

    /// <summary>
    /// 从剪贴板 TSV 文本解析（从 Excel 复制粘贴的格式相同）。
    /// html 为 Clipboard.GetData(DataFormats.Html) 的原始内容，用于识别删除线行并跳过；
    /// 传 null 时不做删除线过滤。
    /// </summary>
    public static List<MasterRegisterConfig> ImportFromClipboard(string tsv, string? html = null, int category = 0)
    {
        var strikeRows = html != null ? StrikethroughRowIndices(html) : [];

        var list = new List<MasterRegisterConfig>();
        int rowIdx = 0;
        foreach (var line in tsv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = line.Split('\t');
            if (cols.Length >= 2
                && int.TryParse(cols[0].Trim(), out int startAddr)
                && !strikeRows.Contains(rowIdx))
            {
                list.Add(BuildConfig(cols, startAddr, category));
            }
            rowIdx++;
        }
        return list;
    }

    // ── 私有 ────────────────────────────────────────────────────────────

    private static List<MasterRegisterConfig> ParseSheet(IXLWorksheet ws, int category)
    {
        var list = new List<MasterRegisterConfig>();
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (int row = 1; row <= lastRow; row++)
        {
            var addrStr = ws.Cell(row, 1).GetValue<string>().Trim();
            if (!int.TryParse(addrStr, out int startAddr)) continue; // 跳过标题/空行

            // 跳过含删除线的行（检查地址列，删除线通常整行应用）
            if (ws.Cell(row, 1).Style.Font.Strikethrough) continue;

            int.TryParse(ws.Cell(row, 2).GetValue<string>().Trim(), out int qty);
            if (qty <= 0) qty = 1;

            double.TryParse(ws.Cell(row, 9).GetValue<string>().Trim(),
                NumberStyles.Any, CultureInfo.InvariantCulture, out double scale);
            if (scale == 0) scale = 1.0;

            double.TryParse(ws.Cell(row, 10).GetValue<string>().Trim(),
                NumberStyles.Any, CultureInfo.InvariantCulture, out double offset);

            list.Add(new MasterRegisterConfig
            {
                StartAddress     = startAddr,
                Quantity         = qty,
                VariableName     = ws.Cell(row, 3).GetValue<string>().Trim(),
                ChineseName      = ws.Cell(row, 4).GetValue<string>().Trim(),
                ReadWrite        = NvOrDefault(ws.Cell(row, 5).GetValue<string>().Trim(), "R"),
                Unit             = ws.Cell(row, 6).GetValue<string>().Trim(),
                DataType         = NormalizeDataType(ws.Cell(row, 7).GetValue<string>().Trim()),
                RegisterDataType = NormalizeDataType(ws.Cell(row, 8).GetValue<string>().Trim()),
                ScaleFactor      = scale,
                Offset           = offset,
                ValueRange       = ws.Cell(row, 11).GetValue<string>().Trim(),
                Description      = ws.Cell(row, 12).GetValue<string>().Trim(),
                Category         = category
            });
        }
        return list;
    }

    /// <summary>
    /// 解析 Excel HTML 剪贴板片段，返回含删除线的行索引集合（0-based，与 TSV 行序对应）。
    /// 判断依据：行内任意单元格含 text-decoration:line-through 或 &lt;s&gt; 标签。
    /// </summary>
    private static HashSet<int> StrikethroughRowIndices(string html)
    {
        var result = new HashSet<int>();
        int rowIdx = 0, pos = 0;
        while (true)
        {
            int trStart = html.IndexOf("<tr", pos, StringComparison.OrdinalIgnoreCase);
            if (trStart < 0) break;
            int trEnd = html.IndexOf("</tr>", trStart, StringComparison.OrdinalIgnoreCase);
            if (trEnd < 0) break;

            var rowHtml = html.Substring(trStart, trEnd - trStart + 5);
            if (rowHtml.IndexOf("line-through", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rowHtml.IndexOf("<s>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rowHtml.IndexOf("<s ", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result.Add(rowIdx);
            }

            rowIdx++;
            pos = trEnd + 5;
        }
        return result;
    }

    private static MasterRegisterConfig BuildConfig(string[] cols, int startAddr, int category)
    {
        // cols 是 0-based（Split('\t') 产生）：
        // [0]=起始地址 [1]=数量 [2]=变量名 [3]=中文名 [4]=读写 [5]=单位
        // [6]=数据类型 [7]=寄存器中数据类型 [8]=比例系数 [9]=偏移量 [10]=取值范围 [11]=说明
        C(cols, 1, out int qty, 1);
        D(cols, 8, out double scale, 1.0); if (scale == 0) scale = 1.0;
        D(cols, 9, out double offset, 0.0);

        return new MasterRegisterConfig
        {
            StartAddress     = startAddr,
            Quantity         = qty,
            VariableName     = S(cols, 2),
            ChineseName      = S(cols, 3),
            ReadWrite        = NvOrDefault(S(cols, 4), "R"),
            Unit             = S(cols, 5),
            DataType         = NormalizeDataType(S(cols, 6)),
            RegisterDataType = NormalizeDataType(S(cols, 7)),
            ScaleFactor      = scale,
            Offset           = offset,
            ValueRange       = S(cols, 10),
            Description      = S(cols, 11),
            Category         = category
        };
    }

    private static string NormalizeDataType(string dt) => dt.ToLowerInvariant() switch
    {
        "uint32" or "u32"        => "uint32",
        "int32"  or "s32"        => "int32",
        "float"  or "float32"    => "float",
        "int16"  or "s16"        => "int16",
        _                        => "uint16"
    };

    private static string NvOrDefault(string v, string def) =>
        string.IsNullOrWhiteSpace(v) ? def : v;

    private static string S(string[] cols, int idx) =>
        idx < cols.Length ? cols[idx].Trim() : string.Empty;

    private static void C(string[] cols, int idx, out int val, int def) =>
        val = idx < cols.Length && int.TryParse(cols[idx].Trim(), out int v) ? v : def;

    private static void D(string[] cols, int idx, out double val, double def) =>
        val = idx < cols.Length && double.TryParse(cols[idx].Trim(),
            NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : def;
}
