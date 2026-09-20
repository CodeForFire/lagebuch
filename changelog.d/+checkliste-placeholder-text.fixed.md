The Checkliste name field used Avalonia's deprecated `TextBox.Watermark` instead of
`PlaceholderText`. No visible change; the build now fails on an obsolete API in XAML the way it
already did in C#.
