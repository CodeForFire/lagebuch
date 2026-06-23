using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Feuerwehr.App;

public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
            return new TextBlock { Text = "—" };

        var shortName = data.GetType().Name.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType($"Feuerwehr.App.Views.{shortName}, Feuerwehr.App");

        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "Nicht gefunden: " + shortName };
    }

    public bool Match(object? data) => data is CommunityToolkit.Mvvm.ComponentModel.ObservableObject;
}
