using Eto.Forms;
using Eto.Drawing;
using Rhino;
using Orbit.Sdk.Api;
using Orbit.Sdk.Api.Models;
using OrbitConnector.Rhino.Models;

namespace OrbitConnector.Rhino.UI;

/// <summary>
/// Modal dialog for configuring a Send or Receive card.
/// Lets the user pick project, model (with optional create), layer mode, and layers.
/// </summary>
public class CardConfigDialog : Dialog<DialogResult>
{
    // ── colours (reuse from panel)
    private static readonly Color BgDark   = Color.FromArgb(36, 36, 36);
    private static readonly Color BgField  = Color.FromArgb(52, 52, 52);
    private static readonly Color Accent   = Color.FromArgb(224, 98, 56);
    private static readonly Color TextPrim = Colors.White;
    private static readonly Color TextSec  = Color.FromArgb(175, 175, 175);

    private readonly ConnectorCard _card;
    private readonly OrbitClient _client;
    private readonly bool _isNew;
    private readonly List<OrbitProject> _projects;
    private List<OrbitModel> _models = new();

    // ── controls
    private readonly DropDown _projectDrop;
    private readonly DropDown _modelDrop;
    private readonly Button _newProjectBtn;
    private readonly Button _newModelBtn;
    private readonly DropDown _layerModeDrop;
    private readonly ListBox _layerList;
    private readonly Panel _layerPanel;
    private readonly Button _okBtn;
    private readonly Button _cancelBtn;
    private readonly Label _statusLabel;

    public new ConnectorCard? Result { get; private set; }

    public CardConfigDialog(ConnectorCard card, List<OrbitProject> projects, OrbitClient client, bool isNew)
    {
        _card     = card;
        _projects = projects;
        _client   = client;
        _isNew    = isNew;

        Title           = isNew ? $"New {card.Type} Card" : $"Edit {card.Type} Card";
        BackgroundColor = BgDark;
        Resizable       = false;
        ClientSize      = new Size(400, 480);

        _projectDrop   = new DropDown { BackgroundColor = BgField, TextColor = TextPrim };
        _modelDrop     = new DropDown { BackgroundColor = BgField, TextColor = TextPrim };
        _newProjectBtn = new Button  { Text = "+ New", BackgroundColor = BgField, TextColor = Accent };
        _newModelBtn   = new Button  { Text = "+ New", BackgroundColor = BgField, TextColor = Accent };
        _layerModeDrop = new DropDown { BackgroundColor = BgField, TextColor = TextPrim };
        _layerList     = new ListBox { BackgroundColor = BgField, TextColor = TextPrim };
        _okBtn         = new Button  { Text = "Save",   BackgroundColor = Accent, TextColor = TextPrim, Width = 90 };
        _cancelBtn     = new Button  { Text = "Cancel", BackgroundColor = BgField, TextColor = TextSec,  Width = 90 };
        _statusLabel   = new Label   { Text = string.Empty, TextColor = TextSec };
        _layerPanel    = new Panel();

        // Layer mode options
        _layerModeDrop.Items.Add("All layers");
        _layerModeDrop.Items.Add("Selected layers");
        _layerModeDrop.Items.Add("Current selection");
        _layerModeDrop.SelectedIndex = card.LayerMode switch
        {
            LayerMode.ByLayer   => 1,
            LayerMode.Selection => 2,
            _                   => 0
        };

        PopulateProjects();

        // Send cards only show layer controls
        _layerPanel.Content = card.Type == CardType.Send ? BuildLayerSection() : null;
        _layerPanel.Visible = card.Type == CardType.Send;

        Content = BuildLayout();

        // ── events ────────────────────────────────────────────────────────────
        _projectDrop.SelectedIndexChanged   += OnProjectChanged;
        _layerModeDrop.SelectedIndexChanged += OnLayerModeChanged;
        _newProjectBtn.Click += OnNewProject;
        _newModelBtn.Click   += OnNewModel;

        _okBtn.Click += (_, _) =>
        {
            if (Validate())
            {
                ApplyToCard();
                Result = _card;
                Close(DialogResult.Ok);
            }
        };
        _cancelBtn.Click += (_, _) => Close(DialogResult.Cancel);

        DefaultButton = _okBtn;
        AbortButton   = _cancelBtn;

        UpdateLayerVisibility();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Layout
    // ─────────────────────────────────────────────────────────────────────────

    private Control BuildLayout()
    {
        var layout = new DynamicLayout { Padding = new Padding(16), DefaultSpacing = new Size(0, 10), BackgroundColor = BgDark };

        // Title
        layout.Add(new Label
        {
            Text = _isNew ? $"Configure {_card.Type} Card" : "Edit Card",
            Font = new Font(SystemFont.Bold, 12),
            TextColor = TextPrim
        });

        // Project row
        layout.Add(FieldLabel("Project"));
        layout.BeginHorizontal();
        layout.Add(_projectDrop, xscale: true);
        layout.Add(_newProjectBtn);
        layout.EndHorizontal();

        // Model row
        layout.Add(FieldLabel("Model / Branch"));
        layout.BeginHorizontal();
        layout.Add(_modelDrop, xscale: true);
        layout.Add(_newModelBtn);
        layout.EndHorizontal();

        // Layer section (Send only)
        layout.Add(_layerPanel);

        // Status
        layout.Add(_statusLabel);
        layout.Add(null); // spacer

        // Buttons
        layout.BeginHorizontal();
        layout.Add(null);
        layout.Add(_cancelBtn);
        layout.Add(_okBtn);
        layout.EndHorizontal();

        return layout;
    }

    private Panel BuildLayerSection()
    {
        var p = new DynamicLayout { DefaultSpacing = new Size(0, 6), BackgroundColor = BgDark };
        p.Add(FieldLabel("Layers"));
        p.Add(_layerModeDrop);
        p.Add(_layerList);
        _layerList.Height = 120;
        return p;
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        TextColor = TextSec,
        Font = new Font(SystemFont.Default, 9)
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Data
    // ─────────────────────────────────────────────────────────────────────────

    private void PopulateProjects()
    {
        _projectDrop.Items.Clear();
        foreach (var p in _projects)
            _projectDrop.Items.Add(new ListItem { Text = p.Name ?? p.Id, Key = p.Id });

        // Restore selection
        if (_card.ProjectId != null)
        {
            var idx = _projects.FindIndex(p => p.Id == _card.ProjectId);
            if (idx >= 0)
            {
                _projectDrop.SelectedIndex = idx;
                LoadModels(_card.ProjectId);
            }
        }
        else if (_projects.Count > 0)
        {
            _projectDrop.SelectedIndex = 0;
            LoadModels(_projects[0].Id!);
        }
    }

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        if (_projectDrop.SelectedIndex < 0 || _projectDrop.SelectedIndex >= _projects.Count) return;
        var pid = _projects[_projectDrop.SelectedIndex].Id!;
        Task.Run(() => LoadModelsAsync(pid));
    }

    private void LoadModels(string projectId)
    {
        try
        {
            _models = _client.GetModelsAsync(projectId).GetAwaiter().GetResult();
            PopulateModels();
        }
        catch { _statusLabel.Text = "Failed to load models."; }
    }

    private async Task LoadModelsAsync(string projectId)
    {
        try
        {
            _models = await _client.GetModelsAsync(projectId);
            Application.Instance.Invoke(PopulateModels);
        }
        catch (Exception ex)
        {
            Application.Instance.Invoke(() => _statusLabel.Text = $"Models: {ex.Message}");
        }
    }

    private void PopulateModels()
    {
        _modelDrop.Items.Clear();
        foreach (var m in _models)
            _modelDrop.Items.Add(new ListItem { Text = m.Name ?? m.Id, Key = m.Id });

        if (_card.ModelId != null)
        {
            var idx = _models.FindIndex(m => m.Id == _card.ModelId);
            if (idx >= 0) _modelDrop.SelectedIndex = idx;
        }
        else if (_models.Count > 0)
        {
            _modelDrop.SelectedIndex = 0;
        }
    }

    private void PopulateLayerList()
    {
        _layerList.Items.Clear();
        var doc = RhinoDoc.ActiveDoc;
        if (doc == null) return;
        foreach (var layer in doc.Layers)
        {
            if (!layer.IsDeleted)
                _layerList.Items.Add(new ListItem { Text = layer.FullPath, Key = layer.FullPath });
        }
    }

    private void OnLayerModeChanged(object? sender, EventArgs e) => UpdateLayerVisibility();

    private void UpdateLayerVisibility()
    {
        var showList = _layerModeDrop.SelectedIndex == 1; // "Selected layers"
        _layerList.Visible = showList;
        if (showList && _layerList.Items.Count == 0)
            PopulateLayerList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Create project/model
    // ─────────────────────────────────────────────────────────────────────────

    private void OnNewProject(object? sender, EventArgs e)
    {
        var dlg = new InputDialog("New Project", "Project name:");
        if (dlg.ShowModal(this) != DialogResult.Ok || string.IsNullOrWhiteSpace(dlg.Value)) return;
        Task.Run(async () =>
        {
            try
            {
                var project = await _client.CreateProjectAsync(dlg.Value);
                _projects.Insert(0, project);
                Application.Instance.Invoke(() =>
                {
                    PopulateProjects();
                    _projectDrop.SelectedIndex = 0;
                });
            }
            catch (Exception ex)
            {
                Application.Instance.Invoke(() =>
                    MessageBox.Show($"Failed to create project: {ex.Message}", "ORBIT", MessageBoxType.Error));
            }
        });
    }

    private void OnNewModel(object? sender, EventArgs e)
    {
        if (_projectDrop.SelectedIndex < 0) return;
        var pid = _projects[_projectDrop.SelectedIndex].Id!;

        var dlg = new InputDialog("New Model", "Model name:");
        if (dlg.ShowModal(this) != DialogResult.Ok || string.IsNullOrWhiteSpace(dlg.Value)) return;
        Task.Run(async () =>
        {
            try
            {
                var model = await _client.CreateModelAsync(pid, dlg.Value);
                _models.Insert(0, model);
                Application.Instance.Invoke(() =>
                {
                    PopulateModels();
                    _modelDrop.SelectedIndex = 0;
                });
            }
            catch (Exception ex)
            {
                Application.Instance.Invoke(() =>
                    MessageBox.Show($"Failed to create model: {ex.Message}", "ORBIT", MessageBoxType.Error));
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Validation + apply
    // ─────────────────────────────────────────────────────────────────────────

    private bool Validate()
    {
        if (_projectDrop.SelectedIndex < 0)
        { _statusLabel.Text = "Please select a project."; return false; }
        if (_modelDrop.SelectedIndex < 0)
        { _statusLabel.Text = "Please select or create a model."; return false; }
        return true;
    }

    private void ApplyToCard()
    {
        var project = _projects[_projectDrop.SelectedIndex];
        var model   = _models[_modelDrop.SelectedIndex];

        _card.ProjectId   = project.Id;
        _card.ProjectName = project.Name;
        _card.ModelId     = model.Id;
        _card.ModelName   = model.Name;
        _card.Target      = _card.Target;

        _card.LayerMode = _layerModeDrop.SelectedIndex switch
        {
            1 => LayerMode.ByLayer,
            2 => LayerMode.Selection,
            _ => LayerMode.All
        };

        if (_card.LayerMode == LayerMode.ByLayer)
        {
            _card.IncludedLayers.Clear();
            // Collect all items whose text matches a selected key
            // Eto ListBox uses SelectedKey for single selection; for multi-select
            // we use a GridView-based approach in a future version. For now, persist all.
            foreach (var item in _layerList.Items)
                _card.IncludedLayers.Add(item.Key);
        }
    }
}
