using System;
using System.Collections.Generic;
using Acrux.Core.Input;
using Adk.Mvvm;

namespace LcdMod.Client.Apps.Projector
{
    internal sealed class ProjectorViewModel : ObservableObject
    {
        public Inventory.InventoryViewModel Inventory { get; } = new Inventory.InventoryViewModel();
        public Action<ProjectorListMode> SelectMode { get; set; }
        public IReadOnlyList<ProjectorListMode> Modes { get; set; } =
            new[] { ProjectorListMode.Components, ProjectorListMode.Ingots, ProjectorListMode.Blocks };
        public Func<ProjectorListMode, string> ModeLabel { get; set; } = mode => mode.ToString();
        public ICommand CraftAllCommand { get; set; }
        public string CraftText => "Craft all";

        string _blocksText, _componentsText, _ingotsText;
        ProjectorListMode _mode;
        float _blocksProgress, _componentsProgress, _ingotsProgress;
        bool _showFooter, _canCraft;
        public string BlocksText => _blocksText;
        public string ComponentsText => _componentsText;
        public string IngotsText => _ingotsText;
        public ProjectorListMode Mode => _mode;
        public float BlocksProgress => _blocksProgress;
        public float ComponentsProgress => _componentsProgress;
        public float IngotsProgress => _ingotsProgress;
        public bool ShowFooter => _showFooter;
        public bool CanCraft => _canCraft;

        public void Update(bool showFooter, bool canCraft, float blocks, float components, float ingots,
            string blocksText, string componentsText, string ingotsText, ProjectorListMode mode)
        {
            RaiseAndSetIfChanged(ref _showFooter, showFooter, nameof(ShowFooter));
            RaiseAndSetIfChanged(ref _canCraft, canCraft, nameof(CanCraft));
            RaiseAndSetIfChanged(ref _blocksProgress, blocks, nameof(BlocksProgress));
            RaiseAndSetIfChanged(ref _componentsProgress, components, nameof(ComponentsProgress));
            RaiseAndSetIfChanged(ref _ingotsProgress, ingots, nameof(IngotsProgress));
            RaiseAndSetIfChanged(ref _blocksText, blocksText, nameof(BlocksText));
            RaiseAndSetIfChanged(ref _componentsText, componentsText, nameof(ComponentsText));
            RaiseAndSetIfChanged(ref _ingotsText, ingotsText, nameof(IngotsText));
            RaiseAndSetIfChanged(ref _mode, mode, nameof(Mode));
        }
    }
}
