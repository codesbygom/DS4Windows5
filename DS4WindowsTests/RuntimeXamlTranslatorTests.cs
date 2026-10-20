using System;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Threading;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.Translations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class RuntimeXamlTranslatorTests
{
    private const string Markup = @"<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        Title='Cancel' Width='400' Height='300' ShowInTaskbar='False' ShowActivated='False'
        WindowStartupLocation='Manual' Left='-10000' Top='-10000'>
  <StackPanel>
    <Button Content='Cancel' ToolTip='Save' />
    <TextBlock>
        Save
    </TextBlock>
    <TextBlock><Hyperlink><Run Text='Cancel' /></Hyperlink></TextBlock>
    <TextBlock Text='{Binding Missing, FallbackValue=Cancel}' />
    <GroupBox Header='Cancel' />
    <Label Content='Not a resource string' />
  </StackPanel>
</Window>";

    [TestMethod]
    public void LiteralXamlTextUsesSelectedLanguage()
    {
        var german = CultureInfo.GetCultureInfo("de");
        string cancel = Strings.ResourceManager.GetString("Cancel", german);
        string save = Strings.ResourceManager.GetString("Save", german);
        Assert.AreNotEqual("Cancel", cancel, "Test needs a German translation of Cancel.");
        Assert.AreNotEqual("Save", save, "Test needs a German translation of Save.");

        string[] text = RenderTexts(german);

        CollectionAssert.AreEqual(new[]
        {
            cancel,                  // Window title
            cancel,                  // Button content
            save,                    // Button tooltip
            save,                    // TextBlock element content
            cancel,                  // Run inside a Hyperlink
            "Cancel",                // bound value is left alone
            cancel,                  // GroupBox header
            "Not a resource string", // unknown text is left alone
        }, text, string.Join(" | ", text));
    }

    [TestMethod]
    public void EnglishLeavesLiteralXamlTextUnchanged()
    {
        string[] text = RenderTexts(CultureInfo.GetCultureInfo("en"));

        CollectionAssert.AreEqual(new[]
        {
            "Cancel", "Cancel", "Save", "Save", "Cancel", "Cancel", "Cancel", "Not a resource string",
        }, text, string.Join(" | ", text));
    }

    private static string[] RenderTexts(CultureInfo culture)
    {
        string[] text = null;
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RuntimeXamlTranslator.Initialize(culture);
                var window = (Window)XamlReader.Parse(Markup);
                window.Show();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var panel = (StackPanel)window.Content;
                var button = (Button)panel.Children[0];
                var link = (Hyperlink)((TextBlock)panel.Children[2]).Inlines.First();
                text = new[]
                {
                    window.Title,
                    (string)button.Content,
                    (string)button.ToolTip,
                    ((TextBlock)panel.Children[1]).Text.Trim(),
                    ((Run)link.Inlines.First()).Text,
                    ((TextBlock)panel.Children[3]).Text,
                    (string)((GroupBox)panel.Children[4]).Header,
                    (string)((Label)panel.Children[5]).Content,
                };
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                RuntimeXamlTranslator.Initialize(CultureInfo.GetCultureInfo("en"));
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Translator test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        return text;
    }
}
