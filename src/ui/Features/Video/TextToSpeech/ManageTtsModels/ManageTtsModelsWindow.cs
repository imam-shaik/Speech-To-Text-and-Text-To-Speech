using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Controls.Templates;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using System;
using System.Globalization;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeech.ManageTtsModels;

public class ManageTtsModelsWindow : Window
{
    private readonly ManageTtsModelsViewModel _vm;

    public ManageTtsModelsWindow(ManageTtsModelsViewModel vm)
    {
        UiUtil.InitializeWindow(this, GetType().Name);
        Title = "TTS - Manage Models";
        Width = 650;
        Height = 500;
        CanResize = true;
        MinWidth = 500;
        MinHeight = 400;

        _vm = vm;
        vm.Window = this;
        DataContext = vm;

        var titleLabel = new TextBlock
        {
            Text = "Text-to-Speech Models",
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 15),
        };

        var subtitleLabel = new TextBlock
        {
            Text = "View and manage downloaded TTS models. Installed models are shown with their size.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 15),
            Foreground = Avalonia.Media.Brushes.Gray,
        };

        var listView = new ListBox
        {
            Height = 250,
            [!ListBox.ItemsSourceProperty] = new Binding(nameof(vm.Models)),
        };
        listView.Bind(ListBox.SelectedItemProperty, new Binding(nameof(vm.SelectedModel)) { Mode = BindingMode.TwoWay });

        listView.ItemTemplate = new FuncDataTemplate<TtsModelItem>((item, namescope) =>
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(100) },
                    new ColumnDefinition { Width = new GridLength(100) },
                },
                Margin = new Thickness(10, 5),
            };

            var nameBlock = new TextBlock
            {
                [!TextBlock.TextProperty] = new Binding("Name"),
                FontWeight = Avalonia.Media.FontWeight.Bold,
            };

            var engineBlock = new TextBlock
            {
                [!TextBlock.TextProperty] = new Binding("Engine"),
            };

            var installedIndicator = new TextBlock
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            };
            installedIndicator.Bind(TextBlock.TextProperty, new Binding("IsInstalled") { Converter = new IsInstalledConverter() });
            installedIndicator.Bind(TextBlock.ForegroundProperty, new Binding("IsInstalled") { Converter = new IsInstalledColorConverter() });

            Grid.SetColumn(nameBlock, 0);
            Grid.SetColumn(engineBlock, 1);
            Grid.SetColumn(installedIndicator, 2);

            grid.Children.Add(nameBlock);
            grid.Children.Add(engineBlock);
            grid.Children.Add(installedIndicator);

            var border = new Border
            {
                Child = grid,
                BorderThickness = new Avalonia.Thickness(1),
                BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#333")),
                Margin = new Thickness(0, 2),
                Padding = new Thickness(5),
            };

            return border;
        });

        var infoPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 10, 0, 10),
        };

        if (listView.SelectedItem is TtsModelItem itm)
        {
            infoPanel.Children.Add(new TextBlock { Text = $"Selected: {itm.Name}" });
        }

        var deleteButton = UiUtil.MakeButton("Delete Selected", vm.DeleteSelectedModelCommand);
        deleteButton[!Button.IsEnabledProperty] = new Binding(nameof(vm.CanDelete));

        var deleteAllButton = UiUtil.MakeButton("Delete All Models", vm.DeleteAllModelsCommand);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Thickness(0, 15, 0, 0),
        };

        var okButton = UiUtil.MakeButtonOk(vm.OkCommand);
        var cancelButton = UiUtil.MakeButtonCancel(vm.CancelCommand);

        buttonPanel.Children.Add(deleteButton);
        buttonPanel.Children.Add(deleteAllButton);
        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        var mainPanel = new StackPanel
        {
            Margin = UiUtil.MakeWindowMargin(),
            Spacing = 10,
            Children =
            {
                titleLabel,
                subtitleLabel,
                listView,
                infoPanel,
                buttonPanel,
            }
        };

        Content = mainPanel;

        Activated += delegate { listView.Focus(); };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _vm.OnKeyDown(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        _vm.OnClosing();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _vm.OnLoaded();
    }
}

public class IsInstalledConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "[Installed]" : "[Not Installed]";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class IsInstalledColorConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2ECC71"))
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E74C3C"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}