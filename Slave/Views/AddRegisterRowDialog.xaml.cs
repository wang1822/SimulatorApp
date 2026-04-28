using System.Windows;
using System.Windows.Input;

namespace SimulatorApp.Slave.Views;

public partial class AddRegisterRowDialog : Window
{
    public int    ResultAddress     { get; private set; }
    public string ResultChineseName { get; private set; } = "";
    public string ResultEnglishName { get; private set; } = "";
    public string ResultReadWrite   { get; private set; } = "";
    public string ResultRange       { get; private set; } = "";
    public string ResultUnit        { get; private set; } = "";
    public string ResultNote        { get; private set; } = "";

    public AddRegisterRowDialog() => InitializeComponent();

    private void Confirm_Click(object sender, RoutedEventArgs e) => TryConfirm();
    private void Cancel_Click(object sender, RoutedEventArgs e)  => DialogResult = false;

    private void Dialog_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  { TryConfirm(); e.Handled = true; }
        if (e.Key == Key.Escape) { DialogResult = false; }
    }

    private void TryConfirm()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var addrText = AddressBox.Text.Trim();
        int address;
        if (addrText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(addrText[2..], System.Globalization.NumberStyles.HexNumber, null, out address))
            {
                ShowError("地址格式无效，请输入十进制数字或 0x 开头的十六进制。");
                return;
            }
        }
        else if (!int.TryParse(addrText, out address))
        {
            ShowError("地址格式无效，请输入十进制数字或 0x 开头的十六进制。");
            return;
        }

        if (address < 0 || address > 65535)
        {
            ShowError("地址超出范围（0–65535）。");
            return;
        }

        ResultAddress     = address;
        ResultChineseName = ChineseNameBox.Text.Trim();
        ResultEnglishName = EnglishNameBox.Text.Trim();
        ResultReadWrite   = ReadWriteBox.Text.Trim();
        ResultRange       = RangeBox.Text.Trim();
        ResultUnit        = UnitBox.Text.Trim();
        ResultNote        = NoteBox.Text.Trim();
        DialogResult      = true;
    }

    private void ShowError(string msg)
    {
        ErrorText.Text       = msg;
        ErrorText.Visibility = Visibility.Visible;
        AddressBox.Focus();
    }
}
