using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace FerdiumApp.Controls;

/// <summary>
/// WinUI 3 TextBox specialized for command-palette, search, and URL inputs.
/// Suppresses standard UIA Text/Value edit patterns so external desktop spell/grammar
/// overlays (such as Grammarly for Windows) do not attach to search fields.
/// </summary>
public class SearchTextBox : Wpf.Ui.Controls.TextBox
{
    public SearchTextBox()
    {
        SpellCheck.SetIsEnabled(this, false);
        AcceptsReturn = false;
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new NonProseTextBoxAutomationPeer(this);

    private sealed class NonProseTextBoxAutomationPeer : FrameworkElementAutomationPeer
    {
        public NonProseTextBoxAutomationPeer(SearchTextBox owner)
            : base(owner)
        {
        }

        protected override AutomationControlType GetAutomationControlTypeCore() =>
            AutomationControlType.Custom;

        public override object? GetPattern(PatternInterface patternInterface) => null;
    }
}
