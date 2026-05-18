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
            var corner = (float)Math.Max(2, Bounds.Width / 16);
            var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
            var clip = context.PushClip(new RoundedRect(rect, corner));
            
            bool drawn = false;
            if (!string.IsNullOrEmpty(Status)) {
                try {
                    context.DrawImage(new Bitmap(AssetLoader.Open(new Uri($"avares://SourceGit/Resources/Images/GitLabCI/{Status}.png", UriKind.RelativeOrAbsolute))), rect);
                    drawn = true;
                } catch (System.IO.FileNotFoundException) {}
            }
            if (!drawn && ShowDefaultIconIfNull) {
                context.DrawImage(new Bitmap(AssetLoader.Open(new Uri($"avares://SourceGit/Resources/Images/GitLabCI/gitlab.png", UriKind.RelativeOrAbsolute))), rect);
            }

            clip.Dispose();
        }

        public static string GetGitlabRoute(List<Models.Remote> remotes)
        {
            if (remotes == null)
            {
                return null;
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
                        return route;
                    }
                }
            }
            return null;
        }

        public static string GetReq(string route, string sha)
        {
            if (string.IsNullOrEmpty(route) || string.IsNullOrEmpty(sha))
            {
                return "";
            }
            return route + '/' + sha;
        }

        public static string GetReq(List<Models.Remote> remotes, string sha)
        {
            if (remotes == null || string.IsNullOrEmpty(sha))
            {
                return "";
            }
            return GetReq(GetGitlabRoute(remotes), sha);
        }

        public static (string, string) GetRouteSha(string req)
        {
            string route = req[..req.LastIndexOf("/")];
            string sha = req[(req.LastIndexOf("/") + 1)..];
            return (route, sha);
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

        public static void CancelPipeline(List<Models.Remote> remotes, string sha)
        {
            var route = GetGitlabRoute(remotes);
            if (!string.IsNullOrEmpty(route))
                _ = Models.CIManager.Instance.CancelPipelinesForSha(route, sha);
        }

        public static void Clear(List<Models.Remote> remotes)
        {
            var req = GetReq(remotes, "");
            if (!string.IsNullOrEmpty(req))
                Models.CIManager.Instance.Clear(req);
        }

        public void OnCIResourceChanged(string req, string status)
        {
            if (!string.IsNullOrEmpty(Req) && req.Equals(Req, StringComparison.Ordinal))
            {
                Status = status;
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
                    Req = GetReq(Repository.Remotes, Commit.SHA);
                    if (string.IsNullOrEmpty(Req))
                        return;

                    Status = Models.CIManager.Instance.Request(Req, false);
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
            pipeline.Icon = this.CreateMenuIcon("Icons.Action");
            pipeline.Header = App.Text("CI.NewPipeline");
            pipeline.Click += (_, e) =>
            {
                Repository.RunCIPipeline(Commit);
                e.Handled = true;
            };
            menu.Items.Add(pipeline);

            var cancel = new MenuItem();
            cancel.Icon = this.CreateMenuIcon("Icons.Close");
            cancel.Header = App.Text("CI.CancelPipeline");
            cancel.Click += (_, e) =>
            {
                if (!string.IsNullOrEmpty(Req))
                {
                    (string route, string sha) = GetRouteSha(Req);
                    _ = Models.CIManager.Instance.CancelPipelinesForSha(route, sha);
                }
                e.Handled = true;
            };
            menu.Items.Add(cancel);

            var refetch = new MenuItem();
            refetch.Icon = this.CreateMenuIcon("Icons.Loading");
            refetch.Header = App.Text("CI.Refetch");
            refetch.Click += (_, ev) =>
            {
                if (!string.IsNullOrEmpty(Req))
                    Models.CIManager.Instance.Request(Req, true);

                ev.Handled = true;
            };
            menu.Items.Add(refetch);

            var link = new MenuItem();
            link.Icon = this.CreateMenuIcon("Icons.Link");
            link.Header = App.Text("IssueLinkCM.CopyLink");
            link.Click += (_, ev) =>
            {
                (string route, string sha) = CI.GetRouteSha(Req);
                _ = this.CopyTextAsync($"https://gitlab.intopix.com/{route}/-/commit/{sha}/pipelines");
                ev.Handled = true;
            };
            menu.Items.Add(link);

            menu.Open(this);
        }

        private string Req = null;
        private string _status = null;
        private string Status
        {
            get { return _status; }
            set { _status = value; if (Button != null) { Button.IsEnabled = _status != null; } }
        }
    }
}
