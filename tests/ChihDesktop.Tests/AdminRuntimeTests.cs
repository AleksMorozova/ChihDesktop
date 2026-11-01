using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class AdminRuntimeTests
{
    [Fact]
    public void Menu_request_waits_for_delayed_Closed_when_IsOpen_is_already_false() => Sta(() =>
    {
        var menu = new ContextMenu(); int opened = 0;
        Assert.False(menu.IsOpen);
        AdminWindowManager.AfterMenuClosed(menu, menuSessionOpen: true, () => opened++);
        Pump(); Assert.Equal(0, opened); // Actual failing event order: IsOpen=false, menu session still closing.
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        Pump(); Assert.Equal(1, opened);
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        Pump(); Assert.Equal(1, opened); // One-shot callback, not a retained handler.
    });

    [Fact]
    public void Already_closed_menu_queues_request_without_dropping_it() => Sta(() =>
    {
        int opened = 0;
        AdminWindowManager.AfterMenuClosed(new ContextMenu(), false, () => opened++);
        Assert.Equal(0, opened); Pump(); Assert.Equal(1, opened);
    });

    [Fact]
    public void Diagnostics_record_technical_failure_without_exception_messages_or_source_paths()
    {
        using var files = new TempFiles(); var log = new AdminDiagnostics(files.File("logs"));
        try { throw new InvalidOperationException("Synthetic sensitive content must not be logged"); }
        catch (Exception ex) { log.Write("admin.open.failed", error: ex); }
        string text = File.ReadAllText(log.LogPath!);
        Assert.DoesNotContain("Synthetic sensitive content", text);
        using var json = JsonDocument.Parse(text);
        Assert.Equal("System.InvalidOperationException", json.RootElement.GetProperty("exceptionType").GetString());
        Assert.Equal("admin.open.failed", json.RootElement.GetProperty("stage").GetString());
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
