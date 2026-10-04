using Adk.Gui.Controls;
using LcdMod.Common.Mvvm;
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

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var viewModel = DataContext as CardGridViewModel;
            if (viewModel != null)
            {
                viewModel.SetViewportWidth(availableSize.X);
                float current;
                if (!Resources.TryGetValue(CARD_HEIGHT_RESOURCE, out current) || current != viewModel.CardHeight)
                    Resources.Add(CARD_HEIGHT_RESOURCE, viewModel.CardHeight);
            }
            return base.MeasureOverride(availableSize);
        }
    }
}
