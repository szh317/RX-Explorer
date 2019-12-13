﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace FileManager
{
    public sealed partial class SearchPage : Page
    {
        public ObservableCollection<FileSystemStorageItem> SearchResult;
        StorageItemQueryResult ItemQuery;
        CancellationTokenSource Cancellation;

        public static SearchPage ThisPage { get; private set; }

        public QueryOptions SetSearchTarget
        {
            set
            {
                SearchResult.Clear();

                ItemQuery.ApplyNewQueryOptions(value);

                SearchPage_Loaded(null, null);
            }
        }

        public SearchPage()
        {
            InitializeComponent();
            ThisPage = this;
            SearchResult = new ObservableCollection<FileSystemStorageItem>();
            SearchResultList.ItemsSource = SearchResult;
        }

        private async void SearchPage_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= SearchPage_Loaded;

            HasItem.Visibility = Visibility.Collapsed;

            LoadingControl.IsLoading = true;
            IReadOnlyList<IStorageItem> SearchItems = null;

            try
            {
                Cancellation = new CancellationTokenSource();
                Cancellation.Token.Register(() =>
                {
                    HasItem.Visibility = Visibility.Visible;
                    SearchResultList.Visibility = Visibility.Collapsed;
                    LoadingControl.IsLoading = false;
                });

                IAsyncOperation<IReadOnlyList<IStorageItem>> SearchAsync = ItemQuery.GetItemsAsync(0, 100);

                SearchItems = await SearchAsync.AsTask(Cancellation.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            finally
            {
                Cancellation?.Dispose();
                Cancellation = null;
            }

            await Task.Delay(500);

            LoadingControl.IsLoading = false;

            if (SearchItems.Count == 0)
            {
                HasItem.Visibility = Visibility.Visible;
            }
            else
            {
                foreach (var Item in SearchItems)
                {
                    var Size = await Item.GetSizeDescriptionAsync();
                    var Thumbnail = await Item.GetThumbnailBitmapAsync() ?? new BitmapImage(new Uri("ms-appx:///Assets/DocIcon.png"));
                    var ModifiedTime = await Item.GetModifiedTimeAsync();

                    SearchResult.Add(new FileSystemStorageItem(Item, Size, Thumbnail, ModifiedTime));
                }
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            ItemQuery = e.Parameter as StorageItemQueryResult;
            Loaded += SearchPage_Loaded;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            SearchResult.Clear();
            Cancellation?.Cancel();
        }

        private async void Location_Click(object sender, RoutedEventArgs e)
        {
            var RemoveFile = SearchResultList.SelectedItem as FileSystemStorageItem;

            if (RemoveFile.ContentType == ContentType.Folder)
            {
                var RootNode = FileControl.ThisPage.FolderTree.RootNodes[0];
                TreeViewNode TargetNode = await FindFolderLocationInTree(RootNode, new PathAnalysis(RemoveFile.Folder.Path, (RootNode.Content as StorageFolder).Path));
                if (TargetNode == null)
                {
                    if (Globalization.Language == LanguageEnum.Chinese)
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = "错误",
                            Content = "无法定位文件夹，该文件夹可能已被删除或移动",
                            CloseButtonText = "确定"
                        };
                        _ = await dialog.ShowAsync();
                    }
                    else
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = "Error",
                            Content = "Unable to locate folder, which may have been deleted or moved",
                            CloseButtonText = "Confirm"
                        };
                        _ = await dialog.ShowAsync();
                    }
                }
                else
                {
                    while (true)
                    {
                        if (FileControl.ThisPage.FolderTree.ContainerFromNode(TargetNode) is TreeViewItem Item)
                        {
                            Item.IsSelected = true;
                            Item.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, VerticalAlignmentRatio = 0.5 });
                            await FileControl.ThisPage.DisplayItemsInFolder(TargetNode);
                            break;
                        }
                        else
                        {
                            await Task.Delay(300);
                        }
                    }
                }
            }
            else
            {
                try
                {
                    _ = await StorageFile.GetFileFromPathAsync(RemoveFile.Path);

                    var RootNode = FileControl.ThisPage.FolderTree.RootNodes[0];
                    var CurrentNode = await FindFolderLocationInTree(RootNode, new PathAnalysis((await RemoveFile.File.GetParentAsync()).Path, (RootNode.Content as StorageFolder).Path));

                    var Container = FileControl.ThisPage.FolderTree.ContainerFromNode(CurrentNode) as TreeViewItem;
                    Container.IsSelected = true;
                    Container.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, VerticalAlignmentRatio = 0.5 });

                    await FileControl.ThisPage.DisplayItemsInFolder(CurrentNode);
                }
                catch (FileNotFoundException)
                {
                    if (Globalization.Language == LanguageEnum.Chinese)
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = "错误",
                            Content = "无法定位文件，该文件可能已被删除或移动",
                            CloseButtonText = "确定"
                        };
                        _ = await dialog.ShowAsync();
                    }
                    else
                    {
                        QueueContentDialog dialog = new QueueContentDialog
                        {
                            Title = "Error",
                            Content = "Unable to locate file, which may have been deleted or moved",
                            CloseButtonText = "Confirm"
                        };
                        _ = await dialog.ShowAsync();
                    }
                }
            }
        }

        private async void Attribute_Click(object sender, RoutedEventArgs e)
        {
            FileSystemStorageItem Device = SearchResultList.SelectedItems.FirstOrDefault() as FileSystemStorageItem;
            if (Device.File != null)
            {
                AttributeDialog Dialog = new AttributeDialog(Device.File);
                _ = await Dialog.ShowAsync();
            }
            else if (Device.Folder != null)
            {
                AttributeDialog Dialog = new AttributeDialog(Device.Folder);
                _ = await Dialog.ShowAsync();
            }
        }

        private async Task<TreeViewNode> FindFolderLocationInTree(TreeViewNode Node, PathAnalysis Analysis)
        {
            if (Node.HasUnrealizedChildren && !Node.IsExpanded)
            {
                Node.IsExpanded = true;
            }

            string NextPathLevel = Analysis.NextPathLevel();

            if (NextPathLevel == Analysis.FullPath)
            {
                if ((Node.Content as StorageFolder).Path == NextPathLevel)
                {
                    return Node;
                }
                else
                {
                    while (true)
                    {
                        var TargetNode = Node.Children.Where((SubNode) => (SubNode.Content as StorageFolder).Path == NextPathLevel).FirstOrDefault();
                        if (TargetNode != null)
                        {
                            return TargetNode;
                        }
                        else
                        {
                            await Task.Delay(500);
                        }
                    }
                }
            }
            else
            {
                if ((Node.Content as StorageFolder).Path == NextPathLevel)
                {
                    return await FindFolderLocationInTree(Node, Analysis);
                }
                else
                {
                    while (true)
                    {
                        var TargetNode = Node.Children.Where((SubNode) => (SubNode.Content as StorageFolder).Path == NextPathLevel).FirstOrDefault();
                        if (TargetNode != null)
                        {
                            return await FindFolderLocationInTree(TargetNode, Analysis);
                        }
                        else
                        {
                            await Task.Delay(500);
                        }
                    }
                }
            }
        }

        private void SearchResultList_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            FileSystemStorageItem Context = (e.OriginalSource as FrameworkElement)?.DataContext as FileSystemStorageItem;
            SearchResultList.SelectedIndex = SearchResult.IndexOf(Context);
            e.Handled = true;
        }

        private void SearchResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Location.IsEnabled = SearchResultList.SelectedIndex != -1;
        }
    }
}
