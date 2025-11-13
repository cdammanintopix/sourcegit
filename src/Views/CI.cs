using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SourceGit.Views
{
    public class CI : Control, Models.ICIHost
    {
        public static readonly StyledProperty<ViewModels.Repository> RepositoryProperty =
            AvaloniaProperty.Register<CI, ViewModels.Repository>(nameof(Repository));

        public static readonly StyledProperty<Models.Commit> CommitProperty =
            AvaloniaProperty.Register<CI, Models.Commit>("Commit");

        public static readonly StyledProperty<bool> ShowDefaultIconIfNullProperty =
            AvaloniaProperty.Register<CI, bool>("ShowDefaultIconIfNull");

        public static readonly StyledProperty<Button> ButtonProperty =
            AvaloniaProperty.Register<CI, Button>("Button");

        public ViewModels.Repository Repository
        {
            get => GetValue(RepositoryProperty);
            set => SetValue(RepositoryProperty, value);
        }

        public Models.Commit Commit
        {
            get => GetValue(CommitProperty);
            set => SetValue(CommitProperty, value);
        }

        public bool ShowDefaultIconIfNull
        {
            get => GetValue(ShowDefaultIconIfNullProperty);
            set => SetValue(ShowDefaultIconIfNullProperty, value);
        }

        public Button Button
        {
            get => GetValue(ButtonProperty);
            set => SetValue(ButtonProperty, value);
        }

        public CI()
        {
            RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        }

        public override void Render(DrawingContext context)
        {
            if (Repository == null || Repository.Remotes == null || Commit == null)
                return;

            var corner = (float)Math.Max(2, Bounds.Width / 16);
            var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
            var clip = context.PushClip(new RoundedRect(rect, corner));

            if (!string.IsNullOrEmpty(status))
            {
                context.DrawImage(new Bitmap(AssetLoader.Open(new Uri($"avares://SourceGit/Resources/Images/{status}.png", UriKind.RelativeOrAbsolute))), rect);
            }
            else if (ShowDefaultIconIfNull)
            {
                context.DrawImage(new Bitmap(AssetLoader.Open(new Uri($"avares://SourceGit/Resources/Images/gitlab.png", UriKind.RelativeOrAbsolute))), rect);
            }

            clip.Dispose();
        }

        public static string GetReq(List<Models.Remote> remotes, string sha) {
            if (remotes == null || sha == null)
            {
                return "";
            }
            foreach (var remote in remotes)
            {
                if (remote.TryGetVisitURL(out var link))
                {
                    if (link.EndsWith(".git"))
                        link = link.Substring(0, link.Length - 4);

                    var uri = new Uri(link, UriKind.Absolute);
                    var host = uri.Host;
                    var route = uri.AbsolutePath.TrimStart('/');

                    if (host.Contains("gitlab.intopix.com", StringComparison.Ordinal))
                    {
                        return route + '/' + sha;
                    }
                }
            }
            return "";
        }

        public static void QueueForNextRefresh(List<Models.Remote> remotes, string sha)
        {
            var req = GetReq(remotes, sha);
            if (!string.IsNullOrEmpty(req))
                Models.CIManager.Instance.QueueForNextRefresh(req);
        }

        public static void Refresh(List<Models.Remote> remotes, string sha)
        {
            var req = GetReq(remotes, sha);
            if (!string.IsNullOrEmpty(req))
                Models.CIManager.Instance.Request(req, true);
        }

        public static void Clear(List<Models.Remote> remotes)
        {
            var req = GetReq(remotes, "");
            if (!string.IsNullOrEmpty(req))
                Models.CIManager.Instance.Clear(req);
        }

        public void OnCIResourceChanged(string req, string status_)
        {
            if (req.Equals(GetReq(Repository.Remotes, Commit.SHA), StringComparison.Ordinal))
            {
                status = status_;
                InvalidateVisual();
            }
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            Models.CIManager.Instance.Subscribe(this);
            ContextRequested += OnContextRequested;
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);
            ContextRequested -= OnContextRequested;
            Models.CIManager.Instance.Unsubscribe(this);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == RepositoryProperty || change.Property == CommitProperty)
            {
                if (Repository != null && Commit != null)
                {
                    var req = GetReq(Repository.Remotes, Commit.SHA);
                    if (string.IsNullOrEmpty(req))
                        return;

                    status = Models.CIManager.Instance.Request(req, false);
                    InvalidateVisual();
                }
            }
        }

        private void OnContextRequested(object sender, ContextRequestedEventArgs e)
        {
            var toplevel = TopLevel.GetTopLevel(this);
            if (toplevel == null)
            {
                e.Handled = true;
                return;
            }

            var menu = new ContextMenu();

            var pipeline = new MenuItem();
            pipeline.Icon = App.CreateMenuIcon("Icons.Action");
            pipeline.Header = App.Text("CI.NewPipeline");
            pipeline.Click += (_, e) =>
            {
                Repository.RunCIPipeline(Commit);
                e.Handled = true;
            };
            menu.Items.Add(pipeline);

            var refetch = new MenuItem();
            refetch.Icon = App.CreateMenuIcon("Icons.Loading");
            refetch.Header = App.Text("CI.Refetch");
            refetch.Click += (_, ev) =>
            {
                Refresh(Repository.Remotes, Commit.SHA);

                ev.Handled = true;
            };
            menu.Items.Add(refetch);

            menu.Open(this);
        }

        private string _status = null;
        private string status
        {
            get { return _status; }
            set { _status = value; if (Button != null) { Button.IsEnabled = _status != null; } }
        }
    }
}
