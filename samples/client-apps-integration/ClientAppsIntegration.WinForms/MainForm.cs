using System.Diagnostics;

namespace ClientAppsIntegration.WinForms;

public partial class MainForm : Form
{
    private const int CardHeight = 320;
    private const int MarkSize = 54;

    private readonly ActivitySource _activitySource = new(Program.HostEnvironment?.ApplicationName ?? "");
    private readonly ILogger _logger;
    private readonly WeatherApiClient _weatherApiClient;
    private readonly CancellationTokenSource _closingCts = new();

    private readonly ForecastCard[] _forecastCards = new ForecastCard[5];

    private GradientPanel _root = null!;
    private CardPanel _header = null!;
    private Label _wordmark = null!;
    private Label _subtitle = null!;
    private CardPanel _controlBar = null!;
    private PrimaryButton _btnLoad = null!;
    private CheckBox _chkForceError = null!;
    private Label _status = null!;
    private ProgressBar _progress = null!;
    private GradientPanel _stripHost = null!;
    private GradientTableLayoutPanel _cardsTable = null!;
    private CardPanel _stateCard = null!;
    private Label _stateTitle = null!;
    private Label _stateBody = null!;
    private CardPanel _footer = null!;
    private Label _footerLabel = null!;

    private StateKind _stateKind = StateKind.Empty;

    private enum StateKind
    {
        Empty,
        Loading,
        Error,
    }

    public MainForm(ILogger<MainForm> logger, WeatherApiClient weatherApiClient)
    {
        _logger = logger;
        _weatherApiClient = weatherApiClient;

        InitializeComponent();
        BuildLayout();
        ShowEmptyState();
    }

    private void BuildLayout()
    {
        SuspendLayout();
        DoubleBuffered = true;
        BackColor = SkylineTheme.SkyTop;
        Font = SkylineTheme.StatusFont;

        _root = new GradientPanel { Dock = DockStyle.Fill, Padding = new Padding(26) };

        BuildHeader();
        BuildControlBar();
        BuildStrip();
        BuildFooter();

        // Add the fill control first, then edge controls, so docking resolves header/control on
        // top, footer on the bottom and the strip filling the remaining space.
        _root.Controls.Add(_stripHost);
        _root.Controls.Add(_footer);
        _root.Controls.Add(_controlBar);
        _root.Controls.Add(_header);

        Controls.Add(_root);

        ResumeLayout(true);
    }

    private void BuildHeader()
    {
        _header = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 104,
            Margin = new Padding(0, 0, 0, 14),
            ContentPadding = 18,
        };
        _header.ContentPaint += DrawHeaderMark;
        _header.Resize += (_, _) => LayoutHeader();

        _wordmark = new Label
        {
            AutoSize = true,
            BackColor = SkylineTheme.Surface,
            Font = SkylineTheme.WordmarkFont,
            ForeColor = SkylineTheme.TextPrimary,
            Text = "Skyline",
            UseMnemonic = false,
        };
        _subtitle = new Label
        {
            AutoSize = true,
            BackColor = SkylineTheme.Surface,
            Font = SkylineTheme.SubtitleFont,
            ForeColor = SkylineTheme.TextSecondary,
            Text = "Skyline Dawn · Aspire WinForms client",
            UseMnemonic = false,
        };

        _header.Controls.Add(_wordmark);
        _header.Controls.Add(_subtitle);
    }

    private void BuildControlBar()
    {
        _controlBar = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 88,
            Margin = new Padding(0, 0, 0, 14),
            ContentPadding = 16,
        };
        _controlBar.Resize += (_, _) => LayoutControlBar();

        _btnLoad = new PrimaryButton
        {
            Text = "Load forecast",
            TabIndex = 0,
            AccessibleName = "Load forecast",
            AccessibleDescription = "Calls the weather API service and displays a five day forecast.",
        };
        _btnLoad.Click += btnLoadWeather_Click;

        _chkForceError = new CheckBox
        {
            AutoSize = true,
            BackColor = SkylineTheme.Surface,
            Font = SkylineTheme.ToggleFont,
            ForeColor = SkylineTheme.TextPrimary,
            Text = "Force error",
            TabIndex = 1,
            FlatStyle = FlatStyle.Standard,
            AccessibleName = "Force error",
            AccessibleDescription = "When checked, simulates a failure to demonstrate error handling and telemetry.",
        };

        _status = new Label
        {
            AutoSize = false,
            BackColor = SkylineTheme.Surface,
            Font = SkylineTheme.StatusFont,
            ForeColor = SkylineTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false,
            AccessibleRole = AccessibleRole.StaticText,
        };

        _progress = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 28,
            Visible = false,
            AccessibleName = "Loading forecast",
        };

        _controlBar.Controls.Add(_btnLoad);
        _controlBar.Controls.Add(_chkForceError);
        _controlBar.Controls.Add(_status);
        _controlBar.Controls.Add(_progress);
    }

    private void BuildStrip()
    {
        _stripHost = new GradientPanel { Dock = DockStyle.Fill };
        _stripHost.Resize += (_, _) => LayoutStrip();

        _cardsTable = new GradientTableLayoutPanel
        {
            ColumnCount = 5,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        _cardsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        for (var i = 0; i < _forecastCards.Length; i++)
        {
            _cardsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
            var card = new ForecastCard { Dock = DockStyle.Fill };
            _forecastCards[i] = card;
            _cardsTable.Controls.Add(card, i, 0);
        }

        _stateCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            ContentPadding = 28,
            Visible = false,
            AccessibleRole = AccessibleRole.Grouping,
        };
        _stateCard.ContentPaint += DrawStateGlyph;
        _stateCard.Resize += (_, _) => LayoutStateCard();

        _stateTitle = new Label
        {
            AutoSize = false,
            BackColor = SkylineTheme.Surface,
            Font = SkylineTheme.StateTitleFont,
            ForeColor = SkylineTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleCenter,
            UseMnemonic = false,
        };
        _stateBody = new Label
        {
            AutoSize = false,
            BackColor = SkylineTheme.Surface,
            Font = SkylineTheme.StateBodyFont,
            ForeColor = SkylineTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleCenter,
            UseMnemonic = false,
        };

        _stateCard.Controls.Add(_stateTitle);
        _stateCard.Controls.Add(_stateBody);

        _stripHost.Controls.Add(_cardsTable);
        _stripHost.Controls.Add(_stateCard);
    }

    private void BuildFooter()
    {
        _footer = new CardPanel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            Margin = new Padding(0, 14, 0, 0),
            ShadowSize = 10,
            ContentPadding = 8,
        };
        _footer.Resize += (_, _) => LayoutFooter();
        _footerLabel = new Label
        {
            AutoSize = false,
            BackColor = SkylineTheme.Surface,
            Font = SkylineTheme.FooterFont,
            ForeColor = SkylineTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "Traces, logs, and metrics stream to the Aspire dashboard via OpenTelemetry.",
            UseMnemonic = false,
        };
        _footer.Controls.Add(_footerLabel);
    }

    private void LayoutFooter()
    {
        var body = Rectangle.Round(_footer.BodyBounds);
        if (body.Width <= 0)
        {
            return;
        }

        // Inset horizontally past the corner radius so the card's rounded corners and
        // painted shadow stay visible around the centered footer text (rather than a
        // full-bleed label squaring off the card).
        var inset = _footer.CornerRadius;
        _footerLabel.SetBounds(body.Left + inset, body.Top, Math.Max(0, body.Width - inset * 2), body.Height);
    }

    private void LayoutHeader()
    {
        var c = _header.ContentRectangle;
        if (c.Width <= 0)
        {
            return;
        }

        // Center the mark and text stack within the white card body so the subtitle
        // never spills below the card onto the gradient backdrop.
        var body = Rectangle.Round(_header.BodyBounds);
        var markSize = Math.Min(MarkSize, body.Height);
        var textX = c.Left + markSize + 18;

        var wordSize = _wordmark.PreferredSize;
        var subSize = _subtitle.PreferredSize;
        var totalH = wordSize.Height + subSize.Height + 2;
        var startY = body.Top + Math.Max(0, (body.Height - totalH) / 2);

        _wordmark.Location = new Point(textX, startY);
        _subtitle.Location = new Point(textX, startY + wordSize.Height + 2);
    }

    private void DrawHeaderMark(Graphics g)
    {
        var c = _header.ContentRectangle;
        var body = _header.BodyBounds;
        var markSize = Math.Min(MarkSize, (int)body.Height);
        var r = new RectangleF(c.Left, body.Top + (body.Height - markSize) / 2f, markSize, markSize);
        SkylineTheme.DrawSkylineMark(g, r, SkylineTheme.AccentCoral, SkylineTheme.AccentAmber);
    }

    private void LayoutControlBar()
    {
        var c = _controlBar.ContentRectangle;
        if (c.Width <= 0)
        {
            return;
        }

        // Vertically center the controls within the white card body rather than the
        // padded content box. The primary button is taller than the padded area, so
        // centering it on the body keeps it from spilling past the card's rounded
        // lower edge onto the gradient backdrop.
        var body = Rectangle.Round(_controlBar.BodyBounds);
        int CenterY(int height) => body.Top + Math.Max(0, (body.Height - height) / 2);

        const int btnW = 156;
        const int btnH = 44;
        _btnLoad.SetBounds(c.Left, CenterY(btnH), btnW, btnH);

        var chkX = _btnLoad.Right + 26;
        _chkForceError.Location = new Point(chkX, CenterY(_chkForceError.Height));

        var statusX = _chkForceError.Right + 30;
        _status.SetBounds(statusX, body.Top, Math.Max(60, c.Right - statusX), body.Height);

        _progress.SetBounds(c.Left, body.Bottom - 8, c.Width, 5);
    }

    private void LayoutStrip()
    {
        if (_stripHost.Width <= 0)
        {
            return;
        }

        var top = Math.Max(0, (_stripHost.Height - CardHeight) / 2);
        _cardsTable.SetBounds(0, top, _stripHost.Width, Math.Min(CardHeight, _stripHost.Height));
        LayoutStateCard();
    }

    private void LayoutStateCard()
    {
        var c = _stateCard.ContentRectangle;
        if (c.Width <= 0)
        {
            return;
        }

        const int glyphSize = 66;
        const int titleH = 32;
        const int bodyH = 48;
        const int gap = 12;
        var totalH = glyphSize + gap + titleH + 4 + bodyH;
        var startY = c.Top + Math.Max(0, (c.Height - totalH) / 2);

        var width = Math.Min(c.Width, 560);
        var x = c.Left + (c.Width - width) / 2;

        _stateTitle.SetBounds(x, startY + glyphSize + gap, width, titleH);
        _stateBody.SetBounds(x, startY + glyphSize + gap + titleH + 4, width, bodyH);
        _stateCard.Invalidate();
    }

    private void DrawStateGlyph(Graphics g)
    {
        var c = _stateCard.ContentRectangle;
        if (c.Width <= 0)
        {
            return;
        }

        const int glyphSize = 66;
        const int titleH = 32;
        const int bodyH = 48;
        const int gap = 12;
        var totalH = glyphSize + gap + titleH + 4 + bodyH;
        var startY = c.Top + Math.Max(0, (c.Height - totalH) / 2);
        var r = new RectangleF(c.Left + (c.Width - glyphSize) / 2f, startY, glyphSize, glyphSize);

        switch (_stateKind)
        {
            case StateKind.Error:
                SkylineTheme.DrawWarningGlyph(g, r, SkylineTheme.ErrorAccent, SkylineTheme.Surface);
                break;
            case StateKind.Loading:
                SkylineTheme.DrawWeatherGlyph(g, r, SkylineTheme.WeatherGlyph.Sun);
                break;
            default:
                SkylineTheme.DrawSkylineMark(g, r, SkylineTheme.AccentCoral, SkylineTheme.AccentAmber);
                break;
        }
    }

    // ---- Public surface so a screenshot/test harness produces the exact runtime UI ----

    public void SetLoading(bool loading)
    {
        _btnLoad.Enabled = !loading;
        _progress.Visible = loading;

        if (loading)
        {
            SetStatus("Loading forecast from apiservice…");
        }
    }

    public void RenderForecasts(IReadOnlyList<WeatherForecast> forecasts)
    {
        var count = Math.Min(forecasts.Count, _forecastCards.Length);
        for (var i = 0; i < _forecastCards.Length; i++)
        {
            if (i < count)
            {
                _forecastCards[i].SetForecast(forecasts[i]);
                _forecastCards[i].Visible = true;
            }
            else
            {
                _forecastCards[i].Visible = false;
            }
        }

        _stateCard.Visible = false;
        _cardsTable.Visible = true;
        SetStatus($"Loaded {count} forecast{(count == 1 ? "" : "s")} from apiservice");
    }

    public void ShowEmptyState()
    {
        ShowState(StateKind.Empty, "No forecast yet", "Select Load forecast to call the weather API.");
        SetStatus("Ready");
    }

    public void ShowError(string message)
    {
        ShowState(StateKind.Error, "Couldn't load the forecast", message);
        SetStatus(message, error: true);
    }

    private void ShowState(StateKind kind, string title, string body)
    {
        _stateKind = kind;
        var tinted = kind == StateKind.Error;
        var surface = tinted ? SkylineTheme.ErrorTint : SkylineTheme.Surface;

        _stateCard.Tinted = tinted;
        _stateTitle.Text = title;
        _stateTitle.ForeColor = tinted ? SkylineTheme.ErrorHeading : SkylineTheme.TextPrimary;
        _stateTitle.BackColor = surface;
        _stateBody.Text = body;
        _stateBody.BackColor = surface;
        _stateBody.ForeColor = tinted ? SkylineTheme.TextPrimary : SkylineTheme.TextSecondary;
        _stateCard.AccessibleName = $"{title}. {body}";

        _cardsTable.Visible = false;
        _stateCard.Visible = true;

        LayoutStateCard();
        _stateCard.Invalidate();
    }

    private void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? SkylineTheme.ErrorHeading : SkylineTheme.TextPrimary;
        _status.AccessibleName = $"Status: {text}";
    }

    private async void btnLoadWeather_Click(object? sender, EventArgs e)
    {
        using var activity = _activitySource.StartActivity("Load Weather", ActivityKind.Client);

        SetLoading(true);

        try
        {
            if (_chkForceError.Checked)
            {
                throw new InvalidOperationException("Forced error!");
            }

            var weather = await _weatherApiClient.GetWeatherAsync(_closingCts.Token);
            RenderForecasts(weather);
        }
        catch (TaskCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Operation was canceled");
            return;
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Error loading weather");

            ShowError(ex.Message);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _closingCts.Cancel();
    }
}
