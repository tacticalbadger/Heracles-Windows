using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Heracles.Tests.Editor;

/// <summary>
/// The one thread every test of a view runs on. WPF's objects belong to the
/// thread that made them and want it to be a single-threaded apartment with
/// a dispatcher, which the test runner's threads are not.
/// </summary>
internal static class Sta
{
    private static readonly Lazy<Dispatcher> Thread = new(Start);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "view tests",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    /// <summary>Held by the test that is on the views' thread, for as long as it is.</summary>
    private static readonly object OneAtATime = new();

    /// <summary>
    /// Run a test on the views' thread. What it throws is thrown here, as it was.
    ///
    /// One test at a time, and the next waits here, off that thread. Without
    /// this, a test that lets the thread do what it has put off (every
    /// typing test does, after each key) would find another class's test
    /// among the things put off, and run the whole of it in the middle of
    /// itself: a test that times its own typing was once handed two seconds
    /// of another test's waiting, and failed for it.
    /// </summary>
    public static void Run(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        lock (OneAtATime)
        {
            Thread.Value.Invoke(() =>
            {
                try { test(); }
                catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
            });
        }
        failure?.Throw();
    }
}

/// <summary>
/// Views in a test: laid out by hand, with no window. A window, even one
/// off the screen, takes the keyboard from whoever is at the machine the
/// first time anything in it is given the focus.
/// </summary>
internal static class Views
{
    /// <summary>Let the dispatcher do what it has put off. A text box types what it was sent only then.</summary>
    public static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

    /// <summary>Give a tree of views its size, as a window would.</summary>
    public static void Layout(FrameworkElement root, double width = 800, double height = 600)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
    }

    /// <summary>A view by itself at a width, as tall as it wants to be.</summary>
    public static StackPanel Column(double width, params UIElement[] children)
    {
        var panel = new StackPanel { Width = width };
        foreach (var child in children) panel.Children.Add(child);
        Layout(panel, width, 4000);
        return panel;
    }

    /// <summary>Type into a text box, one character at a time, the way the keyboard does.</summary>
    public static void Type(UIElement box, string text)
    {
        var characters = StringInfo.GetTextElementEnumerator(text);
        while (characters.MoveNext())
        {
            TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, box, characters.GetTextElement()));
            Pump();
        }
    }
}
