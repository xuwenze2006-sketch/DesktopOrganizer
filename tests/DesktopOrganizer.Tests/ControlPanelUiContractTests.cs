using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class ControlPanelUiContractTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedClickHandlers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["desktop-search"] = "DesktopSearchButton_Click",
            ["inbox"] = "InboxButton_Click",
            ["new-group"] = "NewGroupButton_Click",
            ["auto-classify"] = "AutoClassifyButton_Click",
            ["smart-layout"] = "SmartArrangeGroupsButton_Click",
            ["workspaces"] = "WorkspaceManagerButton_Click",
            ["folder-portals"] = "FolderPortalButton_Click",
            ["pause"] = "PauseButton_Click",
            ["edit-layout"] = "EditModeToggle_Click",
            ["fit-all"] = "AutoFitAllGroupsButton_Click",
            ["undo-layout"] = "UndoSmartLayoutButton_Click",
            ["undo-file-move"] = "UndoFileMoveButton_Click",
            ["collapse-groups"] = "CollapseGroupsButton_Click",
            ["align-icons"] = "AlignIconsButton_Click",
            ["compact-groups"] = "CompactGroupLayoutToggle_Click",
            ["recycle-bin-widget"] = "RecycleBinWidgetToggle_Click",
            ["rules"] = "RuleManagerButton_Click",
            ["auto-classify-new"] = "AutoClassifyNewItemsToggle_Click",
            ["clear-auto-classification"] = "ClearAutoClassificationButton_Click",
            ["snap-to-grid"] = "SnapToGridToggle_Click",
            ["push-reflow"] = "PushReflowToggle_Click",
            ["reserve-workspace"] = "ReserveWorkspaceToggle_Click",
            ["auto-collapse-panel"] = "AutoCollapsePanelToggle_Click",
            ["auto-start"] = "AutoStartToggle_Click",
            ["safe-mode"] = "SafeModeToggle_Click",
            ["refresh"] = "RefreshButton_Click",
            ["diagnostics"] = "OpenDiagnosticsLogButton_Click",
            ["operation-center"] = "OperationCenterButton_Click",
            ["hide-panel"] = "HidePanelButton_Click",
            ["exit"] = "ExitButton_Click"
        };

    private static string[] ExpectedCommandIds => ExpectedClickHandlers.Keys.ToArray();

    [TestMethod]
    public void ExpandedPanel_ContainsEveryLogicalCommandExactlyOnce()
    {
        XDocument document = LoadMainWindowXaml();
        XElement expandedCommands = FindNamedElement(document, "ExpandedCommands");
        string[] actualIds = GetCommandIds(expandedCommands);

        Assert.AreEqual(ExpectedCommandIds.Length, actualIds.Length);
        Assert.AreEqual(actualIds.Length, actualIds.Distinct(StringComparer.Ordinal).Count());
        CollectionAssert.AreEquivalent(ExpectedCommandIds, actualIds);

        foreach (XElement command in GetCommandElements(expandedCommands))
        {
            string commandId = command.Attribute("AutomationProperties.AutomationId")!.Value;
            string expectedStyle = command.Name.LocalName == "ToggleButton"
                ? "{StaticResource PanelCommandToggleStyle}"
                : "{StaticResource PanelCommandButtonStyle}";

            Assert.AreEqual(
                ExpectedClickHandlers[commandId],
                command.Attribute("Click")?.Value,
                $"Command '{commandId}' is bound to the wrong handler.");
            Assert.AreEqual(expectedStyle, command.Attribute("Style")?.Value);
            Assert.IsFalse(string.IsNullOrWhiteSpace(command.Attribute("Content")?.Value));
            Assert.IsFalse(string.IsNullOrWhiteSpace(command.Attribute("Tag")?.Value));
            Assert.IsFalse(string.IsNullOrWhiteSpace(command.Attribute("ToolTip")?.Value));
        }
    }

    [TestMethod]
    public void CommandTabs_PartitionCommandsIntoFourSmallPages()
    {
        XDocument document = LoadMainWindowXaml();
        var expectedByTab = new Dictionary<string, string[]>
        {
            ["CommonCommandTab"] =
            [
                "desktop-search", "inbox", "new-group", "auto-classify",
                "smart-layout", "workspaces", "folder-portals", "pause"
            ],
            ["LayoutCommandTab"] =
            [
                "edit-layout", "fit-all", "undo-layout", "undo-file-move",
                "collapse-groups", "align-icons", "compact-groups", "recycle-bin-widget"
            ],
            ["AutomationCommandTab"] =
            [
                "rules", "auto-classify-new", "clear-auto-classification", "snap-to-grid",
                "push-reflow", "reserve-workspace", "auto-collapse-panel"
            ],
            ["SystemCommandTab"] =
            [
                "auto-start", "safe-mode", "refresh", "diagnostics",
                "operation-center", "hide-panel", "exit"
            ]
        };

        foreach ((string tabName, string[] expectedIds) in expectedByTab)
        {
            XElement tab = FindNamedElement(document, tabName);
            string[] actualIds = GetCommandIds(tab);

            Assert.IsLessThanOrEqualTo(8, actualIds.Length, $"{tabName} exposes too many commands at once.");
            CollectionAssert.AreEqual(expectedIds, actualIds, $"{tabName} command mapping changed.");
        }
    }

    [TestMethod]
    public void DesktopSearchCommand_AdvertisesDesktopShortcut()
    {
        XDocument document = LoadMainWindowXaml();
        XElement command = document
            .Descendants()
            .Single(element => string.Equals(
                (string?)element.Attribute("AutomationProperties.AutomationId"),
                "desktop-search",
                StringComparison.Ordinal));

        StringAssert.Contains(command.Attribute("ToolTip")?.Value, "Ctrl+F");
    }

    [TestMethod]
    public void RefreshCommand_AdvertisesDesktopShortcut()
    {
        XDocument document = LoadMainWindowXaml();
        XElement command = document
            .Descendants()
            .Single(element => string.Equals(
                (string?)element.Attribute("AutomationProperties.AutomationId"),
                "refresh",
                StringComparison.Ordinal));

        StringAssert.Contains(command.Attribute("ToolTip")?.Value, "F5");
    }

    [TestMethod]
    public void ExpandedPanel_DefaultsToCompactCommonTab()
    {
        XDocument document = LoadMainWindowXaml();
        XElement expandedCommands = FindNamedElement(document, "ExpandedCommands");
        XElement compactHeader = FindNamedElement(document, "CompactControlPanelHeader");
        XElement tabs = FindNamedElement(document, "ControlPanelTabs");
        XElement commonTab = FindNamedElement(document, "CommonCommandTab");
        XElement tabControlStyle = FindKeyedElement(document, "PanelTabControlStyle");
        XElement tabItemsHost = tabControlStyle
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "UniformGrid" &&
                string.Equals((string?)element.Attribute("IsItemsHost"), "True", StringComparison.Ordinal));
        XAttribute? widthAttribute = expandedCommands.Attribute("Width");

        Assert.IsNotNull(widthAttribute, "ExpandedCommands must retain an explicit width limit.");
        double width = double.Parse(widthAttribute.Value, CultureInfo.InvariantCulture);

        Assert.IsLessThanOrEqualTo(420, width);
        Assert.AreEqual("250", compactHeader.Attribute("MinWidth")?.Value);
        Assert.AreEqual("34", compactHeader.Attribute("Height")?.Value);
        Assert.AreEqual("Collapsed", expandedCommands.Attribute("Visibility")?.Value);
        Assert.AreEqual("0", tabs.Attribute("SelectedIndex")?.Value);
        Assert.AreEqual("4", tabItemsHost.Attribute("Columns")?.Value);
        Assert.AreEqual(
            "ControlPanelTabs_PreviewMouseLeftButtonDown",
            tabs.Attribute("PreviewMouseLeftButtonDown")?.Value,
            "Tab selection must not depend on focus in the no-activate desktop window.");
        Assert.AreEqual("True", commonTab.Attribute("IsSelected")?.Value);
    }

    [STATestMethod]
    public void ExpanderAndRestore_RoundTripWithoutShowingWindow()
    {
        var window = new MainWindow(startQuietly: false);
        window.ControlPanel.Visibility = Visibility.Visible;
        window.ControlPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.AreEqual(268, GetPanelContentWidth(window.ControlPanel), 1);

        RaiseClick(window.PanelExpanderButton);
        window.ControlPanel.InvalidateMeasure();
        window.ControlPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.AreEqual(Visibility.Visible, window.ExpandedCommands.Visibility);
        Assert.AreEqual("整理  ▴", window.PanelExpanderButton.Content);
        Assert.AreEqual("收起整理命令", window.PanelExpanderButton.ToolTip);
        Assert.IsLessThanOrEqualTo(
            440,
            GetPanelContentWidth(window.ControlPanel),
            $"Expanded panel width grew to {GetPanelContentWidth(window.ControlPanel):F1} DIP.");
        Assert.AreEqual(438, GetPanelContentWidth(window.ControlPanel), 1);
        Assert.IsLessThanOrEqualTo(
            282,
            GetPanelContentHeight(window.ControlPanel),
            $"Expanded panel height grew to {GetPanelContentHeight(window.ControlPanel):F1} DIP.");

        RaiseClick(window.HidePanelCommandButton);

        Assert.AreEqual(Visibility.Collapsed, window.ControlPanel.Visibility);
        Assert.AreEqual(Visibility.Collapsed, window.ExpandedCommands.Visibility);
        Assert.AreEqual(Visibility.Visible, window.ControlPanelRestoreButton.Visibility);

        RaiseClick(window.ControlPanelRestoreButton);

        Assert.AreEqual(Visibility.Visible, window.ControlPanel.Visibility);
        Assert.AreEqual(Visibility.Collapsed, window.ExpandedCommands.Visibility);
        Assert.AreEqual(Visibility.Collapsed, window.ControlPanelRestoreButton.Visibility);
        Assert.AreEqual("整理  ▾", window.PanelExpanderButton.Content);
        window.ControlPanel.InvalidateMeasure();
        window.ControlPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.AreEqual(
            268,
            GetPanelContentWidth(window.ControlPanel),
            1);
    }

    [STATestMethod]
    public void Expander_RepeatedRightAnchoredRoundTripsDoNotDrift()
    {
        var window = new MainWindow(startQuietly: false);
        window.ControlPanel.Visibility = Visibility.Visible;
        window.ControlPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double compactWidth = GetPanelContentWidth(window.ControlPanel);
        Rect workArea = SystemParameters.WorkArea;
        double compactX = Math.Max(14, workArea.Width - compactWidth - 14);
        window.ControlPanel.Margin = new Thickness(compactX, 14, 0, 0);
        window.RootGrid.Measure(new Size(workArea.Width, workArea.Height));
        window.RootGrid.Arrange(new Rect(0, 0, workArea.Width, workArea.Height));
        Assert.AreEqual(
            compactWidth,
            window.ControlPanel.ActualWidth,
            1,
            "The regression setup must begin with a rendered compact width.");

        for (int iteration = 0; iteration < 12; iteration++)
        {
            RaiseClick(window.PanelExpanderButton);
            double expandedWidth = GetPanelContentWidth(window.ControlPanel);
            Assert.AreEqual(
                workArea.Width - 14,
                window.ControlPanel.Margin.Left + expandedWidth,
                1,
                $"Expanded right edge drifted on iteration {iteration}.");
            double immediateExpandedX = window.ControlPanel.Margin.Left;
            DrainDeferredLayout(window);
            Assert.AreEqual(
                immediateExpandedX,
                window.ControlPanel.Margin.Left,
                1,
                $"Deferred clamp moved the expanded panel on iteration {iteration}.");

            RaiseClick(window.PanelExpanderButton);
            double roundTripWidth = GetPanelContentWidth(window.ControlPanel);
            Assert.AreEqual(
                compactX,
                window.ControlPanel.Margin.Left,
                1,
                $"Collapsed position drifted on iteration {iteration}.");
            Assert.AreEqual(compactWidth, roundTripWidth, 1);
            double immediateCollapsedX = window.ControlPanel.Margin.Left;
            DrainDeferredLayout(window);
            Assert.AreEqual(
                immediateCollapsedX,
                window.ControlPanel.Margin.Left,
                1,
                $"Deferred clamp moved the collapsed panel on iteration {iteration}.");
        }
    }

    [STATestMethod]
    public void TabHeaderMouseDown_SelectsPageWithoutWindowActivation()
    {
        var window = new MainWindow(startQuietly: false);
        window.ControlPanelTabs.ApplyTemplate();
        window.ControlPanelTabs.Measure(new Size(420, 214));
        window.ControlPanelTabs.Arrange(new Rect(0, 0, 420, 214));

        var mouseEvent = new MouseButtonEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        };

        window.LayoutCommandTab.RaiseEvent(mouseEvent);

        Assert.AreSame(window.LayoutCommandTab, window.ControlPanelTabs.SelectedItem);
        Assert.IsTrue(mouseEvent.Handled);

        window.ControlPanelTabs.Measure(new Size(420, 214));
        window.ControlPanelTabs.Arrange(new Rect(0, 0, 420, 214));
        var commandMouseEvent = new MouseButtonEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        };

        window.EditModeToggle.RaiseEvent(commandMouseEvent);

        Assert.IsFalse(commandMouseEvent.Handled, "Tab selection must not swallow command input.");
    }

    private static IEnumerable<XElement> GetCommandElements(XElement root) =>
        root.Descendants()
            .Where(element => element.Attribute("AutomationProperties.AutomationId") != null);

    private static double GetPanelContentWidth(FrameworkElement panel)
    {
        panel.InvalidateMeasure();
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return panel.DesiredSize.Width - panel.Margin.Left - panel.Margin.Right;
    }

    private static double GetPanelContentHeight(FrameworkElement panel)
    {
        panel.InvalidateMeasure();
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return panel.DesiredSize.Height - panel.Margin.Top - panel.Margin.Bottom;
    }

    private static void DrainDeferredLayout(MainWindow window) =>
        window.Dispatcher.Invoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => { }));

    private static string[] GetCommandIds(XElement root) =>
        GetCommandElements(root)
            .Select(element => element.Attribute("AutomationProperties.AutomationId")!.Value)
            .ToArray();

    private static XElement FindNamedElement(XDocument document, string name)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document
            .Descendants()
            .Single(element => string.Equals((string?)element.Attribute(xaml + "Name"), name, StringComparison.Ordinal));
    }

    private static void RaiseClick(ButtonBase button) =>
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

    private static XElement FindKeyedElement(XDocument document, string key)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document
            .Descendants()
            .Single(element => string.Equals((string?)element.Attribute(xaml + "Key"), key, StringComparison.Ordinal));
    }

    private static XDocument LoadMainWindowXaml([CallerFilePath] string sourceFilePath = "")
    {
        string testDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Cannot resolve the test source directory.");
        string projectRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", ".."));
        return XDocument.Load(Path.Combine(projectRoot, "MainWindow.xaml"));
    }
}
