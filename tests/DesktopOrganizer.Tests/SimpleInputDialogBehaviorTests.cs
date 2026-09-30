using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class SimpleInputDialogBehaviorTests
{
    [STATestMethod]
    [DataRow("")]
    [DataRow(" \t\r\n ")]
    public void RequiredInput_BlocksEmptyConfirmationWithoutClosing(string input)
    {
        RunDialog(input, dialog =>
        {
            Button confirm = FindButton(dialog, isDefault: true);
            Assert.IsFalse(confirm.IsEnabled, "Empty names must not be confirmable.");
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsTrue(dialog.IsVisible, "Invalid input must stay editable in the same dialog.");
            Assert.AreEqual(string.Empty, dialog.ResultText);
            var feedback = (TextBlock)dialog.FindName("ValidationText");
            Assert.IsNotNull(feedback);
            Assert.AreEqual(Visibility.Visible, feedback.Visibility);
            Assert.IsFalse(string.IsNullOrWhiteSpace(feedback.Text));
        }, expectedResult: false);
    }

    [STATestMethod]
    public void RequiredInput_CanBeCorrectedAndConfirmedWithTrimmedResult()
    {
        RunDialog(" ", dialog =>
        {
            Button confirm = FindButton(dialog, isDefault: true);
            Assert.IsFalse(confirm.IsEnabled);
            ((TextBox)dialog.FindName("InputBox")).Text = "  Work  ";
            Assert.IsTrue(confirm.IsEnabled);
            Assert.AreEqual(Visibility.Collapsed,
                ((TextBlock)dialog.FindName("ValidationText")).Visibility);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // A queued second confirmation must be harmless after the modal closes.
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, expectedResult: true, expectedText: "Work");
    }

    [STATestMethod]
    public void OptionalInput_AllowsClearingTagsAndUpdatesConfirmationImmediately()
    {
        RunDialog(" ", dialog =>
        {
            var property = typeof(SimpleInputDialog).GetProperty("AllowEmpty");
            Assert.IsNotNull(property, "Tag clearing needs an explicit optional-input mode.");
            property.SetValue(dialog, true);
            Button confirm = FindButton(dialog, isDefault: true);
            Assert.IsTrue(confirm.IsEnabled);
            Assert.AreEqual(Visibility.Collapsed,
                ((TextBlock)dialog.FindName("ValidationText")).Visibility);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, expectedResult: true);
    }

    [STATestMethod]
    public void Cancel_DoesNotAcceptOrReturnEditedText()
    {
        RunDialog("original", dialog =>
        {
            ((TextBox)dialog.FindName("InputBox")).Text = "edited";
            FindButton(dialog, isDefault: false)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, expectedResult: false);
    }

    [STATestMethod]
    public void WindowClose_DoesNotAcceptText()
    {
        RunDialog("draft", dialog => dialog.Close(), expectedResult: false);
    }

    private static Button FindButton(SimpleInputDialog dialog, bool isDefault)
    {
        var root = (StackPanel)dialog.Content;
        return root.Children.OfType<StackPanel>().Single()
            .Children.OfType<Button>().Single(button => button.IsDefault == isDefault);
    }

    private static void RunDialog(string initialText, Action<SimpleInputDialog> action,
        bool expectedResult, string expectedText = "")
    {
        var dialog = new SimpleInputDialog("Test name", initialText);
        Exception? failure = null;
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                try { action(dialog); }
                catch (Exception exception) { failure = exception; }
                finally { if (dialog.IsVisible) dialog.Close(); }
            }));
        bool? result = dialog.ShowDialog();
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.AreEqual(expectedResult, result == true);
        Assert.AreEqual(expectedText, dialog.ResultText);
    }
}
