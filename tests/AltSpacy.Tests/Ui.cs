using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AltSpacy.Tests;

/// <summary>Runs real WPF windows on an STA thread and drives them like a user would.</summary>
internal static class Ui
{
    /// <summary>
    /// xunit runs test classes in parallel; WPF resource lookup (StaticResource, theme dictionaries) is not safe across
    /// UI threads and fails intermittently with KeyNotFoundException. One window thread at a time.
    /// </summary>
    private static readonly Lock Gate = new();

    public static void Run(Action body)
    {
        lock (Gate) RunOnSta(body);
    }

    private static void RunOnSta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Throw(error);
    }

    /// <summary>Processes queued dispatcher work; exceptions thrown by event handlers surface here.</summary>
    public static void Flush()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    public static void Show(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
        window.Show();
        Flush();
    }

    /// <summary>Clicks through UI Automation, which runs the same code path as a mouse click (including IsDefault/IsCancel behaviour).</summary>
    public static void Click(Button button)
    {
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
        Flush();
    }

    public static void Press(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target)!;
        foreach (var routedEvent in new[] { Keyboard.PreviewKeyDownEvent, Keyboard.KeyDownEvent })
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = routedEvent };
            target.RaiseEvent(args);
            if (args.Handled) break;
        }
        Flush();
    }

    /// <summary>A real left click: preview + bubbling mouse down, then up, as WPF's input system raises them.</summary>
    public static void MouseClick(UIElement target)
    {
        foreach (var (preview, bubble) in new[]
                 {
                     (UIElement.PreviewMouseLeftButtonDownEvent, UIElement.MouseLeftButtonDownEvent),
                     (UIElement.PreviewMouseLeftButtonUpEvent, UIElement.MouseLeftButtonUpEvent),
                 })
        {
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = preview };
            target.RaiseEvent(args);
            if (!args.Handled) target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = bubble });
            Flush();
        }
    }

    public static Button Button(DependencyObject root, string content) =>
        Find<Button>(root, b => Equals(b.Content, content)) ?? throw new InvalidOperationException($"No button '{content}'.");

    public static T? Find<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        if (root is T match && predicate(match)) return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if (Find(child, predicate) is { } found) return found;
        return null;
    }
}
