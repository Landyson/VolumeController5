using System;
using System.IO.Ports;
using System.Linq;
using System.Windows;

namespace VolumeController5;

public partial class PortSettingsWindow : Window
{
    public string? SelectedUsbPort { get; private set; }
    public string? SelectedBtPort { get; private set; }

    public PortSettingsWindow(string? currentUsbPort, string? currentBtPort)
    {
        InitializeComponent();

        LoadPorts(currentUsbPort, currentBtPort);

        RefreshBtn.Click += (_, __) => LoadPorts(SelectedUsbPort, SelectedBtPort);
        ClearBtn.Click += (_, __) =>
        {
            SelectedUsbPort = null;
            SelectedBtPort = null;
            LoadPorts(null, null);
        };

        OkBtn.Click += (_, __) =>
        {
            SelectedUsbPort = UsbPortCombo.SelectedItem as string;
            if (string.Equals(SelectedUsbPort, "(auto)", StringComparison.OrdinalIgnoreCase))
                SelectedUsbPort = null;

            SelectedBtPort = BtPortCombo.SelectedItem as string;
            if (string.Equals(SelectedBtPort, "(auto)", StringComparison.OrdinalIgnoreCase))
                SelectedBtPort = null;

            DialogResult = true;
            Close();
        };

        CancelBtn.Click += (_, __) =>
        {
            DialogResult = false;
            Close();
        };
    }

    private void LoadPorts(string? usbSelect, string? btSelect)
    {
        var ports = SerialPort.GetPortNames().OrderBy(x => x).ToList();
        ports.Insert(0, "(auto)");

        UsbPortCombo.ItemsSource = ports;
        BtPortCombo.ItemsSource = ports;

        if (!string.IsNullOrWhiteSpace(usbSelect) && ports.Contains(usbSelect))
            UsbPortCombo.SelectedItem = usbSelect;
        else
            UsbPortCombo.SelectedIndex = 0;

        if (!string.IsNullOrWhiteSpace(btSelect) && ports.Contains(btSelect))
            BtPortCombo.SelectedItem = btSelect;
        else
            BtPortCombo.SelectedIndex = 0;
    }
}
