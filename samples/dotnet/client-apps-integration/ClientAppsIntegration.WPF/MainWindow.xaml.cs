using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace ClientAppsIntegration.WPF;

/// <summary>
/// Interaction logic for MainWindow.xaml.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ActivitySource _activitySource = new(Program.HostEnvironment?.ApplicationName ?? "");
    private readonly ILogger<MainWindow> _logger;
    private readonly WeatherApiClient _weatherApiClient;
    private readonly CancellationTokenSource _closingCts = new();

    public MainWindow(ILogger<MainWindow> logger, WeatherApiClient weatherApiClient)
    {
        _logger = logger;
        _weatherApiClient = weatherApiClient;

        InitializeComponent();

        ShowEmptyState();
    }

    private async void btnLoad_Click(object sender, RoutedEventArgs e)
    {
        using var activity = _activitySource.StartActivity("Load Weather", ActivityKind.Client);

        SetLoading(true);

        try
        {
            if (chkForceError.IsChecked == true)
            {
                throw new InvalidOperationException("Forced error! Toggle off \"Force error\" to call the weather API.");
            }

            var weather = await _weatherApiClient.GetWeatherAsync(_closingCts.Token);
            RenderForecasts(weather);
        }
        catch (TaskCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Operation was canceled");
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Error loading weather");
            }

            ShowError(ex.Message);
        }
        finally
        {
            SetLoading(false);
        }
    }

    /// <summary>
    /// Toggles the loading affordance: disables input and shows the indeterminate progress bar.
    /// </summary>
    public void SetLoading(bool isLoading)
    {
        btnLoad.IsEnabled = !isLoading;
        chkForceError.IsEnabled = !isLoading;
        pbLoading.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;

        if (isLoading)
        {
            SetStatus("Loading forecast from apiservice…");
        }
    }

    /// <summary>
    /// Renders the five-day forecast strip. Called by the real button handler and by the screenshot harness.
    /// </summary>
    public void RenderForecasts(IReadOnlyList<WeatherForecast> forecasts)
    {
        forecastGrid.Children.Clear();
        forecastGrid.Columns = Math.Max(1, forecasts.Count);

        foreach (var forecast in forecasts)
        {
            forecastGrid.Children.Add(BuildForecastCard(forecast));
        }

        forecastGrid.Visibility = Visibility.Visible;
        emptyState.Visibility = Visibility.Collapsed;
        errorState.Visibility = Visibility.Collapsed;

        SetStatus($"Loaded {forecasts.Count} forecasts from apiservice");
    }

    /// <summary>
    /// Replaces the old blocking MessageBox with an inline, themed error card.
    /// </summary>
    public void ShowError(string message)
    {
        errorText.Text = message;

        errorState.Visibility = Visibility.Visible;
        forecastGrid.Visibility = Visibility.Collapsed;
        emptyState.Visibility = Visibility.Collapsed;

        SetStatus(message);
    }

    /// <summary>
    /// Shows the friendly first-run prompt before any forecast has been loaded.
    /// </summary>
    public void ShowEmptyState()
    {
        emptyState.Visibility = Visibility.Visible;
        forecastGrid.Visibility = Visibility.Collapsed;
        errorState.Visibility = Visibility.Collapsed;

        SetStatus("Ready");
    }

    private void SetStatus(string text) => txtStatus.Text = text;

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) => _closingCts.Cancel();

    // ----- Card construction (kept in code so per-day glyphs and the accent ramp render deterministically) -----

    private enum GlyphKind
    {
        Snow,
        CloudSun,
        Sun,
        HotSun
    }

    private static readonly Brush CardBrush = FrozenBrush("#211E40");
    private static readonly Brush CardBorderBrush = FrozenBrush("#3B356B");
    private static readonly Brush TextPrimaryBrush = FrozenBrush("#F4F2FF");
    private static readonly Brush TextSecondaryBrush = FrozenBrush("#B9B2E0");
    private static readonly Brush AccentBrush = FrozenBrush("#FFB454");
    private static readonly Brush CoralBrush = FrozenBrush("#FF7A5C");
    private static readonly Brush IcyBrush = FrozenBrush("#8FB7FF");
    private static readonly Brush CloudBrush = FrozenBrush("#D7D0F5");

    private static FrameworkElement BuildForecastCard(WeatherForecast forecast)
    {
        var culture = CultureInfo.InvariantCulture;
        var weekday = forecast.Date.ToString("ddd", culture);
        var dayLabel = forecast.Date.ToString("MMM d", culture);
        var summary = string.IsNullOrWhiteSpace(forecast.Summary) ? "Unknown" : forecast.Summary!;

        var content = new StackPanel();

        content.Children.Add(new TextBlock
        {
            Text = weekday,
            Foreground = TextPrimaryBrush,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        content.Children.Add(new TextBlock
        {
            Text = dayLabel,
            Foreground = TextSecondaryBrush,
            FontSize = 12.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0)
        });

        var glyph = BuildGlyph(PickGlyph(forecast));
        glyph.Width = 66;
        glyph.Height = 66;
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.Margin = new Thickness(0, 14, 0, 14);
        content.Children.Add(glyph);

        var tempRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        tempRow.Children.Add(new TextBlock
        {
            Text = $"{forecast.TemperatureC}°",
            Foreground = TextPrimaryBrush,
            FontSize = 36,
            FontWeight = FontWeights.Bold
        });
        tempRow.Children.Add(new TextBlock
        {
            Text = $"{forecast.TemperatureF}°F",
            Foreground = TextSecondaryBrush,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(6, 0, 0, 7)
        });
        content.Children.Add(tempRow);

        content.Children.Add(new TextBlock
        {
            Text = summary,
            Foreground = TextPrimaryBrush,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        });

        // Decorative temperature ramp bar (color reinforces, never replaces, the numbers + word above).
        var accentBar = new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = RampBrush(forecast.TemperatureC),
            Margin = new Thickness(2, 16, 2, 0)
        };
        content.Children.Add(accentBar);

        var card = new Border
        {
            Background = CardBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16, 18, 16, 16),
            Margin = new Thickness(9, 0, 9, 0),
            MinWidth = 150,
            Child = content,
            Effect = new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 6,
                Direction = 270,
                Color = (Color)ColorConverter.ConvertFromString("#0D0A22"),
                Opacity = 0.5
            }
        };

        AutomationProperties.SetName(
            card,
            $"{forecast.Date.ToString("dddd MMMM d", culture)}, {summary}, {forecast.TemperatureC} degrees Celsius, {forecast.TemperatureF} Fahrenheit");

        return card;
    }

    private static GlyphKind PickGlyph(WeatherForecast forecast)
    {
        switch (forecast.Summary?.Trim().ToLowerInvariant())
        {
            case "freezing" or "bracing" or "chilly":
                return GlyphKind.Snow;
            case "cool" or "mild":
                return GlyphKind.CloudSun;
            case "warm" or "balmy":
                return GlyphKind.Sun;
            case "hot" or "sweltering" or "scorching":
                return GlyphKind.HotSun;
        }

        return forecast.TemperatureC switch
        {
            < 10 => GlyphKind.Snow,
            < 20 => GlyphKind.CloudSun,
            < 30 => GlyphKind.Sun,
            _ => GlyphKind.HotSun
        };
    }

    private static Brush RampBrush(int temperatureC) => temperatureC switch
    {
        < 0 => FrozenBrush("#7FB2FF"),
        < 10 => FrozenBrush("#37C8B8"),
        < 20 => FrozenBrush("#5BD16A"),
        <= 30 => FrozenBrush("#FFB454"),
        _ => FrozenBrush("#FF7A5C")
    };

    private static Viewbox BuildGlyph(GlyphKind kind)
    {
        var canvas = new Canvas { Width = 100, Height = 100 };

        switch (kind)
        {
            case GlyphKind.Snow:
                BuildSnow(canvas);
                break;
            case GlyphKind.CloudSun:
                BuildCloudSun(canvas);
                break;
            case GlyphKind.Sun:
                BuildSun(canvas, AccentBrush, 50, 50, discRadius: 22, rayInner: 27, rayOuter: 41, rayThickness: 6, rayCount: 8);
                break;
            case GlyphKind.HotSun:
                BuildSun(canvas, CoralBrush, 50, 50, discRadius: 26, rayInner: 31, rayOuter: 47, rayThickness: 7, rayCount: 12);
                // warm inner highlight for depth
                AddDisc(canvas, 50, 50, 15, AccentBrush);
                break;
        }

        return new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
    }

    private static void BuildSun(Canvas canvas, Brush brush, double centerX, double centerY, double discRadius, double rayInner, double rayOuter, double rayThickness, int rayCount)
    {
        for (var i = 0; i < rayCount; i++)
        {
            var angle = Math.PI * 2 * i / rayCount;
            AddLine(
                canvas,
                centerX + (Math.Cos(angle) * rayInner), centerY + (Math.Sin(angle) * rayInner),
                centerX + (Math.Cos(angle) * rayOuter), centerY + (Math.Sin(angle) * rayOuter),
                rayThickness, brush);
        }

        AddDisc(canvas, centerX, centerY, discRadius, brush);
    }

    private static void BuildCloudSun(Canvas canvas)
    {
        // small sun peeking from the upper-left, behind the cloud
        BuildSun(canvas, AccentBrush, 36, 35, discRadius: 14, rayInner: 18, rayOuter: 27, rayThickness: 4.5, rayCount: 8);

        // cloud built from overlapping discs + a rounded base (reads as a cloud silhouette)
        AddRoundedRect(canvas, 28, 60, 50, 20, 10, CloudBrush);
        AddDisc(canvas, 40, 58, 13, CloudBrush);
        AddDisc(canvas, 57, 49, 17, CloudBrush);
        AddDisc(canvas, 72, 60, 12, CloudBrush);
    }

    private static void BuildSnow(Canvas canvas)
    {
        const double cx = 50;
        const double cy = 50;
        const double radius = 33;

        for (var i = 0; i < 6; i++)
        {
            var angle = Math.PI * i / 3;
            var endX = cx + (Math.Cos(angle) * radius);
            var endY = cy + (Math.Sin(angle) * radius);
            AddLine(canvas, cx, cy, endX, endY, 5, IcyBrush);

            var branchX = cx + (Math.Cos(angle) * radius * 0.58);
            var branchY = cy + (Math.Sin(angle) * radius * 0.58);
            const double tick = 11;
            AddLine(canvas, branchX, branchY, branchX + (Math.Cos(angle + 0.9) * tick), branchY + (Math.Sin(angle + 0.9) * tick), 4, IcyBrush);
            AddLine(canvas, branchX, branchY, branchX + (Math.Cos(angle - 0.9) * tick), branchY + (Math.Sin(angle - 0.9) * tick), 4, IcyBrush);
        }

        AddDisc(canvas, cx, cy, 5, IcyBrush);
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2, double thickness, Brush brush) =>
        canvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });

    private static void AddDisc(Canvas canvas, double centerX, double centerY, double radius, Brush brush)
    {
        var ellipse = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = brush };
        Canvas.SetLeft(ellipse, centerX - radius);
        Canvas.SetTop(ellipse, centerY - radius);
        canvas.Children.Add(ellipse);
    }

    private static void AddRoundedRect(Canvas canvas, double left, double top, double width, double height, double radius, Brush brush)
    {
        var rect = new Rectangle
        {
            Width = width,
            Height = height,
            RadiusX = radius,
            RadiusY = radius,
            Fill = brush
        };
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, top);
        canvas.Children.Add(rect);
    }

    private static SolidColorBrush FrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
