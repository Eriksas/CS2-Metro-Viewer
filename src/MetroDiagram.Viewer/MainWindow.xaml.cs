using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MetroDiagram.Core;
using MetroDiagram.Core.Loading;
using MetroDiagram.Core.Models;
using MetroDiagram.Rendering;
using Microsoft.Win32;

namespace MetroDiagram.Viewer;

public partial class MainWindow : Window
{
    private readonly MetroSvgRenderer _renderer = new();
    private MetroExportDocument? _document;
    private string? _jsonPath;
    private string? _currentSvg;
    private string? _defaultExportPath;
    private ViewerSettings _settings = new();
    private string _language = "en";
    private bool _uiReady;
    private bool _suppressUiEvents;

    public MainWindow()
    {
        InitializeComponent();

        _settings = ViewerSettingsStore.Load();
        _language = NormalizeLanguage(_settings.Language);
        ApplySettingsToUi(_settings);
        ApplyLanguage();
        RefreshDefaultExportState(showStatus: false);
        UpdateSummary(null, null);
        ClearPreview(T("InitialPreview"));
        SetStatus(_defaultExportPath is null ? T("Ready") : string.Format(CultureInfo.CurrentCulture, T("DefaultFound"), _defaultExportPath));

        _uiReady = true;
    }

    private void OpenJson_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = "Metro JSON (*.json)|*.json|All files (*.*)|*.*",
            Title = T("OpenMetroJsonTitle")
        };

        string? initialDirectory = GetInitialOpenDirectory();
        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        LoadDocument(dialog.FileName);
    }

    private void OpenDefaultExport_Click(object sender, RoutedEventArgs e)
    {
        RefreshDefaultExportState(showStatus: false);
        if (string.IsNullOrWhiteSpace(_defaultExportPath))
        {
            SetStatus(T("DefaultMissing"));
            return;
        }

        LoadDocument(_defaultExportPath);
    }

    private void OpenExportFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = GetPreferredExportFolder(createIfMissing: true);
            Process.Start(new ProcessStartInfo("explorer.exe", folder)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("OpenFolderFailed"), ex.Message));
        }
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        ViewerSettings defaults = new()
        {
            LastJsonPath = _jsonPath ?? _settings.LastJsonPath,
            Language = _language
        };

        _settings = defaults;
        ApplySettingsToUi(defaults);
        ClearError();
        SetStatus(T("DefaultsReset"));
        TrySaveCurrentSettings(showError: true);

        if (_document is not null)
        {
            RenderPreview();
        }
    }

    private void RefreshPreview_Click(object sender, RoutedEventArgs e)
    {
        RenderPreview();
    }

    private void SaveSvg_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentSvg))
        {
            SetError(T("NoSvgToSave"));
            return;
        }

        SaveFileDialog dialog = new()
        {
            Filter = "SVG files (*.svg)|*.svg|All files (*.*)|*.*",
            Title = T("SaveSvgTitle"),
            FileName = BuildDefaultSvgFileName()
        };

        string folder = GetPreferredExportFolder(createIfMissing: false);
        if (Directory.Exists(folder))
        {
            dialog.InitialDirectory = folder;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _currentSvg, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            SetStatus(string.Format(CultureInfo.CurrentCulture, T("SvgSaved"), dialog.FileName));
            ClearError();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("SaveFailed"), ex.Message));
        }
    }

    private void LayoutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _suppressUiEvents)
        {
            return;
        }

        if (_document is not null)
        {
            RenderPreview();
            return;
        }

        TrySaveCurrentSettings(showError: false);
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _suppressUiEvents)
        {
            return;
        }

        _language = ReadSelectedLanguage();
        ApplyLanguage();
        if (_document is null)
        {
            ClearPreview(T("InitialPreview"));
        }

        TrySaveCurrentSettings(showError: false);
        RefreshDefaultExportState(showStatus: true);
    }

    private void RenderOptionChanged(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _suppressUiEvents)
        {
            return;
        }

        if (_document is not null)
        {
            RenderPreview();
            return;
        }

        TrySaveCurrentSettings(showError: false);
    }

    private void LoadDocument(string path)
    {
        try
        {
            MetroLoadResult loadResult = MetroJsonLoader.LoadFromFile(path);
            if (!loadResult.IsValid || loadResult.Document is null)
            {
                _document = null;
                _jsonPath = null;
                _currentSvg = null;
                SaveButton.IsEnabled = false;
                ClearPreview(T("JsonCouldNotLoad"));
                UpdateSummary(null, null);
                SetError(string.Format(CultureInfo.CurrentCulture, T("InvalidJson"), string.Join(Environment.NewLine, loadResult.Errors)));
                SetStatus(T("JsonLoadFailed"));
                return;
            }

            _document = loadResult.Document;
            _jsonPath = path;
            FileTextBlock.Text = path;
            UpdateSummary(_document, path);
            ClearError();
            SetStatus(loadResult.Warnings.Count == 0
                ? T("JsonLoaded")
                : string.Format(CultureInfo.CurrentCulture, T("JsonLoadedWarnings"), string.Join(" | ", loadResult.Warnings)));
            TrySaveCurrentSettings(showError: false);
            RenderPreview();
        }
        catch (Exception ex)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("InvalidJson"), ex.Message));
            SetStatus(T("JsonLoadFailed"));
        }
    }

    private void RenderPreview()
    {
        if (_document is null)
        {
            SetError(T("RenderFirst"));
            return;
        }

        try
        {
            SvgRenderOptions options = ReadRenderOptions();
            SvgRenderResult renderResult = _renderer.Render(_document, options);
            _currentSvg = renderResult.Svg;
            SaveButton.IsEnabled = true;
            WritePreviewHtml(renderResult.Svg);
            ClearError();
            SetStatus(renderResult.Warnings.Count == 0
                ? string.Format(CultureInfo.CurrentCulture, T("Rendered"), GetLayoutModeText(options.LayoutMode))
                : string.Format(CultureInfo.CurrentCulture, T("RenderedWarnings"), string.Join(" | ", renderResult.Warnings)));
            TrySaveCurrentSettings(showError: false);
        }
        catch (Exception ex)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("RenderFailed"), ex.Message));
            SetStatus(T("RenderFailed").Replace("{0}", ex.Message, StringComparison.Ordinal));
        }
    }

    private SvgRenderOptions ReadRenderOptions()
    {
        int width = ReadPositiveInt(WidthTextBox, T("Width"));
        int height = ReadPositiveInt(HeightTextBox, T("Height"));
        int legendWidth = ReadPositiveInt(LegendWidthTextBox, T("Legend"));
        int padding = ReadPositiveInt(PaddingTextBox, T("Padding"));
        double lineWidth = ReadPositiveDouble(LineWidthTextBox, T("Line"));
        double stationRadius = ReadPositiveDouble(StationRadiusTextBox, T("Station"));
        double labelFontSize = ReadPositiveDouble(LabelFontSizeTextBox, T("Label"));
        double gridSize = ReadPositiveDouble(GridSizeTextBox, T("Grid"));
        SvgLayoutMode layoutMode = ReadSelectedLayoutMode();

        return new SvgRenderOptions
        {
            LayoutMode = layoutMode,
            Width = width,
            Height = height,
            Padding = padding,
            Margin = padding,
            LegendWidth = legendWidth,
            LineWidth = lineWidth,
            StationRadius = stationRadius,
            InterchangeStationRadius = Math.Max(stationRadius + 3.5, stationRadius * 1.45),
            LabelFontSize = labelFontSize,
            GridSize = gridSize,
            HideGenericStationLabels = HideGenericCheckBox.IsChecked == true,
            HideCrowdedLabels = HideCrowdedCheckBox.IsChecked == true,
            AlwaysShowInterchanges = AlwaysInterchangesCheckBox.IsChecked == true,
            AlwaysShowTerminals = AlwaysTerminalsCheckBox.IsChecked == true
        };
    }

    private SvgLayoutMode ReadSelectedLayoutMode()
    {
        string? tag = (LayoutComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return string.Equals(tag, "schematic-lite", StringComparison.Ordinal)
            ? SvgLayoutMode.SchematicLite
            : SvgLayoutMode.Geographic;
    }

    private string ReadSelectedLanguage()
    {
        string? tag = (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return NormalizeLanguage(tag);
    }

    private int ReadPositiveInt(TextBox textBox, string label)
    {
        if (!int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, T("PositiveInteger"), label));
        }

        return value;
    }

    private double ReadPositiveDouble(TextBox textBox, string label)
    {
        if (!double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value <= 0)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, T("PositiveNumber"), label));
        }

        return value;
    }

    private void WritePreviewHtml(string svg)
    {
        PreviewBrowser.NavigateToString(BuildPreviewHtml(svg));
    }

    private void ClearPreview(string message)
    {
        string escapedMessage = System.Security.SecurityElement.Escape(message) ?? string.Empty;
        string svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="960" height="540" viewBox="0 0 960 540">
              <rect width="960" height="540" fill="#ffffff" />
              <text x="48" y="72" font-family="Arial, sans-serif" font-size="20" font-weight="600" fill="#52616f">{escapedMessage}</text>
            </svg>
            """;
        PreviewBrowser.NavigateToString(BuildPreviewHtml(svg));
    }

    private static string BuildPreviewHtml(string svg)
    {
        return """
            <!doctype html>
            <html>
            <head>
              <meta charset="utf-8">
              <style>
                html, body { margin: 0; min-height: 100%; background: #f4f6f8; }
                body { padding: 16px; box-sizing: border-box; }
                svg { display: block; max-width: 100%; height: auto; margin: 0 auto; box-shadow: 0 1px 4px rgba(16, 24, 40, 0.18); }
              </style>
            </head>
            <body>
            """ + svg + """
            </body>
            </html>
            """;
    }

    private void UpdateSummary(MetroExportDocument? document, string? path)
    {
        if (document is null)
        {
            FileTextBlock.Text = T("NoJsonLoaded");
            SummaryTextBlock.Text = T("SummaryEmpty");
            return;
        }

        string city = string.IsNullOrWhiteSpace(document.City?.Name) ? T("UnnamedCity") : document.City!.Name!;
        int lineCount = document.Network?.Lines?.Count ?? 0;
        int stationCount = document.Network?.Stations?.Count ?? 0;
        FileTextBlock.Text = path ?? T("JsonLoadedShort");
        SummaryTextBlock.Text = string.Format(CultureInfo.CurrentCulture, T("Summary"), city, lineCount, stationCount);
    }

    private string BuildDefaultSvgFileName()
    {
        string layout = GetLayoutModeText(ReadSelectedLayoutMode());
        string baseName = string.IsNullOrWhiteSpace(_jsonPath)
            ? "metro-diagram"
            : Path.GetFileNameWithoutExtension(_jsonPath);
        return $"{baseName}.{layout}.svg";
    }

    private static string GetLayoutModeText(SvgLayoutMode layoutMode)
    {
        return layoutMode == SvgLayoutMode.SchematicLite ? "schematic-lite" : "geographic";
    }

    private void RefreshDefaultExportState(bool showStatus)
    {
        _defaultExportPath = FindDefaultExportPath();
        OpenDefaultButton.IsEnabled = _defaultExportPath is not null;

        if (showStatus)
        {
            SetStatus(_defaultExportPath is null
                ? T("DefaultMissing")
                : string.Format(CultureInfo.CurrentCulture, T("DefaultFound"), _defaultExportPath));
        }
    }

    private static string? FindDefaultExportPath()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string[] candidates =
        [
            @"D:\CS2MetroDiagram\metro-export.json",
            string.IsNullOrWhiteSpace(documents)
                ? string.Empty
                : Path.Combine(documents, "CS2MetroDiagram", "metro-export.json")
        ];

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private string? GetInitialOpenDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_settings.LastJsonPath))
        {
            string? folder = Path.GetDirectoryName(_settings.LastJsonPath);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                return folder;
            }
        }

        if (!string.IsNullOrWhiteSpace(_defaultExportPath))
        {
            string? folder = Path.GetDirectoryName(_defaultExportPath);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                return folder;
            }
        }

        if (Directory.Exists(@"D:\CS2MetroDiagram"))
        {
            return @"D:\CS2MetroDiagram";
        }

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string documentsExportFolder = string.IsNullOrWhiteSpace(documents)
            ? string.Empty
            : Path.Combine(documents, "CS2MetroDiagram");
        return Directory.Exists(documentsExportFolder) ? documentsExportFolder : null;
    }

    private string GetPreferredExportFolder(bool createIfMissing)
    {
        List<string> candidates = [];

        if (!string.IsNullOrWhiteSpace(_defaultExportPath))
        {
            string? folder = Path.GetDirectoryName(_defaultExportPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                candidates.Add(folder);
            }
        }

        if (!string.IsNullOrWhiteSpace(_jsonPath))
        {
            string? folder = Path.GetDirectoryName(_jsonPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                candidates.Add(folder);
            }
        }

        candidates.Add(@"D:\CS2MetroDiagram");

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            candidates.Add(Path.Combine(documents, "CS2MetroDiagram"));
        }

        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        string fallback = candidates.Last();
        if (createIfMissing)
        {
            Directory.CreateDirectory(fallback);
        }

        return fallback;
    }

    private void ApplySettingsToUi(ViewerSettings settings)
    {
        _suppressUiEvents = true;
        try
        {
            SelectComboBoxItem(LayoutComboBox, settings.LayoutMode);
            SelectComboBoxItem(LanguageComboBox, NormalizeLanguage(settings.Language));
            WidthTextBox.Text = settings.Width.ToString(CultureInfo.InvariantCulture);
            HeightTextBox.Text = settings.Height.ToString(CultureInfo.InvariantCulture);
            LegendWidthTextBox.Text = settings.LegendWidth.ToString(CultureInfo.InvariantCulture);
            PaddingTextBox.Text = settings.Padding.ToString(CultureInfo.InvariantCulture);
            LineWidthTextBox.Text = settings.LineWidth.ToString(CultureInfo.InvariantCulture);
            StationRadiusTextBox.Text = settings.StationRadius.ToString(CultureInfo.InvariantCulture);
            LabelFontSizeTextBox.Text = settings.LabelFontSize.ToString(CultureInfo.InvariantCulture);
            GridSizeTextBox.Text = settings.GridSize.ToString(CultureInfo.InvariantCulture);
            HideGenericCheckBox.IsChecked = settings.HideGenericStationLabels;
            HideCrowdedCheckBox.IsChecked = settings.HideCrowdedLabels;
            AlwaysInterchangesCheckBox.IsChecked = settings.AlwaysShowInterchanges;
            AlwaysTerminalsCheckBox.IsChecked = settings.AlwaysShowTerminals;
        }
        finally
        {
            _suppressUiEvents = false;
        }
    }

    private void ApplyLanguage()
    {
        _language = ReadSelectedLanguage();
        Title = $"{T("WindowTitle")} {MetroDiagramAppInfo.Version}";
        OpenButton.Content = T("OpenJson");
        OpenDefaultButton.Content = T("OpenDefaultExport");
        OpenFolderButton.Content = T("OpenExportFolder");
        ResetButton.Content = T("ResetDefaults");
        RefreshButton.Content = T("RefreshPreview");
        SaveButton.Content = T("SaveSvg");
        LayoutLabelTextBlock.Text = T("Layout");
        LanguageLabelTextBlock.Text = T("Language");
        WidthLabelTextBlock.Text = T("Width");
        HeightLabelTextBlock.Text = T("Height");
        LegendLabelTextBlock.Text = T("Legend");
        PaddingLabelTextBlock.Text = T("Padding");
        LineLabelTextBlock.Text = T("Line");
        StationLabelTextBlock.Text = T("Station");
        LabelFontLabelTextBlock.Text = T("Label");
        GridLabelTextBlock.Text = T("Grid");
        HideGenericCheckBox.Content = T("HideGeneric");
        HideCrowdedCheckBox.Content = T("HideCrowded");
        AlwaysInterchangesCheckBox.Content = T("AlwaysInterchanges");
        AlwaysTerminalsCheckBox.Content = T("AlwaysTerminals");
        UpdateSummary(_document, _jsonPath);
    }

    private void TrySaveCurrentSettings(bool showError)
    {
        try
        {
            _settings = BuildSettingsFromUi();
            ViewerSettingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (showError)
            {
                SetError(string.Format(CultureInfo.CurrentCulture, T("SettingsSaveFailed"), ex.Message));
            }
        }
    }

    private ViewerSettings BuildSettingsFromUi()
    {
        return new ViewerSettings
        {
            LastJsonPath = _jsonPath ?? _settings.LastJsonPath,
            Language = _language,
            LayoutMode = GetLayoutModeText(ReadSelectedLayoutMode()),
            Width = ReadIntOrDefault(WidthTextBox, _settings.Width),
            Height = ReadIntOrDefault(HeightTextBox, _settings.Height),
            LegendWidth = ReadIntOrDefault(LegendWidthTextBox, _settings.LegendWidth),
            Padding = ReadIntOrDefault(PaddingTextBox, _settings.Padding),
            LineWidth = ReadDoubleOrDefault(LineWidthTextBox, _settings.LineWidth),
            StationRadius = ReadDoubleOrDefault(StationRadiusTextBox, _settings.StationRadius),
            LabelFontSize = ReadDoubleOrDefault(LabelFontSizeTextBox, _settings.LabelFontSize),
            GridSize = ReadDoubleOrDefault(GridSizeTextBox, _settings.GridSize),
            HideGenericStationLabels = HideGenericCheckBox.IsChecked == true,
            HideCrowdedLabels = HideCrowdedCheckBox.IsChecked == true,
            AlwaysShowInterchanges = AlwaysInterchangesCheckBox.IsChecked == true,
            AlwaysShowTerminals = AlwaysTerminalsCheckBox.IsChecked == true
        };
    }

    private static void SelectComboBoxItem(ComboBox comboBox, string tag)
    {
        foreach (object item in comboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem
                && string.Equals(comboBoxItem.Tag?.ToString(), tag, StringComparison.Ordinal))
            {
                comboBox.SelectedItem = comboBoxItem;
                return;
            }
        }
    }

    private static int ReadIntOrDefault(TextBox textBox, int fallback)
    {
        return int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value
            : fallback;
    }

    private static double ReadDoubleOrDefault(TextBox textBox, double fallback)
    {
        return double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0
            ? value
            : fallback;
    }

    private static string NormalizeLanguage(string? language)
    {
        return string.Equals(language, "zh", StringComparison.Ordinal) ? "zh" : "en";
    }

    private string T(string key)
    {
        return ViewerResources.Text(_language, key);
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    private void SetError(string message)
    {
        ErrorTextBlock.Text = message;
    }

    private void ClearError()
    {
        ErrorTextBlock.Text = string.Empty;
    }

    protected override void OnClosed(EventArgs e)
    {
        TrySaveCurrentSettings(showError: false);
        base.OnClosed(e);
    }
}
