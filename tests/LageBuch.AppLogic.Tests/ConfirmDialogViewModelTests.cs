using LageBuch.AppLogic.ViewModels;

namespace LageBuch.AppLogic.Tests;

public class ConfirmDialogViewModelTests
{
    [Fact]
    public void A_plain_confirm_offers_no_option()
    {
        var dialog = new ConfirmDialogViewModel("Titel", "Text", "OK", () => { });

        Assert.False(dialog.HasOption);
        Assert.Null(dialog.OptionLabel);
    }

    [Fact]
    public void The_ticked_option_is_readable_when_the_confirm_callback_runs()
    {
        bool? seen = null;
        ConfirmDialogViewModel? dialog = null;
        dialog = new ConfirmDialogViewModel("Titel", "Text", "OK", () => seen = dialog?.IsOptionChecked, "Option");

        Assert.True(dialog.HasOption);
        Assert.Equal("Option", dialog.OptionLabel);
        Assert.False(dialog.IsOptionChecked); // opt-in: never ticked by default
        dialog.IsOptionChecked = true;
        dialog.ConfirmCommand.Execute(null);

        Assert.True(seen);
    }
}
