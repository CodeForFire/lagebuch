using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace LageBuch.App.Shared;

public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
        {
            return new TextBlock { Text = "—" };
        }

        var shortName = data.GetType().Name.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType($"LageBuch.App.Shared.Views.{shortName}, LageBuch.App.Shared");

        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "Nicht gefunden: " + shortName };
    }

    public bool Match(object? data) => data is CommunityToolkit.Mvvm.ComponentModel.ObservableObject;
}
