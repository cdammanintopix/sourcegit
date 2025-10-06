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
        public static readonly StyledProperty<List<Models.Remote>> RemotesProperty =
            AvaloniaProperty.Register<CI, List<Models.Remote>>(nameof(Remotes));

        public static readonly StyledProperty<string> SHAProperty =
            AvaloniaProperty.Register<CI, string>("SHA");

        public static readonly StyledProperty<bool> ShowDefaultIconIfNullProperty =
            AvaloniaProperty.Register<CI, bool>("ShowDefaultIconIfNull");

        public static readonly StyledProperty<Button> ButtonProperty =
            AvaloniaProperty.Register<CI, Button>("Button");

        public List<Models.Remote> Remotes
        {
            get => GetValue(RemotesProperty);
            set => SetValue(RemotesProperty, value);
        }

        public string SHA
        {
            get => GetValue(SHAProperty);
            set => SetValue(SHAProperty, value);
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
            if (Remotes == null || SHA == null)
                return;

            var corner = (float)Math.Max(2, Bounds.Width / 16);
            var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
            var clip = context.PushClip(new RoundedRect(rect, corner));

            if (img != null)
            {
                context.DrawImage(img, rect);
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

        public void OnCIResourceChanged(string req, Bitmap image)
        {
            if (req.Equals(GetReq(Remotes, SHA), StringComparison.Ordinal))
            {
                img = image;
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

            if (change.Property == RemotesProperty || change.Property == SHAProperty)
            {
                var req = GetReq(Remotes, SHA);
                if (string.IsNullOrEmpty(req))
                    return;

                img = Models.CIManager.Instance.Request(req, false);
                InvalidateVisual();
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

            var refetch = new MenuItem();
            refetch.Icon = App.CreateMenuIcon("Icons.Loading");
            refetch.Header = App.Text("CI.Refetch");
            refetch.Click += (_, ev) =>
            {
                var req = GetReq(Remotes, SHA);
                if (!string.IsNullOrEmpty(req))
                    Models.CIManager.Instance.Request(req, true);

                ev.Handled = true;
            };

            var menu = new ContextMenu();
            menu.Items.Add(refetch);

            menu.Open(this);
        }

        private Bitmap _img = null;
        private Bitmap img
        {
            get { return _img; }
            set { _img = value; if (Button != null) { Button.IsEnabled = _img != null; } }
        }
    }
}
