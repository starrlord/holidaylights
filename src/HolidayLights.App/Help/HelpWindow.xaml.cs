using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.App.Shell;

namespace HolidayLights.App.Help;

/// <summary>
/// Holiday Lights Help (PRODUCT-SPEC 3.10): the Contents tree of the 5.4 books, a search over titles and text, Back and
/// Forward, and the topics with their links and "Open the &lt;page&gt; page" buttons. UI thread.
/// </summary>
public partial class HelpWindow : Window
{
    private const string LogSource = "Help";

    private readonly IAppServices services;
    private readonly HelpContents contents;
    private readonly HelpHistory history = new();
    private readonly HelpDocumentBuilder builder;
    private readonly Lazy<HelpSearchIndex> index;
    private readonly Dictionary<string, TreeViewItem> topicItems = new(StringComparer.Ordinal);
    private readonly ImageSource? banner;
    private bool syncingTree;

    /// <summary>Creates the window (it shows nothing until <see cref="Navigate"/>).</summary>
    /// <param name="services">The services (Settings pages, the log).</param>
    /// <param name="contents">The books and topics.</param>
    public HelpWindow(IAppServices services, HelpContents contents)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(contents);
        this.services = services;
        this.contents = contents;
        InitializeComponent();
        builder = new HelpDocumentBuilder(Navigate, page => services.SettingsWindow.Show(page), HelpDocumentBuilder.LoadEmbeddedImage);
        index = new Lazy<HelpSearchIndex>(BuildIndex);
        banner = LoadBanner();
        BuildContents();
    }

    /// <summary>The topic shown, or null before the first navigation.</summary>
    public string? CurrentTopic => history.Current;

    /// <summary>Shows a topic and records it for Back (an unknown id shows "Welcome to Holiday Lights").</summary>
    /// <param name="topicId">A <see cref="HelpTopics"/> id.</param>
    public void Navigate(string topicId)
    {
        ArgumentException.ThrowIfNullOrEmpty(topicId);
        if (!topicItems.ContainsKey(topicId))
        {
            services.Log.Warn(LogSource, $"Unknown help topic \"{topicId}\".");
            topicId = HelpTopics.Welcome;
        }

        history.Navigate(topicId);
        ShowTopic(topicId);
    }

    private static ImageSource? LoadBanner()
    {
        try
        {
            var image = new BitmapImage(AppAssetUris.Resolve(AppAssets.HelpBanner));
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or UriFormatException or NotSupportedException)
        {
            return null;
        }
    }

    private HelpSearchIndex BuildIndex() => new(contents.Books
        .SelectMany(book => book.Topics)
        .Select(topic => (topic.Id, topic.Title, MarkdownParser.Parse(HelpContentStore.ReadTopicMarkdown(topic.Id)).ToPlainText())));

    private void BuildContents()
    {
        foreach (HelpBook book in contents.Books)
        {
            var bookItem = new TreeViewItem { Header = book.Title };
            foreach (HelpTopicEntry topic in book.Topics)
            {
                var topicItem = new TreeViewItem { Header = topic.Title, Tag = topic.Id };
                topicItems[topic.Id] = topicItem;
                bookItem.Items.Add(topicItem);
            }

            ContentsTree.Items.Add(bookItem);
        }
    }

    private void ShowTopic(string topicId)
    {
        MarkdownDocument topic = MarkdownParser.Parse(HelpContentStore.ReadTopicMarkdown(topicId));
        TopicViewer.Document = builder.Build(topic, banner);
        SelectInTree(topicId);
        CommandManager.InvalidateRequerySuggested();
    }

    private void SelectInTree(string topicId)
    {
        if (!topicItems.TryGetValue(topicId, out TreeViewItem? item))
        {
            return;
        }

        syncingTree = true;
        try
        {
            if (item.Parent is TreeViewItem book)
            {
                book.IsExpanded = true;
            }

            item.IsSelected = true;
            item.BringIntoView();
        }
        finally
        {
            syncingTree = false;
        }
    }

    private void OnContentsSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (!syncingTree && e.NewValue is TreeViewItem { Tag: string topicId })
        {
            Navigate(topicId);
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchBox.Text.Trim();
        bool searching = query.Length > 0;
        ContentsTree.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        ResultsList.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        ResultsList.ItemsSource = searching
            ? index.Value.Search(query).Select(r => new ListBoxItem
            {
                Content = r.Snippet.Length == 0 ? r.Title : $"{r.Title}\n{r.Snippet}",
                Tag = r.TopicId,
                ToolTip = r.Snippet.Length == 0 ? null : r.Snippet,
            }).ToList()
            : null;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SearchBox.Text.Length > 0)
        {
            SearchBox.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && ResultsList.Items.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.Key == Key.Down && ResultsList.Items.Count > 0)
        {
            ResultsList.Focus();
            e.Handled = true;
        }
    }

    private void OnResultSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is ListBoxItem { Tag: string topicId })
        {
            Navigate(topicId);
        }
    }

    private void OnBack(object sender, ExecutedRoutedEventArgs e)
    {
        if (history.Back() is { } topicId)
        {
            ShowTopic(topicId);
        }
    }

    private void OnForward(object sender, ExecutedRoutedEventArgs e)
    {
        if (history.Forward() is { } topicId)
        {
            ShowTopic(topicId);
        }
    }

    private void CanGoBack(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = history.CanGoBack;

    private void CanGoForward(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = history.CanGoForward;

    private void OnFind(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}
