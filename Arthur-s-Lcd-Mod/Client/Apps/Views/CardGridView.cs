using Acrux.Controls;
using Adk.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.Views
{
    internal abstract class CardGridViewModel : ObservableObject
    {
        float _viewportWidth;
        int _columns = 1;
        float _cardHeight = 1f;

        public int Columns => _columns;
        public float CardHeight => _cardHeight;

        protected abstract float MinimumColumnWidth { get; }
        protected abstract float RowHeight { get; }
        protected abstract int CardCount { get; }

        public void SetViewportWidth(float width)
        {
            if (_viewportWidth == width)
                return;
            _viewportWidth = width;
            UpdateCardGrid();
        }

        protected void UpdateCardGrid()
        {
            float minimum = MinimumColumnWidth;
            int columns = minimum <= 0f ? 1 : UniformColumns.Calculate(_viewportWidth, minimum, CardCount);
            RaiseAndSetIfChanged(ref _columns, columns, nameof(Columns));
            RaiseAndSetIfChanged(ref _cardHeight, RowHeight, nameof(CardHeight));
        }
    }

    internal class CardGridView : UserControl
    {
        const string CARD_HEIGHT_RESOURCE = "cardHeight";

        float _cardHeight = float.NaN;

        // The height of the view's cards (the cardHeight resource their styles read). A view binds it to its view model
        // (CardHeight="{Binding CardHeight}"), so it follows any view model with that member, a preview clone too.
        public float CardHeight
        {
            get { return _cardHeight; }
            set
            {
                if (_cardHeight == value)
                    return;
                _cardHeight = value;
                Resources.Add(CARD_HEIGHT_RESOURCE, value);
            }
        }

        // The app's model also sizes its grid to the view (columns and card height follow); another view model keeps
        // its own values.
        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            (DataContext as CardGridViewModel)?.SetViewportWidth(availableSize.X);
            return base.MeasureOverride(availableSize);
        }
    }
}
