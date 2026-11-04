using System;
using System.Collections.Generic;
using System.Globalization;
using Acrux.Core.Input;
using LcdMod.Client.Apps.Dialogs;
using Adk.Mvvm;

namespace LcdMod.Client.Apps.Inventory
{
    /// <summary>
    /// Queues items on the selected assemblers. With one request the amount field is the item count; with several
    /// it is a multiplier applied to every request's amount.
    /// </summary>
    [PreviewWith("Title", "Craft")]
    [PreviewWith("SmallFontSize", 0.5525f)]
    [PreviewWith("IsRegular", true)]
    [PreviewWith("IsCompact", false)]
    [PreviewWith("FontSize", 0.65f)]
    internal sealed class CraftDialogViewModel : DialogViewModel
    {
        const int MAXIMUM_AMOUNT = 1000000;
        const int MAXIMUM_MULTIPLIER = 10000;

        readonly List<CraftRequestModel> _requests = new List<CraftRequestModel>();
        string _amount;
        bool _submitting;
        public readonly ObservableList<CraftAssemblerModel> Assemblers = new ObservableList<CraftAssemblerModel>();

        public CraftDialogViewModel(double amount)
            : this(new[] { new CraftRequestModel(null, string.Empty, null, amount) }, 1)
        {
        }

        CraftDialogViewModel(IEnumerable<CraftRequestModel> requests, int multiplier)
        {
            _requests.AddRange(requests);
            foreach (var request in _requests)
                request.Owner = this;
            IsMultiple = _requests.Count > 1;
            _amount = IsMultiple
                ? Clamp(multiplier, MAXIMUM_MULTIPLIER).ToString(CultureInfo.InvariantCulture)
                : Clamp((double)_requests[0].Amount * multiplier, MAXIMUM_AMOUNT).ToString(CultureInfo.InvariantCulture);
            Title = Loc(IsMultiple ? "LcdMod_CraftDialog_CraftAll" : "LcdMod_CraftDialog_Title");
            AmountLabel = Loc(IsMultiple ? "LcdMod_CraftDialog_Multiplier" : "LcdMod_CraftDialog_Amount");
            IncreaseCommand = new DelegateCommand(_ => AdjustAmount(1));
            DecreaseCommand = new DelegateCommand(_ => AdjustAmount(-1));
            CraftCommand = new DelegateCommand(_ => Craft(), _ => CanCraft);
            RefreshRequestTotals();
        }

        public static CraftDialogViewModel ForRequests(IEnumerable<CraftRequestModel> requests, int multiplier = 1)
        {
            return new CraftDialogViewModel(requests, multiplier);
        }

        public bool IsMultiple { get; }
        [PreviewWith(true)]
        public bool IsSingle => !IsMultiple;
        [PreviewWith(1)]
        public IReadOnlyList<CraftRequestModel> Requests => _requests;
        [PreviewWith(4)]
        public IEnumerable<CraftAssemblerModel> AssemblerItems => Assemblers;
        [PreviewWith("Steel Plate")]
        public string ItemName
        {
            get { return _requests[0].Name; }
            set { _requests[0].Name = value; }
        }
        [PreviewWith("MyObjectBuilder_Component/SteelPlate")]
        public string ItemIcon
        {
            get { return _requests[0].Icon; }
            set { _requests[0].Icon = value; }
        }
        [PreviewWith("Amount")]
        public string AmountLabel { get; set; }
        public bool IsEmpty => Assemblers.Count == 0;
        public ICommand IncreaseCommand { get; }
        public ICommand DecreaseCommand { get; }
        public ICommand CraftCommand { get; }
        public ICommand CancelCommand => CloseCommand;
        public Action<Exception> Failed { get; set; }

        [PreviewWith("250")]
        public string Amount
        {
            get { return _amount; }
            set
            {
                if (!RaiseAndSetIfChanged(ref _amount, value, nameof(Amount))) return;
                RefreshRequestTotals();
                RaisePropertyChanged<bool>(nameof(CanCraft));
            }
        }

        [PreviewWith(true)]
        public bool CanCraft
        {
            get
            {
                int amount;
                if (_submitting || !TryGetAmount(out amount)) return false;
                foreach (var assembler in Assemblers)
                    if (assembler.IsSelected) return true;
                return false;
            }
        }

        int Maximum => IsMultiple ? MAXIMUM_MULTIPLIER : MAXIMUM_AMOUNT;

        bool TryGetAmount(out int amount)
        {
            return int.TryParse(Amount, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount) &&
                   amount >= 1 && amount <= Maximum;
        }

        void AdjustAmount(int delta)
        {
            int amount;
            if (!TryGetAmount(out amount)) amount = 1;
            Amount = Math.Max(1, Math.Min(Maximum, amount + delta)).ToString(CultureInfo.InvariantCulture);
        }

        void RefreshRequestTotals()
        {
            if (!IsMultiple) return;
            int multiplier;
            if (!TryGetAmount(out multiplier)) multiplier = 1;
            foreach (var request in _requests)
                request.SetMultiplier(multiplier);
        }

        protected override void OnFontSizeChanged()
        {
            foreach (var request in _requests)
                request.RaisePropertyChanged<float>(nameof(CraftRequestModel.FontSize));
            foreach (var assembler in Assemblers)
                assembler.RaisePropertyChanged<float>(nameof(CraftAssemblerModel.FontSize));
        }

        public void SetAssemblers(IList<CraftAssemblerModel> items)
        {
            foreach (var item in Assemblers) item.PropertyChanged -= OnSelectionChanged;
            Assemblers.Clear();
            foreach (var item in items)
            {
                item.Owner = this;
                item.PropertyChanged += OnSelectionChanged;
                Assemblers.Add(item);
            }
            RaisePropertyChanged<bool>(nameof(IsEmpty));
            RaisePropertyChanged<bool>(nameof(CanCraft));
        }

        void OnSelectionChanged(ObservableObject sender, string name)
        {
            if (name == nameof(CraftAssemblerModel.IsSelected)) RaisePropertyChanged<bool>(nameof(CanCraft));
        }

        void Craft()
        {
            int amount;
            if (!CanCraft || !TryGetAmount(out amount)) return;
            _submitting = true;
            RaisePropertyChanged<bool>(nameof(CanCraft));
            try
            {
                foreach (var request in _requests)
                    Queue(request, IsMultiple ? (int)Math.Min(int.MaxValue, (long)request.Amount * amount) : amount);
                Close();
            }
            catch (Exception error)
            {
                // Some assemblers may already have accepted their share. Close
                // rather than leaving an enabled retry that could duplicate it.
                if (Failed != null) Failed(error);
                Close();
            }
        }

        void Queue(CraftRequestModel request, int total)
        {
            var capable = new List<CraftAssemblerModel>();
            foreach (var assembler in Assemblers)
                if (assembler.IsSelected && assembler.CanCraft(request))
                    capable.Add(assembler);
            for (int i = 0; i < capable.Count; i++)
            {
                int share = total / capable.Count + (i < total % capable.Count ? 1 : 0);
                if (share > 0) capable[i].Queue(request, share);
            }
        }

        static int Clamp(double value, int maximum)
        {
            return (int)Math.Max(1, Math.Min(maximum, Math.Ceiling(value)));
        }
    }

    internal sealed class CraftRequestModel : ObservableObject
    {
        string _totalText = string.Empty;

        public CraftRequestModel(object key, string name, string icon, double amount)
        {
            Key = key;
            Name = name ?? string.Empty;
            Icon = icon;
            Amount = (int)Math.Max(1, Math.Min(int.MaxValue, Math.Ceiling(amount)));
            SetMultiplier(1);
        }

        [PreviewWith("SteelPlate")]
        public object Key { get; }
        [PreviewWith("Steel Plate")]
        public string Name { get; set; }
        [PreviewWith("MyObjectBuilder_Component/SteelPlate")]
        public string Icon { get; set; }
        [PreviewWith(250)]
        public int Amount { get; }
        [PreviewWith("×250")]
        public string TotalText => _totalText;
        internal CraftDialogViewModel Owner { get; set; }
        [PreviewWith(0.65f)]
        public float FontSize => Owner == null ? .65f : Owner.FontSize;

        internal void SetMultiplier(int multiplier)
        {
            long total = (long)Amount * Math.Max(1, multiplier);
            RaiseAndSetIfChanged(ref _totalText, "×" + total.ToString("N0", CultureInfo.CurrentCulture), nameof(TotalText));
        }
    }

    internal sealed class CraftAssemblerModel : ObservableObject
    {
        bool _selected;
        string _label;
        [PreviewWith(1L, 2L, 3L, 4L)]
        public long Id { get; set; }
        [PreviewWith(true, false, true, false)]
        public bool Idle { get; set; }
        [PreviewWith(1f, 2f, 0.5f, 0.2f)]
        public float Speed { get; set; }
        public Func<CraftRequestModel, bool> CanCraft { get; set; } = _ => true;
        public Action<CraftRequestModel, int> Queue { get; set; }
        internal CraftDialogViewModel Owner { get; set; }
        [PreviewWith(0.5525f)]
        public float FontSize => Owner == null ? .65f : Owner.SmallFontSize;
        [PreviewWith("Assembler (Main) (Idle) x1", "Industrial Assembler x2", "Basic Assembler (Idle) x0.5", "Survival Kit x0.2")]
        public string Label
        {
            get { return _label; }
            set { RaiseAndSetIfChanged(ref _label, value, nameof(Label)); }
        }
        [PreviewWith(true, false, false, false)]
        public bool IsSelected
        {
            get { return _selected; }
            set { RaiseAndSetIfChanged(ref _selected, value, nameof(IsSelected)); }
        }
    }
}
