using Adk.Gui.Collections.Reactive;
using Adk.Gui.Controls;
using LcdMod.Common.Mvvm;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class ObservableCollectionTests
{
    [Fact]
    public void ListNotificationsProvideIndicesAndRemovedItemsForSubscriptionCleanup()
    {
        var source = new ObservableList<string> { "first", "second" };
        var changes = new List<NotifyCollectionChangedEventArgs>();
        source.CollectionChanged += (sender, args) =>
        {
            Assert.Same(source, sender);
            changes.Add(args);
        };

        source.Insert(1, "inserted");
        source.Move(2, 0);
        source[1] = "replacement";
        source.RemoveAt(2);
        source.Clear();
        source.Clear();
        Assert.False(source.Remove("absent"));

        Assert.Equal(5, changes.Count);
        Assert.Equal(NotifyCollectionChangedAction.Add, changes[0].Action);
        Assert.Equal(1, changes[0].NewStartingIndex);
        Assert.Equal("inserted", Assert.Single(changes[0].NewItems.Cast<string>()));
        Assert.Equal(NotifyCollectionChangedAction.Move, changes[1].Action);
        Assert.Equal(2, changes[1].OldStartingIndex);
        Assert.Equal(0, changes[1].NewStartingIndex);
        Assert.Equal("second", Assert.Single(changes[1].NewItems.Cast<string>()));
        Assert.Equal(NotifyCollectionChangedAction.Replace, changes[2].Action);
        Assert.Equal("first", Assert.Single(changes[2].OldItems.Cast<string>()));
        Assert.Equal("replacement", Assert.Single(changes[2].NewItems.Cast<string>()));
        Assert.Equal(1, changes[2].NewStartingIndex);
        Assert.Equal(NotifyCollectionChangedAction.Remove, changes[3].Action);
        Assert.Equal(2, changes[3].OldStartingIndex);
        Assert.Equal("inserted", Assert.Single(changes[3].OldItems.Cast<string>()));
        Assert.Equal(NotifyCollectionChangedAction.Remove, changes[4].Action);
        Assert.Equal(new[] { "second", "replacement" }, changes[4].OldItems.Cast<string>());
        Assert.Empty(source);
    }

    [Fact]
    public void DictionaryUpdatesReachAdkItemsControlAndRetainPreviousValues()
    {
        var source = new ObservableDictionary<string, int>();
        var changes = new List<NotifyCollectionChangedEventArgs>();
        source.CollectionChanged += (_, args) => changes.Add(args);
        using var control = new ItemsControl { ItemsSource = source };

        source["one"] = 1;
        source.Add("two", 2);
        Assert.Equal(2, control.ItemCount);
        source["one"] = 3;
        var replacement = changes[2];
        Assert.Equal(NotifyCollectionChangedAction.Replace, replacement.Action);
        Assert.Equal(new KeyValuePair<string, int>("one", 1), replacement.OldItems[0]);
        Assert.Equal(new KeyValuePair<string, int>("one", 3), replacement.NewItems[0]);
        Assert.Equal(new KeyValuePair<string, int>("one", 3), control.GetContainerAt(0).DataContext);
        source["one"] = 3;
        Assert.False(source.Remove(new KeyValuePair<string, int>("one", 1)));
        Assert.Equal(3, changes.Count);
        Assert.True(source.Remove("two"));
        Assert.Equal(1, control.ItemCount);
        source.Clear();
        Assert.Equal(new KeyValuePair<string, int>("one", 3), changes[4].OldItems[0]);
        Assert.Equal(0, control.ItemCount);
        source.Clear();
        Assert.Equal(5, changes.Count);
    }

    [Fact]
    public void ListStillUpdatesAdkControlWhenEarlierSubscribersThrow()
    {
        var source = new ObservableList<object>();
        var propertyError = new InvalidOperationException("property subscriber");
        source.PropertyChanged += (_, _) => throw propertyError;
        source.CollectionChanged += (_, _) => throw new InvalidOperationException("collection subscriber");
        using var control = new ItemsControl { ItemsSource = source };
        var item = new object();

        Assert.Same(propertyError, Assert.Throws<InvalidOperationException>(() => source.Add(item)));
        Assert.Same(item, Assert.Single(source));
        Assert.Equal(1, control.ItemCount);
        Assert.Same(propertyError, Assert.Throws<InvalidOperationException>(source.Clear));
        Assert.Empty(source);
        Assert.Equal(0, control.ItemCount);
    }

    [Fact]
    public void DictionaryStillUpdatesAdkControlWhenEarlierSubscribersThrow()
    {
        var source = new ObservableDictionary<string, int>();
        var error = new InvalidOperationException("collection subscriber");
        source.CollectionChanged += (_, _) => throw error;
        using var control = new ItemsControl { ItemsSource = source };

        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => source.Add("key", 1)));
        Assert.Equal(1, control.ItemCount);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(source.Clear));
        Assert.Empty(source);
        Assert.Equal(0, control.ItemCount);
    }
}
