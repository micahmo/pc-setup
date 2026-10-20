using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace PCSetup;

/// <summary>Small attached behaviors for the views, so DataTemplates can stay code-free.</summary>
public static class Behaviors
{
    /// <summary>Scrolls the element into view whenever the bound value turns true (e.g. the item that's running now).</summary>
    public static readonly DependencyProperty BringIntoViewWhenProperty = DependencyProperty.RegisterAttached(
        "BringIntoViewWhen", typeof(bool), typeof(Behaviors),
        new PropertyMetadata(false, (d, e) =>
        {
            if (e.NewValue is true && d is FrameworkElement element)
                element.Dispatcher.InvokeAsync(() => element.BringIntoView());
        }));

    public static bool GetBringIntoViewWhen(DependencyObject d) => (bool)d.GetValue(BringIntoViewWhenProperty);
    public static void SetBringIntoViewWhen(DependencyObject d, bool value) => d.SetValue(BringIntoViewWhenProperty, value);

    /// <summary>Keeps a ListBox pinned to its newest item: as items are added, and whenever it's shown (e.g. a log expanded).</summary>
    public static readonly DependencyProperty FollowNewestProperty = DependencyProperty.RegisterAttached(
        "FollowNewest", typeof(bool), typeof(Behaviors),
        new PropertyMetadata(false, (d, e) =>
        {
            if (d is not ListBox list || e.NewValue is not true) return;

            // Scroll after layout, so the new item exists on screen; while collapsed there's nothing to scroll.
            void ScrollToEnd() => list.Dispatcher.InvokeAsync(() =>
            {
                if (list.IsVisible && list.Items.Count > 0) list.ScrollIntoView(list.Items[^1]);
            }, System.Windows.Threading.DispatcherPriority.Background);

            ((INotifyCollectionChanged)list.Items).CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Add) ScrollToEnd();
            };
            list.IsVisibleChanged += (_, _) => ScrollToEnd();
        }));

    public static bool GetFollowNewest(DependencyObject d) => (bool)d.GetValue(FollowNewestProperty);
    public static void SetFollowNewest(DependencyObject d, bool value) => d.SetValue(FollowNewestProperty, value);
}
