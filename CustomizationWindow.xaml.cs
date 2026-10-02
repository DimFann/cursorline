using System.IO;
using System.Globalization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;
using WpfColor = System.Windows.Media.Color;

namespace CursorLine;

public partial class CustomizationWindow : Window
{
    private readonly OverlaySettings _originalSettings;
    private readonly OverlaySettings _draftSettings;
    private readonly List<string> _importedBigCursorImages = [];
    private WpfColor _selectedColor;
    private bool _isUiInitialized;
    private bool _isSaved;

    internal event Action<OverlaySettings>? SettingsChanged;

    internal CustomizationWindow(OverlaySettings settings, IReadOnlyList<MonitorDisplay> displays)
    {
        InitializeComponent();
        _originalSettings = settings;
        _draftSettings = settings.Clone();
        _selectedColor = settings.GetLineColor();
        DisplayInput.ItemsSource = displays;
        DisplayInput.SelectedValue = _draftSettings.DisplayDeviceName;
        LineWidthInput.Text = _draftSettings.LineWidthPixels.ToString(CultureInfo.CurrentCulture);
        DrawLineInput.IsChecked = _draftSettings.DrawLine;
        StartCornerInput.SelectedValue = _draftSettings.StartCorner;
        WhitelistEnabledInput.IsChecked = _draftSettings.WhitelistEnabled;
        WhitelistedAppsList.ItemsSource = _draftSettings.WhitelistedApplications;
        DrawCursorCircleInput.IsChecked = _draftSettings.DrawCursorCircle;
        CircleRadiusInput.Text = _draftSettings.CursorCircleRadiusPixels.ToString(CultureInfo.CurrentCulture);
        CircleRadiusInput.IsEnabled = _draftSettings.DrawCursorCircle;
        BigCursorEnabledInput.IsChecked = _draftSettings.EnableBigCursor;
        BigCursorImageName.Text = _draftSettings.BigCursorImagePath is { Length: > 0 } imagePath
            ? Path.GetFileName(imagePath)
            : "No image selected";
        BigCursorScaleInput.Value = _draftSettings.BigCursorScalePixels;
        BigCursorScaleInput.IsEnabled = !string.IsNullOrWhiteSpace(_draftSettings.BigCursorImagePath);
        BigCursorRotationInput.Value = _draftSettings.BigCursorRotationDegrees;
        UpdateBigCursorScaleText();
        UpdateBigCursorRotationText();
        UpdateColorPreview();
        _isUiInitialized = true;
        Closing += OnClosing;
    }

    private void ChooseColor_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(_selectedColor.R, _selectedColor.G, _selectedColor.B),
            FullOpen = true,
            AnyColor = true
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            _selectedColor = WpfColor.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
            _draftSettings.SetLineColor(_selectedColor);
            UpdateColorPreview();
            NotifySettingsChanged();
        }
    }

    private void LineWidthInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (double.TryParse(LineWidthInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var width) &&
            double.IsFinite(width) && width is >= 1 and <= 100)
        {
            _draftSettings.LineWidthPixels = width;
            ValidationMessage.Text = string.Empty;
            NotifySettingsChanged();
        }
    }

    private void DrawLine_Changed(object sender, RoutedEventArgs e)
    {
        _draftSettings.DrawLine = DrawLineInput.IsChecked == true;
        NotifySettingsChanged();
    }

    private void StartCornerInput_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (StartCornerInput.SelectedValue is string startCorner)
        {
            _draftSettings.StartCorner = startCorner;
            NotifySettingsChanged();
        }
    }

    private void DisplayInput_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DisplayInput.SelectedValue is string deviceName)
        {
            _draftSettings.DisplayDeviceName = deviceName;
            NotifySettingsChanged();
        }
    }

    private void WhitelistEnabled_Changed(object sender, RoutedEventArgs e)
    {
        _draftSettings.WhitelistEnabled = WhitelistEnabledInput.IsChecked == true;
        NotifySettingsChanged();
    }

    private void AddWhitelistApplication_Click(object sender, RoutedEventArgs e)
    {
        var processName = Path.GetFileNameWithoutExtension(ApplicationNameInput.Text.Trim());
        if (string.IsNullOrWhiteSpace(processName))
        {
            ValidationMessage.Text = "Enter an application process name.";
            return;
        }

        if (_draftSettings.WhitelistedApplications.Contains(processName, StringComparer.OrdinalIgnoreCase))
        {
            ValidationMessage.Text = "That application is already on the list.";
            return;
        }

        _draftSettings.WhitelistedApplications.Add(processName);
        WhitelistedAppsList.Items.Refresh();
        WhitelistedAppsList.SelectedItem = processName;
        ApplicationNameInput.Clear();
        ValidationMessage.Text = string.Empty;
        NotifySettingsChanged();
    }

    private void RemoveWhitelistApplication_Click(object sender, RoutedEventArgs e)
    {
        if (WhitelistedAppsList.SelectedItem is not string selectedProcess)
        {
            return;
        }

        _draftSettings.WhitelistedApplications.Remove(selectedProcess);
        WhitelistedAppsList.Items.Refresh();
        ValidationMessage.Text = string.Empty;
        NotifySettingsChanged();
    }

    private void CircleRadiusInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (DrawCursorCircleInput.IsChecked != true)
        {
            return;
        }

        if (double.TryParse(CircleRadiusInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var radius) &&
            double.IsFinite(radius) && radius is >= 1 and <= 2000)
        {
            _draftSettings.CursorCircleRadiusPixels = radius;
            ValidationMessage.Text = string.Empty;
            NotifySettingsChanged();
        }
    }

    private void ImportBigCursorImage_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Filter = "PNG images (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Import big cursor image"
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
        {
            return;
        }

        try
        {
            using (var stream = File.OpenRead(dialog.FileName))
            {
                var decoder = BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                if (decoder is not PngBitmapDecoder || decoder.Frames.Count == 0 ||
                    decoder.Frames[0].PixelWidth <= 0 || decoder.Frames[0].PixelHeight <= 0)
                {
                    ValidationMessage.Text = "Choose a valid PNG image.";
                    return;
                }
            }

            var importedPath = OverlaySettings.CopyBigCursorImage(dialog.FileName);
            _importedBigCursorImages.Add(importedPath);
            _draftSettings.BigCursorImagePath = importedPath;
            BigCursorImageName.Text = Path.GetFileName(dialog.FileName);
            BigCursorScaleInput.IsEnabled = true;
            ValidationMessage.Text = string.Empty;
            NotifySettingsChanged();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          NotSupportedException or FormatException or ArgumentException)
        {
            ValidationMessage.Text = "Could not import that image. Choose a readable, valid PNG file.";
        }
    }

    private void BigCursorEnabled_Changed(object sender, RoutedEventArgs e)
    {
        _draftSettings.EnableBigCursor = BigCursorEnabledInput.IsChecked == true;
        NotifySettingsChanged();
    }

    private void BigCursorScaleInput_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isUiInitialized)
        {
            return;
        }

        _draftSettings.BigCursorScalePixels = e.NewValue;
        UpdateBigCursorScaleText();
        NotifySettingsChanged();
    }

    private void BigCursorRotationInput_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isUiInitialized)
        {
            return;
        }

        _draftSettings.BigCursorRotationDegrees = e.NewValue;
        UpdateBigCursorRotationText();
        NotifySettingsChanged();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(LineWidthInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var width) ||
            !double.IsFinite(width) || width is < 1 or > 100)
        {
            ValidationMessage.Text = "Enter a width from 1 to 100 pixels.";
            return;
        }

        if (StartCornerInput.SelectedValue is not string)
        {
            ValidationMessage.Text = "Choose a start corner.";
            return;
        }

        var radius = _draftSettings.CursorCircleRadiusPixels;
        if (DrawCursorCircleInput.IsChecked == true &&
            (!double.TryParse(CircleRadiusInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out radius) ||
             !double.IsFinite(radius) || radius is < 1 or > 2000))
        {
            ValidationMessage.Text = "Enter a circle radius from 1 to 2000 pixels.";
            return;
        }

        if (BigCursorEnabledInput.IsChecked == true &&
            (string.IsNullOrWhiteSpace(_draftSettings.BigCursorImagePath) ||
             !File.Exists(_draftSettings.BigCursorImagePath)))
        {
            ValidationMessage.Text = "Import a PNG image before enabling the big cursor.";
            return;
        }

        _draftSettings.LineWidthPixels = width;
        _draftSettings.DrawLine = DrawLineInput.IsChecked == true;
        _draftSettings.SetLineColor(_selectedColor);
        _draftSettings.DrawCursorCircle = DrawCursorCircleInput.IsChecked == true;
        _draftSettings.CursorCircleRadiusPixels = radius;
        _draftSettings.EnableBigCursor = BigCursorEnabledInput.IsChecked == true;
        _draftSettings.BigCursorScalePixels = BigCursorScaleInput.Value;
        _draftSettings.BigCursorRotationDegrees = BigCursorRotationInput.Value;
        try
        {
            _draftSettings.Save();
        }
        catch (IOException)
        {
            ValidationMessage.Text = "Could not save settings. Check that your user profile is writable.";
            return;
        }
        catch (UnauthorizedAccessException)
        {
            ValidationMessage.Text = "Could not save settings. Check that your user profile is writable.";
            return;
        }

        _isSaved = true;
        NotifySettingsChanged();
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void DrawCursorCircle_Changed(object sender, RoutedEventArgs e)
    {
        CircleRadiusInput.IsEnabled = DrawCursorCircleInput.IsChecked == true;
        _draftSettings.DrawCursorCircle = DrawCursorCircleInput.IsChecked == true;
        if (_draftSettings.DrawCursorCircle &&
            double.TryParse(CircleRadiusInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var radius) &&
            double.IsFinite(radius) && radius is >= 1 and <= 2000)
        {
            _draftSettings.CursorCircleRadiusPixels = radius;
        }

        NotifySettingsChanged();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_isSaved)
        {
            SettingsChanged?.Invoke(_originalSettings);
        }

        CleanupImportedBigCursorImages();
    }

    private void NotifySettingsChanged() => SettingsChanged?.Invoke(_draftSettings);

    private void UpdateColorPreview()
    {
        ColorPreview.Background = new SolidColorBrush(_selectedColor);
        ColorHexText.Text = $"#{_selectedColor.R:X2}{_selectedColor.G:X2}{_selectedColor.B:X2}";
    }

    private void UpdateBigCursorScaleText() =>
        BigCursorScaleText.Text = $"{BigCursorScaleInput.Value:0} px";

    private void UpdateBigCursorRotationText() =>
        BigCursorRotationText.Text = $"{BigCursorRotationInput.Value:0.#}°";

    private void CleanupImportedBigCursorImages()
    {
        foreach (var imagePath in _importedBigCursorImages)
        {
            if (_isSaved && string.Equals(
                    imagePath,
                    _draftSettings.BigCursorImagePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(imagePath);
            }
            catch (IOException)
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"Could not remove a temporary imported cursor image:\n{imagePath}",
                    "CursorLine",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (UnauthorizedAccessException)
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"Could not remove a temporary imported cursor image:\n{imagePath}",
                    "CursorLine",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
