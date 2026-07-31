using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public class DependencyTreeNodeToggleButton : ToggleButton
    {
        protected override Type StyleKeyOverride => typeof(ToggleButton);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
                DataContext is ViewModels.DependencyTreeNode { IsFolder: true } node)
            {
                var view = this.FindAncestorOfType<DependenciesView>();
                view?.ToggleNodeIsExpanded(node);
            }

            e.Handled = true;
        }
    }

    public class DependencyTreeNodeIcon : UserControl
    {
        public static readonly DirectProperty<DependencyTreeNodeIcon, bool> IsExpandedProperty =
            AvaloniaProperty.RegisterDirect<DependencyTreeNodeIcon, bool>(
                nameof(IsExpanded),
                static o => o.IsExpanded,
                static (o, v) => o.IsExpanded = v);

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetAndRaise(IsExpandedProperty, ref _isExpanded, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == IsExpandedProperty)
                UpdateContent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            UpdateContent();
        }

        private void UpdateContent()
        {
            if (DataContext is not ViewModels.DependencyTreeNode node)
            {
                Content = null;
                return;
            }

            if (node.Dependency != null)
                CreateContent(new Thickness(0, 0, 0, 0), "Icons.Link");
            else if (node.IsExpanded)
                CreateContent(new Thickness(0, 2, 0, 0), "Icons.Folder.Open");
            else
                CreateContent(new Thickness(0, 2, 0, 0), "Icons.Folder");
        }

        private void CreateContent(Thickness margin, string iconKey)
        {
            if (this.FindResource(iconKey) is not StreamGeometry geo)
                return;

            Content = new Avalonia.Controls.Shapes.Path()
            {
                Width = 12,
                Height = 12,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = margin,
                Data = geo,
            };
        }

        private bool _isExpanded = false;
    }

    public partial class DependenciesView : UserControl
    {
        public static readonly RoutedEvent<RoutedEventArgs> RowsChangedEvent =
            RoutedEvent.Register<DependenciesView, RoutedEventArgs>(nameof(RowsChanged), RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        public event EventHandler<RoutedEventArgs> RowsChanged
        {
            add { AddHandler(RowsChangedEvent, value); }
            remove { RemoveHandler(RowsChangedEvent, value); }
        }

        public static readonly RoutedEvent<RoutedEventArgs> SearchRequestedEvent =
            RoutedEvent.Register<DependenciesView, RoutedEventArgs>(nameof(SearchRequested), RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        public event EventHandler<RoutedEventArgs> SearchRequested
        {
            add { AddHandler(SearchRequestedEvent, value); }
            remove { RemoveHandler(SearchRequestedEvent, value); }
        }

        public int Rows
        {
            get;
            private set;
        }

        public DependenciesView()
        {
            InitializeComponent();
        }

        public void ToggleNodeIsExpanded(ViewModels.DependencyTreeNode node)
        {
            if (Content is ViewModels.DependencyCollectionAsTree tree)
            {
                tree.ToggleExpand(node);
                Rows = tree.Rows.Count;
                RaiseEvent(new RoutedEventArgs(RowsChangedEvent));
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == ContentProperty)
            {
                if (Content is ViewModels.DependencyCollectionAsTree tree)
                    Rows = tree.Rows.Count;
                else if (Content is ViewModels.DependencyCollectionAsList list)
                    Rows = list.Dependencies.Count;
                else
                    Rows = 0;

                RaiseEvent(new RoutedEventArgs(RowsChangedEvent));
            }
            else if (change.Property == IsVisibleProperty)
            {
                RaiseEvent(new RoutedEventArgs(RowsChangedEvent));
            }
        }

        private void OnItemDoubleTapped(object sender, TappedEventArgs e)
        {
            if (sender is Control control && DataContext is ViewModels.Repository repo)
            {
                if (control.DataContext is ViewModels.DependencyTreeNode node)
                {
                    if (node.IsFolder)
                        ToggleNodeIsExpanded(node);
                    else if (node.Dependency.CanBeOpened)
                        repo.OpenDependency(node.Dependency);
                }
                else if (control.DataContext is Models.Dependency dep && dep.CanBeOpened)
                {
                    repo.OpenDependency(dep);
                }
            }

            e.Handled = true;
        }

        private void OnItemContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (sender is Control control && DataContext is ViewModels.Repository repo)
            {
                var dep = control.DataContext switch
                {
                    ViewModels.DependencyTreeNode node => node.Dependency,
                    Models.Dependency d => d,
                    _ => null,
                };

                if (dep != null)
                {
                    var open = new MenuItem();
                    open.Header = App.Text("Dependency.Open");
                    open.Icon = this.CreateMenuIcon("Icons.OpenAsStandalone");
                    open.IsEnabled = dep.CanBeOpened;
                    open.Click += (_, ev) =>
                    {
                        repo.OpenDependency(dep);
                        ev.Handled = true;
                    };

                    var explore = new MenuItem();
                    explore.Header = App.Text("Repository.Explore");
                    explore.Icon = this.CreateMenuIcon("Icons.Explore");
                    explore.IsEnabled = dep.Status != Models.DependencyStatus.NotCloned;
                    explore.Click += (_, ev) =>
                    {
                        Native.OS.OpenInFileManager(dep.FullPath);
                        ev.Handled = true;
                    };

                    // Only offered when the working copy drifted away from the recorded revision.
                    MenuItem reset = null;
                    if (dep.Status == Models.DependencyStatus.RevisionChanged)
                    {
                        reset = new MenuItem();
                        reset.Header = App.Text("Dependency.Reset");
                        reset.Icon = this.CreateMenuIcon("Icons.Reset");
                        reset.Click += (_, ev) =>
                        {
                            if (repo.CanCreatePopup())
                                repo.ShowPopup(new ViewModels.ResetDependency(repo, dep));
                            ev.Handled = true;
                        };
                    }

                    var copyName = new MenuItem();
                    copyName.Header = App.Text("Dependency.CopyName");
                    copyName.Icon = this.CreateMenuIcon("Icons.Folder");
                    copyName.Click += async (_, ev) =>
                    {
                        await this.CopyTextAsync(dep.Name);
                        ev.Handled = true;
                    };

                    var copySHA = new MenuItem();
                    copySHA.Header = App.Text("Dependency.Revision");
                    copySHA.Icon = this.CreateMenuIcon("Icons.Hash");
                    copySHA.Click += async (_, ev) =>
                    {
                        await this.CopyTextAsync(dep.RevHash);
                        ev.Handled = true;
                    };

                    var copyURL = new MenuItem();
                    copyURL.Header = App.Text("Dependency.URL");
                    copyURL.Icon = this.CreateMenuIcon("Icons.Link");
                    copyURL.Click += async (_, ev) =>
                    {
                        await this.CopyTextAsync(dep.URL);
                        ev.Handled = true;
                    };

                    var copy = new MenuItem();
                    copy.Header = App.Text("Copy");
                    copy.Icon = this.CreateMenuIcon("Icons.Copy");
                    copy.Items.Add(copyName);
                    copy.Items.Add(copySHA);
                    copy.Items.Add(copyURL);

                    var menu = new ContextMenu();
                    menu.Items.Add(open);
                    menu.Items.Add(explore);

                    if (reset != null)
                    {
                        menu.Items.Add(new MenuItem() { Header = "-" });
                        menu.Items.Add(reset);
                    }

                    menu.Items.Add(new MenuItem() { Header = "-" });
                    menu.Items.Add(copy);
                    menu.Open(control);
                }
            }

            e.Handled = true;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && e.KeyModifiers == (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control))
            {
                RaiseEvent(new RoutedEventArgs(SearchRequestedEvent));
                e.Handled = true;
                return;
            }
        }
    }
}
