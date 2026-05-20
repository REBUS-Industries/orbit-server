using Eto.Forms;
using Eto.Drawing;

namespace OrbitConnector.Rhino.UI;

/// <summary>Minimal single-line text input dialog.</summary>
public class InputDialog : Dialog<DialogResult>
{
    private static readonly Color BgDark  = Color.FromArgb(36, 36, 36);
    private static readonly Color BgField = Color.FromArgb(52, 52, 52);
    private static readonly Color Accent  = Color.FromArgb(224, 98, 56);
    private static readonly Color TextPrim = Colors.White;
    private static readonly Color TextSec  = Color.FromArgb(175, 175, 175);

    private readonly TextBox _input;
    public string Value => _input.Text;

    public InputDialog(string title, string prompt)
    {
        Title           = title;
        BackgroundColor = BgDark;
        ClientSize      = new Size(320, 130);
        Resizable       = false;

        _input = new TextBox { BackgroundColor = BgField, TextColor = TextPrim };

        var ok     = new Button { Text = "OK",     BackgroundColor = Accent, TextColor = TextPrim, Width = 80 };
        var cancel = new Button { Text = "Cancel", BackgroundColor = BgField, TextColor = TextSec,  Width = 80 };

        ok.Click     += (_, _) => Close(DialogResult.Ok);
        cancel.Click += (_, _) => Close(DialogResult.Cancel);

        DefaultButton = ok;
        AbortButton   = cancel;

        var layout = new DynamicLayout { Padding = new Padding(16), DefaultSpacing = new Size(0, 8), BackgroundColor = BgDark };
        layout.Add(new Label { Text = prompt, TextColor = TextSec });
        layout.Add(_input);
        layout.BeginHorizontal();
        layout.Add(null);
        layout.Add(cancel);
        layout.Add(ok);
        layout.EndHorizontal();

        Content = layout;

        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Keys.Enter) Close(DialogResult.Ok);
        };
    }
}
