using Eto.Forms;
using Eto.Drawing;
using Rhino;
using Rhino.UI;
using Orbit.Sdk.Api;
using Orbit.Sdk.Api.Models;
using Orbit.Sdk.Transport;
using OrbitConnector.Rhino.Auth;
using OrbitConnector.Rhino.Models;
using OrbitConnector.Rhino.Pipeline;

namespace OrbitConnector.Rhino.UI;

/// <summary>
/// Main ORBIT dockable panel.
/// Sections: auth bar → card list → add-card buttons.
/// </summary>
[System.Runtime.InteropServices.Guid("21621060-21E4-4A81-8FF6-3E11FBEF5D04")]
public class OrbitEtoPanel : Panel, IPanel
{
    // ── colours ──────────────────────────────────────────────────────────────
    private static readonly Color BgDark     = Color.FromArgb(28, 28, 28);
    private static readonly Color BgCard     = Color.FromArgb(44, 44, 44);
    private static readonly Color BgCardHov  = Color.FromArgb(52, 52, 52);
    private static readonly Color Accent     = Color.FromArgb(224, 98, 56);   // #E06238
    private static readonly Color AccentHov  = Color.FromArgb(200, 74, 33);
    private static readonly Color TextPrim   = Colors.White;
    private static readonly Color TextSec    = Color.FromArgb(175, 175, 175);
    private static readonly Color TextMuted  = Color.FromArgb(95, 95, 95);
    private static readonly Color Danger     = Color.FromArgb(220, 55, 55);

    public static System.Guid PanelId => typeof(OrbitEtoPanel).GUID;

    // ── state ─────────────────────────────────────────────────────────────────
    private readonly uint _docSerial;
    private readonly ServerConfig _config;
    private readonly OrbitTokenStore _tokenStore;
    private readonly OrbitAuthManager _authManager;
    private readonly RhinoSendPipeline _pipeline = new();

    private OrbitClient? _client;
    private string? _currentUserName;
    private ServerTarget _activeTarget = ServerTarget.Prod;
    private CancellationTokenSource? _sendCts;

    // ── layout controls ───────────────────────────────────────────────────────
    private readonly DynamicLayout _root;
    private readonly Panel _authBar;
    private readonly Scrollable _cardScroller;
    private readonly DynamicLayout _cardList;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;

    // ── auth bar controls ─────────────────────────────────────────────────────
    private readonly Label _userLabel;
    private readonly Button _loginButton;
    private readonly Button _logoutButton;
    private readonly DropDown _targetDrop;

    public OrbitEtoPanel(uint documentSerialNumber)
    {
        _docSerial  = documentSerialNumber;
        _config     = ServerConfig.Default;
        _tokenStore = new OrbitTokenStore(OrbitConnectorPlugin.Instance!);
        _authManager = new OrbitAuthManager(_config);

        _activeTarget = _tokenStore.LastTarget;

        _root       = new DynamicLayout { Padding = new Padding(0), DefaultSpacing = Size.Empty };
        _cardList   = new DynamicLayout { Padding = new Padding(8), DefaultSpacing = new Size(0, 6) };
        _statusLabel = new Label { Text = string.Empty, TextColor = TextSec, Visible = false };
        _progressBar = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 0, Visible = false };

        // ── auth bar ─────────────────────────────────────────────────────────
        _userLabel    = new Label { Text = "Not logged in", TextColor = TextSec, VerticalAlignment = VerticalAlignment.Center };
        _loginButton  = new Button { Text = "Login",  BackgroundColor = Accent, TextColor = TextPrim };
        _logoutButton = new Button { Text = "Logout", BackgroundColor = BgCard, TextColor = TextSec, Visible = false };
        _targetDrop   = new DropDown();
        _targetDrop.Items.Add("Production");
        _targetDrop.Items.Add("Dev");
        _targetDrop.SelectedIndex = _activeTarget == ServerTarget.Prod ? 0 : 1;
        _targetDrop.BackgroundColor = BgCard;
        _targetDrop.TextColor = TextPrim;

        _authBar = BuildAuthBar();

        // ── card scroller ─────────────────────────────────────────────────────
        _cardScroller = new Scrollable
        {
            Content = _cardList,
            BackgroundColor = BgDark,
            Border = BorderType.None,
            ExpandContentHeight = false
        };

        // ── add buttons ───────────────────────────────────────────────────────
        var addSend    = MakeAccentButton("+ Add Send Card");
        var addReceive = MakeAccentButton("+ Add Receive Card", outline: true);

        addSend.Click    += (_, _) => Task.Run(() => OpenCardConfig(CardType.Send));
        addReceive.Click += (_, _) => Task.Run(() => OpenCardConfig(CardType.Receive));

        // ── assemble root ─────────────────────────────────────────────────────
        _root.BeginVertical(new Padding(0), new Size(0, 0));
        _root.Add(_authBar);
        _root.Add(new Panel { Height = 1, BackgroundColor = Color.FromArgb(52, 52, 52) });
        _root.Add(_cardScroller, yscale: true);
        _root.Add(new Panel { Height = 1, BackgroundColor = Color.FromArgb(52, 52, 52) });
        _root.BeginVertical(new Padding(8), new Size(0, 4));
        _root.Add(_statusLabel);
        _root.Add(_progressBar);
        _root.Add(addSend);
        _root.Add(addReceive);
        _root.EndVertical();
        _root.EndVertical();

        BackgroundColor = BgDark;
        Content = _root;

        // ── wire events ───────────────────────────────────────────────────────
        _loginButton.Click  += (_, _) => Task.Run(() => DoLogin());
        _logoutButton.Click += (_, _) => DoLogout();
        _targetDrop.SelectedIndexChanged += OnTargetChanged;

        // Try to restore session from saved token
        Task.Run(() => TryRestoreSession());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Auth bar
    // ─────────────────────────────────────────────────────────────────────────

    private Panel BuildAuthBar()
    {
        var bar = new DynamicLayout
        {
            Padding = new Padding(8, 6),
            DefaultSpacing = new Size(6, 0),
            BackgroundColor = Color.FromArgb(36, 36, 36)
        };
        bar.BeginHorizontal();
        bar.Add(new Label { Text = "ORBIT", Font = new Font(SystemFont.Bold, 11), TextColor = Accent, VerticalAlignment = VerticalAlignment.Center });
        bar.Add(null); // spacer
        bar.Add(_targetDrop);
        bar.Add(_userLabel);
        bar.Add(_loginButton);
        bar.Add(_logoutButton);
        bar.EndHorizontal();
        return bar;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Auth logic
    // ─────────────────────────────────────────────────────────────────────────

    private async Task TryRestoreSession()
    {
        var serverUrl = _config.GetUrl(_activeTarget);
        var token = _tokenStore.GetToken(serverUrl);
        if (token == null) return;

        try
        {
            var client = new OrbitClient(serverUrl, token);
            var user = await client.GetActiveUserAsync();
            if (user == null) return;
            _client = client;
            _currentUserName = user.Name ?? user.Email ?? "User";
            Application.Instance.Invoke(() => SetLoggedIn(_currentUserName));
        }
        catch { /* token expired or server unreachable — stay logged out */ }
    }

    private async Task DoLogin()
    {
        Application.Instance.Invoke(() =>
        {
            _loginButton.Enabled = false;
            _loginButton.Text = "Opening browser…";
        });
        try
        {
            var serverUrl = _config.GetUrl(_activeTarget);
            var token = await _authManager.AuthenticateAsync(_activeTarget);
            _tokenStore.SaveToken(serverUrl, token);

            var client = new OrbitClient(serverUrl, token);
            var user = await client.GetActiveUserAsync()
                       ?? throw new Exception("No active user returned after auth");

            _client = client;
            _currentUserName = user.Name ?? user.Email ?? "User";
            Application.Instance.Invoke(() => SetLoggedIn(_currentUserName));
        }
        catch (Exception ex)
        {
            Application.Instance.Invoke(() =>
            {
                MessageBox.Show($"Login failed: {ex.Message}", "ORBIT Login", MessageBoxType.Error);
                _loginButton.Enabled = true;
                _loginButton.Text = "Login";
            });
        }
    }

    private void DoLogout()
    {
        var serverUrl = _config.GetUrl(_activeTarget);
        _tokenStore.ClearToken(serverUrl);
        _client = null;
        _currentUserName = null;
        SetLoggedOut();
    }

    private void SetLoggedIn(string name)
    {
        _userLabel.Text     = name;
        _userLabel.TextColor = TextPrim;
        _loginButton.Visible  = false;
        _logoutButton.Visible = true;
        _loginButton.Text     = "Login";
        _loginButton.Enabled  = true;
        RefreshCardList();
    }

    private void SetLoggedOut()
    {
        _userLabel.Text      = "Not logged in";
        _userLabel.TextColor = TextSec;
        _loginButton.Visible  = true;
        _logoutButton.Visible = false;
        RefreshCardList();
    }

    private void OnTargetChanged(object? sender, EventArgs e)
    {
        _activeTarget = _targetDrop.SelectedIndex == 0 ? ServerTarget.Prod : ServerTarget.Dev;
        _tokenStore.LastTarget = _activeTarget;
        _client = null;
        _currentUserName = null;
        SetLoggedOut();
        Task.Run(() => TryRestoreSession());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Card list
    // ─────────────────────────────────────────────────────────────────────────

    private void RefreshCardList()
    {
        Application.Instance.Invoke(() =>
        {
            _cardList.Clear();
            var store = CardStore.Instance;
            if (store == null || store.Cards.Count == 0)
            {
                _cardList.BeginVertical(new Padding(0, 16), Size.Empty);
                _cardList.Add(new Label
                {
                    Text = "No cards yet.\nAdd a Send or Receive card below.",
                    TextColor = TextMuted,
                    TextAlignment = TextAlignment.Center,
                    Wrap = WrapMode.Word
                });
                _cardList.EndVertical();
            }
            else
            {
                foreach (var card in store.Cards)
                    _cardList.Add(BuildCardRow(card));
            }
        });
    }

    private Panel BuildCardRow(ConnectorCard card)
    {
        var isSend     = card.Type == CardType.Send;
        var typeColor  = isSend ? Accent : Color.FromArgb(0, 122, 255);
        var typeLabel  = new Label
        {
            Text = isSend ? "▲ SEND" : "▼ RECEIVE",
            TextColor = typeColor,
            Font = new Font(SystemFont.Bold, 9)
        };

        var projectLabel = new Label
        {
            Text = card.ProjectName != null
                ? $"{card.ProjectName} / {card.ModelName ?? "—"}"
                : "Click ⚙ to configure",
            TextColor = card.ProjectName != null ? TextPrim : TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };

        var lastLabel = new Label
        {
            Text = isSend
                ? (card.LastSentAt.HasValue ? $"Sent {Ago(card.LastSentAt.Value)}" : "Never sent")
                : (card.LastReceivedAt.HasValue ? $"Received {Ago(card.LastReceivedAt.Value)}" : "Never received"),
            TextColor = TextMuted,
            Font = new Font(SystemFont.Default, 8)
        };

        var actionButton = new Button
        {
            Text = isSend ? "Send" : "Receive",
            BackgroundColor = isSend ? Accent : Color.FromArgb(0, 122, 255),
            TextColor = TextPrim,
            Enabled = _client != null && card.ProjectId != null
        };
        actionButton.Click += (_, _) =>
        {
            if (isSend) Task.Run(() => DoSend(card));
            else        MessageBox.Show("Receive coming soon.", "ORBIT");
        };

        var configButton = new Button { Text = "⚙", Width = 28, BackgroundColor = BgCard, TextColor = TextSec };
        configButton.Click += (_, _) => Task.Run(() => OpenCardConfig(card));

        var deleteButton = new Button { Text = "✕", Width = 28, BackgroundColor = BgCard, TextColor = Danger };
        deleteButton.Click += (_, _) =>
        {
            var confirm = MessageBox.Show(
                "Remove this card?", "ORBIT", MessageBoxButtons.YesNo);
            if (confirm == DialogResult.Yes)
            {
                CardStore.Instance?.RemoveCard(card.Id);
                RefreshCardList();
            }
        };

        var row = new DynamicLayout
        {
            BackgroundColor = BgCard,
            Padding = new Padding(10, 8)
        };
        row.BeginVertical(new Padding(0), new Size(0, 3));
        row.BeginHorizontal();
        row.Add(typeLabel);
        row.Add(null);
        row.Add(configButton);
        row.Add(deleteButton);
        row.EndHorizontal();
        row.Add(projectLabel);
        row.Add(lastLabel);
        row.BeginHorizontal();
        row.Add(null);
        row.Add(actionButton);
        row.EndHorizontal();
        row.EndVertical();

        return row;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Card config dialog
    // ─────────────────────────────────────────────────────────────────────────

    private void OpenCardConfig(CardType type)
    {
        var card = new ConnectorCard { Type = type, Target = _activeTarget };
        OpenCardConfig(card, isNew: true);
    }

    private void OpenCardConfig(ConnectorCard existingCard)
        => OpenCardConfig(existingCard, isNew: false);

    private void OpenCardConfig(ConnectorCard card, bool isNew)
    {
        if (_client == null)
        {
            Application.Instance.Invoke(() =>
                MessageBox.Show("Please log in first.", "ORBIT"));
            return;
        }

        // Load projects synchronously for the dialog (blocks briefly)
        List<Orbit.Sdk.Api.Models.OrbitProject> projects;
        try { projects = _client.GetProjectsAsync().GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            Application.Instance.Invoke(() =>
                MessageBox.Show($"Failed to load projects: {ex.Message}", "ORBIT", MessageBoxType.Error));
            return;
        }

        Application.Instance.Invoke(() =>
        {
            var dlg = new CardConfigDialog(card, projects, _client, isNew);
            if (dlg.ShowModal(Application.Instance.MainForm) == DialogResult.Ok)
            {
                if (isNew)
                    CardStore.Instance?.AddCard(dlg.Result!);
                else
                    CardStore.Instance?.UpdateCard(dlg.Result!);
                RefreshCardList();
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Send pipeline
    // ─────────────────────────────────────────────────────────────────────────

    private async Task DoSend(ConnectorCard card)
    {
        if (_client == null) { ShowStatus("Not logged in."); return; }

        var doc = RhinoDoc.FromRuntimeSerialNumber(_docSerial);
        if (doc == null) { ShowStatus("No active document."); return; }

        _sendCts = new CancellationTokenSource();
        var ct   = _sendCts.Token;

        try
        {
            Application.Instance.Invoke(() =>
            {
                _progressBar.Visible = true;
                _statusLabel.Visible = true;
            });

            var serverUrl = _config.GetUrl(card.Target);
            var token     = _tokenStore.GetToken(serverUrl)
                            ?? throw new InvalidOperationException("No token for server.");

            using var transport = new ServerTransport(serverUrl, card.ProjectId!, token);

            var versionId = await _pipeline.SendAsync(
                card, doc, transport, _client,
                progress: new Progress<(string status, int percent)>(p =>
                {
                    Application.Instance.Invoke(() =>
                    {
                        _statusLabel.Text    = p.status;
                        _progressBar.Value   = p.percent;
                    });
                }),
                ct: ct);

            card.LastVersionId = versionId;
            card.LastSentAt    = DateTime.UtcNow;
            CardStore.Instance?.UpdateCard(card);

            var url = $"{serverUrl}/projects/{card.ProjectId}/models/{card.ModelId}@{versionId}";
            Application.Instance.Invoke(() =>
            {
                _statusLabel.Text = $"✓ Sent! Version {versionId[..8]}…";
                _progressBar.Visible = false;
                RefreshCardList();
                // Offer to open in browser
                if (MessageBox.Show($"Version created!\n\nOpen in browser?", "ORBIT",
                        MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        { FileName = url, UseShellExecute = true });
                }
            });
        }
        catch (OperationCanceledException)
        {
            Application.Instance.Invoke(() => { _statusLabel.Text = "Cancelled."; _progressBar.Visible = false; });
        }
        catch (Exception ex)
        {
            Application.Instance.Invoke(() =>
            {
                _progressBar.Visible = false;
                MessageBox.Show($"Send failed: {ex.Message}", "ORBIT", MessageBoxType.Error);
                _statusLabel.Text = "Send failed.";
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void ShowStatus(string msg) =>
        Application.Instance.Invoke(() => { _statusLabel.Text = msg; _statusLabel.Visible = true; });

    private static string Ago(DateTime dt)
    {
        var d = DateTime.UtcNow - dt;
        if (d.TotalMinutes < 2)  return "just now";
        if (d.TotalHours   < 1)  return $"{(int)d.TotalMinutes}m ago";
        if (d.TotalDays    < 1)  return $"{(int)d.TotalHours}h ago";
        if (d.TotalDays    < 30) return $"{(int)d.TotalDays}d ago";
        return dt.ToString("dd MMM");
    }

    private static Button MakeAccentButton(string text, bool outline = false)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = outline ? BgCard : Accent,
            TextColor = outline ? Accent : TextPrim
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IPanel
    // ─────────────────────────────────────────────────────────────────────────

    void IPanel.PanelShown(uint documentSerialNumber, ShowPanelReason reason)
    {
        var doc = RhinoDoc.FromRuntimeSerialNumber(documentSerialNumber);
        if (doc != null) CardStore.LoadFromDocument(doc);
        RefreshCardList();
    }

    void IPanel.PanelHidden(uint documentSerialNumber, ShowPanelReason reason) { }
    void IPanel.PanelClosing(uint documentSerialNumber, bool onCloseDocument)
    {
        _sendCts?.Cancel();
    }
}
