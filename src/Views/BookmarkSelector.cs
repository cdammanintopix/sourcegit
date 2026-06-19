using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace SourceGit.Views
{
    public class BookmarkSelector : Control
    {
        public static readonly StyledProperty<int> BookmarkProperty =
            AvaloniaProperty.Register<BookmarkSelector, int>(nameof(Bookmark), 0);

        public int Bookmark
        {
            get => GetValue(BookmarkProperty);
            set => SetValue(BookmarkProperty, value);
        }

        public BookmarkSelector()
        {
            var geo = App.Current.FindResource("Icons.Bookmark") as StreamGeometry;
            _icon = geo!.Clone();
            var iconBounds = _icon.Bounds;
            var translation = Matrix.CreateTranslation(-(Vector)iconBounds.Position);
            var scale = Math.Min(14.0 / iconBounds.Width, 14.0 / iconBounds.Height);
            var transform = translation * Matrix.CreateScale(scale, scale);
            if (_icon.Transform == null || _icon.Transform.Value == Matrix.Identity)
                _icon.Transform = new MatrixTransform(transform);
            else
                _icon.Transform = new MatrixTransform(_icon.Transform.Value * transform);

            var x = 2.0;
            for (var i = 0; i < Models.Bookmarks.Brushes.Length; i++)
            {
                var hitBox = new Rect(x - 2.5, 2.5, 18, 20);
                _hitBoxes.Add(hitBox);
                x += 26;
            }
        }

        public override void Render(DrawingContext context)
        {
            // Just enable clicking anywhere in the control.
            context.FillRectangle(Brushes.Transparent, new Rect(0, 0, Bounds.Width, Bounds.Height));

            var defaultBrush = this.FindResource("Brush.FG1") as IBrush;
            var selectedBorder = new Pen(new SolidColorBrush((Color)this.FindResource("SystemAccentColor")), 1);
            var active = Bookmark;

            for (var i = 0; i < _hitBoxes.Count; i++)
            {
                var hitBox = _hitBoxes[i];
                if (i == active)
                    context.DrawRectangle(selectedBorder, hitBox, 3);

                var bursh = Models.Bookmarks.Get(i) ?? defaultBrush;
                using (context.PushTransform(Matrix.CreateTranslation(hitBox.X + 3, 5)))
                    context.DrawGeometry(bursh, null, _icon);
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == BookmarkProperty || change.Property.Name.Equals(nameof(ActualThemeVariant), StringComparison.Ordinal))
                InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                var pos = e.GetPosition(this);
                for (var i = 0; i < _hitBoxes.Count; i++)
                {
                    if (_hitBoxes[i].Contains(pos))
                    {
                        SetCurrentValue(BookmarkProperty, i);
                        break;
                    }
                }

                e.Handled = true;
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(8 * 14 + 7 * 12 + 4, 24);
        }

        private Geometry _icon = null;
        private List<Rect> _hitBoxes = [];
    }
}
