using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using CodexTPS.WindowsApp;

namespace CodexTPS.Windows.Tests;

public sealed class DashboardFormBehaviorTests
{
    [Fact]
    public void DeactivationHidesDashboardToTray()
    {
        RunOnStaThread(() =>
        {
            using var form = new DashboardForm(string.Empty);
            form.Show();
            Assert.True(form.Visible);

            var onDeactivate = typeof(DashboardForm).GetMethod(
                "OnDeactivate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(onDeactivate);
            onDeactivate.Invoke(form, [EventArgs.Empty]);

            Assert.False(form.Visible);
            Assert.Equal(FormWindowState.Normal, form.WindowState);
        });
    }

    [Fact]
    public void HeaderProvidesExplicitMinimizeToTrayButton()
    {
        RunOnStaThread(() =>
        {
            using var form = new DashboardForm(string.Empty);
            var minimize = Assert.IsType<Button>(
                FindByAccessibleName(form, "最小化到通知区域"));

            form.Show();
            Assert.True(form.Visible);
            minimize.PerformClick();

            Assert.False(form.Visible);
            Assert.False(form.ShowInTaskbar);
        });
    }

    private static Control? FindByAccessibleName(Control root, string accessibleName)
    {
        foreach (Control child in root.Controls)
        {
            if (child.AccessibleName == accessibleName)
            {
                return child;
            }

            var nested = FindByAccessibleName(child, accessibleName);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                failure = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WinForms test thread timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
