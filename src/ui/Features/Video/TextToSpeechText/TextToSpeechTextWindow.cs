using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using System.Collections.Generic;

namespace Nikse.SubtitleEdit.Features.Video.TextToSpeechText;

public class TextToSpeechTextWindow : Window
{
    private readonly TextToSpeechTextViewModel _vm;

    public TextToSpeechTextWindow(TextToSpeechTextViewModel vm)
    {
        UiUtil.InitializeWindow(this, GetType().Name);
        Title = "Text to Speech - Text Files";
        Width = 1000;
        Height = 850;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        vm.Window = this;
        DataContext = vm;
        _vm = vm;
        
        Content = MakeMainLayout(vm);
    }

    private static Grid MakeMainLayout(TextToSpeechTextViewModel vm)
    {
        var mainLayout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) }, // Mode selection
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) }, // Engine/Settings
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }, // Splitter
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }, // Center content
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) }, // Progress
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) }, // Buttons
            },
            Margin = UiUtil.MakeWindowMargin(),
            ColumnSpacing = 10,
            RowSpacing = 0,
        };

        var modePanel = MakeModeSelection(vm);
        var settingsPanel = MakeTopControls(vm);
        var centerLayout = MakeCenterControls(vm);
        var progressLayout = MakeProgressControls(vm);
        var buttonLayout = MakeButtonControls(vm);

        var splitter = new GridSplitter
        {
            Height = 2,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Gray,
            Margin = new Thickness(0, 4, 0, 4),
        };

        mainLayout.Add(modePanel, 0, 0);
        mainLayout.Add(settingsPanel, 1, 0);
        mainLayout.Add(splitter, 2, 0);
        mainLayout.Add(centerLayout, 3, 0);
        mainLayout.Add(progressLayout, 4, 0);
        mainLayout.Add(buttonLayout, 5, 0);
        return mainLayout;
    }

    private static StackPanel MakeModeSelection(TextToSpeechTextViewModel vm)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
        };

        var singleButton = new ToggleButton
        {
            Content = "Single Mode",
            [!ToggleButton.IsCheckedProperty] = new Binding(nameof(vm.IsSingleMode)) { Mode = BindingMode.TwoWay },
            Width = 150,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        singleButton.Click += (s, e) => { vm.IsBatchMode = false; vm.IsSingleMode = true; };

        var batchButton = new ToggleButton
        {
            Content = "Batch Mode",
            [!ToggleButton.IsCheckedProperty] = new Binding(nameof(vm.IsBatchMode)) { Mode = BindingMode.TwoWay },
            Width = 150,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        batchButton.Click += (s, e) => { vm.IsSingleMode = false; vm.IsBatchMode = true; };

        panel.Children.Add(singleButton);
        panel.Children.Add(batchButton);

        return panel;
    }

    private static Grid MakeTopControls(TextToSpeechTextViewModel vm)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            ColumnSpacing = 20,
            Margin = new Thickness(0, 0, 0, 10),
        };

        var enginePanel = MakeEngineControls(vm);
        var settingsPanel = MakeSettingsControls(vm);

        grid.Add(enginePanel, 0, 0);
        grid.Add(settingsPanel, 0, 1);

        return grid;
    }

    private static StackPanel MakeEngineControls(TextToSpeechTextViewModel vm)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
        };

        var labelMinWidth = 120;
        var controlMinWidth = 280;

        var comboBoxEngines = UiUtil.MakeComboBox(vm.Engines, vm, nameof(vm.SelectedEngine)).WithWidth(controlMinWidth);
        comboBoxEngines.SelectionChanged += vm.SelectedEngineChanged;

        var engineRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        engineRow.Children.Add(new Label { Content = Se.Language.General.Engine, MinWidth = labelMinWidth, VerticalAlignment = VerticalAlignment.Center });
        engineRow.Children.Add(comboBoxEngines);

        var comboBoxVoices = UiUtil.MakeComboBox(vm.Voices, vm, nameof(vm.SelectedVoice)).WithWidth(controlMinWidth);
        var voiceRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        voiceRow.Children.Add(new Label { Content = Se.Language.General.Voice, MinWidth = labelMinWidth, VerticalAlignment = VerticalAlignment.Center });
        voiceRow.Children.Add(comboBoxVoices);

        var comboBoxModels = UiUtil.MakeComboBox(vm.Models, vm, nameof(vm.SelectedModel)).WithWidth(controlMinWidth);
        var modelRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        modelRow.Children.Add(new Label { Content = Se.Language.General.Model, MinWidth = labelMinWidth, VerticalAlignment = VerticalAlignment.Center });
        modelRow.Children.Add(comboBoxModels);
        modelRow[!StackPanel.IsVisibleProperty] = new Binding(nameof(vm.HasModel)) { Mode = BindingMode.OneWay };

        var comboBoxLanguages = UiUtil.MakeComboBox(vm.Languages, vm, nameof(vm.SelectedLanguage)).WithWidth(controlMinWidth);
        var languageRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        languageRow.Children.Add(new Label { Content = Se.Language.General.Language, MinWidth = labelMinWidth, VerticalAlignment = VerticalAlignment.Center });
        languageRow.Children.Add(comboBoxLanguages);
        languageRow[!StackPanel.IsVisibleProperty] = new Binding(nameof(vm.HasLanguageParameter)) { Mode = BindingMode.OneWay };

        var apiKeyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        apiKeyRow.Children.Add(new Label { Content = Se.Language.General.ApiKey, MinWidth = labelMinWidth, VerticalAlignment = VerticalAlignment.Center });
        apiKeyRow.Children.Add(UiUtil.MakeTextBox(250, vm, nameof(vm.ApiKey)));
        apiKeyRow[!StackPanel.IsVisibleProperty] = new Binding(nameof(vm.HasApiKey)) { Mode = BindingMode.OneWay };

        panel.Children.Add(engineRow);
        panel.Children.Add(voiceRow);
        panel.Children.Add(modelRow);
        panel.Children.Add(languageRow);
        panel.Children.Add(apiKeyRow);

        return panel;
    }

    private static StackPanel MakeSettingsControls(TextToSpeechTextViewModel vm)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
        };

        var labelMinWidth = 120;

        var fileNameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        fileNameRow.Children.Add(new Label { Content = "File Name", MinWidth = labelMinWidth, VerticalAlignment = VerticalAlignment.Center });
        fileNameRow.Children.Add(UiUtil.MakeTextBox(200, vm, nameof(vm.FileName)));
        fileNameRow.Children.Add(new Label { Content = ".wav / .mp3", VerticalAlignment = VerticalAlignment.Center });
        fileNameRow[!StackPanel.IsVisibleProperty] = new Binding(nameof(vm.IsSingleMode)) { Mode = BindingMode.OneWay };

        var outputFolderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        outputFolderRow.Children.Add(new Label { Content = "Output Folder", MinWidth = labelMinWidth, VerticalAlignment = VerticalAlignment.Center });
        outputFolderRow.Children.Add(UiUtil.MakeTextBox(200, vm, nameof(vm.OutputFolder)));
        outputFolderRow.Children.Add(UiUtil.MakeButton("...", vm.BrowseOutputFolderCommand));

        var audioFormatRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        audioFormatRow.Children.Add(new Label { Content = "Audio Format", MinWidth = labelMinWidth, VerticalAlignment = VerticalAlignment.Center });
        audioFormatRow.Children.Add(UiUtil.MakeComboBox(vm.AudioFormats, vm, nameof(vm.SelectedAudioFormat)).WithWidth(100));

        var saveOptionsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 15 };
        saveOptionsPanel.Children.Add(new CheckBox { Content = "Save TXT", [!CheckBox.IsCheckedProperty] = new Binding(nameof(vm.DoSaveTxt)) { Mode = BindingMode.TwoWay } });

        panel.Children.Add(fileNameRow);
        panel.Children.Add(outputFolderRow);
        panel.Children.Add(audioFormatRow);
        panel.Children.Add(saveOptionsPanel);

        return panel;
    }

    private static Grid MakeCenterControls(TextToSpeechTextViewModel vm)
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };

        // Single Mode Controls
        var singleGrid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Stretch,
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
            },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) },
            },
            ColumnSpacing = 10,
            RowSpacing = 10,
            [!Grid.IsVisibleProperty] = new Binding(nameof(vm.IsSingleMode)) { Mode = BindingMode.OneWay },
        };

        var singleTextBox = new TextBox
        {
            [!TextBox.TextProperty] = new Binding(nameof(vm.InputText)) { Mode = BindingMode.TwoWay },
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Stretch,
            PlaceholderText = "Enter text here or paste from clipboard...",
            [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
        };

        var singleSidePanel = new StackPanel { Spacing = 10, Width = 150 };
        singleSidePanel.Children.Add(UiUtil.MakeButton("Import File...", vm.ImportTextFileCommand));
        singleSidePanel.Children.Add(UiUtil.MakeButton("Paste Text", vm.PasteTextCommand));
        singleSidePanel.Children.Add(UiUtil.MakeButton("Test Voice", vm.TestVoiceCommand).WithBindIsEnabled(nameof(vm.IsNotGenerating)));
        singleSidePanel.Children.Add(UiUtil.MakeButton("Manage Models...", vm.OpenModelManagerCommand));

        singleGrid.Add(singleTextBox, 0, 0);
        singleGrid.Add(singleSidePanel, 0, 1);

        // Batch Mode Controls
        var batchGrid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Stretch,
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
            },
            RowSpacing = 10,
            ColumnSpacing = 10,
            [!Grid.IsVisibleProperty] = new Binding(nameof(vm.IsBatchMode)) { Mode = BindingMode.OneWay },
        };

        var listBox = new ListBox
        {
            ItemsSource = vm.BatchItems,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 0, 0, 0),
            ItemTemplate = new FuncDataTemplate<TextToSpeechTextBatchItem>((item, namescope) =>
            {
                var itemGrid = new Grid
                {
                    ClipToBounds = true,
                    RowDefinitions =
                    {
                        new RowDefinition { Height = new GridLength(100, GridUnitType.Pixel) }, // Row 0: Content
                        new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },   // Row 1: Separator
                    },
                    Margin = new Thickness(0, 0, 0, 5),
                };

                var numberingLabel = new Label 
                { 
                    [!Label.ContentProperty] = new Binding(nameof(item.Name)), 
                    VerticalAlignment = VerticalAlignment.Center, 
                    HorizontalAlignment = HorizontalAlignment.Center, 
                    FontWeight = FontWeight.Bold,
                    Width = 35,
                };

                var progressContainer = new Border
                {
                    Width = 100,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    ClipToBounds = true,
                    Child = new StackPanel
                    {
                        Spacing = 1,
                        Children =
                        {
                            new ProgressBar 
                            { 
                                Minimum = 0, 
                                Maximum = 100, 
                                [!ProgressBar.ValueProperty] = new Binding(nameof(item.Progress)) { Mode = BindingMode.OneWay }, 
                                Height = 10, 
                                Width = 80,
                                HorizontalAlignment = HorizontalAlignment.Center 
                            },
                            new TextBlock 
                            { 
                                [!TextBlock.TextProperty] = new Binding(nameof(item.Progress)) { StringFormat = "{0:0}%" },
                                FontSize = 10, 
                                HorizontalAlignment = HorizontalAlignment.Center, 
                                FontWeight = FontWeight.Bold 
                            },
                            new TextBlock 
                            { 
                                [!TextBlock.TextProperty] = new Binding(nameof(item.ProgressText)) { Mode = BindingMode.OneWay }, 
                                FontSize = 8, 
                                HorizontalAlignment = HorizontalAlignment.Center, 
                                MaxWidth = 95
                            }
                        }
                    }
                };

                var deleteButton = UiUtil.MakeButton("X", vm.RemoveBatchItemCommand, item);
                deleteButton.Width = 32;
                deleteButton.Height = 32;
                deleteButton.Margin = new Thickness(10, 0, 5, 0);

                var textBox = new TextBox
                {
                    [!TextBox.TextProperty] = new Binding(nameof(item.Text)) { Mode = BindingMode.TwoWay },
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(5, 0, 10, 0),
                    PlaceholderText = "Paste text here...",
                    [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                };

                var contentDock = new DockPanel { LastChildFill = true };
                DockPanel.SetDock(numberingLabel, Dock.Left);
                
                var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                rightStack.Children.Add(progressContainer);
                rightStack.Children.Add(deleteButton);
                DockPanel.SetDock(rightStack, Dock.Right);

                contentDock.Children.Add(numberingLabel);
                contentDock.Children.Add(rightStack);
                contentDock.Children.Add(textBox); // Fills center

                var separator = new Border
                {
                    Height = 1,
                    Background = Brushes.Gray,
                    Margin = new Thickness(0, 5, 0, 0),
                };

                itemGrid.Add(contentDock, 0, 0);
                itemGrid.Add(separator, 1, 0);

                return itemGrid;
            }),
        };

        batchGrid.Add(listBox, 0, 0);

        grid.Children.Add(singleGrid);
        grid.Children.Add(batchGrid);

        return grid;
    }

    private static Grid MakeProgressControls(TextToSpeechTextViewModel vm)
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
            },
            Margin = new Thickness(0, 10, 0, 10),
            [!Grid.IsVisibleProperty] = new Binding(nameof(vm.IsGenerating)) { Mode = BindingMode.OneWay },
        };

        var labelOverall = new Label { [!Label.ContentProperty] = new Binding(nameof(vm.ProgressText)) { Mode = BindingMode.OneWay } };
        var progressOverall = new ProgressBar { Minimum = 0, Maximum = 100, [!ProgressBar.ValueProperty] = new Binding(nameof(vm.ProgressValue)) { Mode = BindingMode.OneWay }, Height = 15 };

        var labelIndividual = new Label { [!Label.ContentProperty] = new Binding(nameof(vm.IndividualProgressText)) { Mode = BindingMode.OneWay }, Margin = new Thickness(0, 5, 0, 0) };
        var progressIndividual = new ProgressBar { Minimum = 0, Maximum = 100, [!ProgressBar.ValueProperty] = new Binding(nameof(vm.IndividualProgressValue)) { Mode = BindingMode.OneWay }, Height = 10 };

        grid.Add(labelOverall, 0, 0);
        grid.Add(progressOverall, 1, 0);
        grid.Add(labelIndividual, 2, 0);
        grid.Add(progressIndividual, 3, 0);

        return grid;
    }

    private static Grid MakeButtonControls(TextToSpeechTextViewModel vm)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) },
            },
            Margin = new Thickness(0, 10, 0, 0),
        };

        // Batch-specific buttons (Left side)
        var batchPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        batchPanel.Children.Add(UiUtil.MakeButton("+ Add Box", vm.AddBatchItemCommand));
        batchPanel.Children.Add(UiUtil.MakeButton("Import Files...", vm.ImportTextFileCommand));
        batchPanel.Children.Add(UiUtil.MakeButton("Clear All", vm.ClearBatchItemsCommand));
        batchPanel[!StackPanel.IsVisibleProperty] = new Binding(nameof(vm.IsBatchMode)) { Mode = BindingMode.OneWay };

        // Main action buttons (Right side)
        var generateButton = UiUtil.MakeButton("Generate Audio", vm.GenerateAudioCommand).WithBindIsEnabled(nameof(vm.IsNotGenerating));
        var cancelButton = UiUtil.MakeButtonCancel(vm.CancelCommand).WithBindIsVisible(nameof(vm.IsGenerating));
        var doneButton = UiUtil.MakeButtonDone(vm.DoneCommand).WithBindIsVisible(nameof(vm.IsNotGenerating));
        var actionPanel = UiUtil.MakeButtonBar(generateButton, cancelButton, doneButton);

        grid.Add(batchPanel, 0, 0);
        grid.Add(actionPanel, 0, 1);

        return grid;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _vm.OnKeyDown(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        _vm.OnClosing(e);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _vm.OnLoaded(e);
    }
}