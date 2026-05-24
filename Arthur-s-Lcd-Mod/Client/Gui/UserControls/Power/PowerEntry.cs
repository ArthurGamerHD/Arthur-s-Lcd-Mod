using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.Tooltip;
using Sandbox.ModAPI;
using VRageMath;

namespace LcdMod.Client.Gui.UserControls.Power
{
    internal sealed class PowerEntry : ControlModelBase
    {
        Func<IList<ITooltipLine>> _getDetails;

        public long EntryId { get; private set; }
        public IMyTerminalBlock Entity { get; private set; }
        public FillableTexture FillableTexture { get; private set; }
        public float Ratio { get; private set; }
        public string PercentText { get; private set; }
        public Color FillColor { get; private set; }
        public bool DrawCenterIcon { get; private set; }
        public float CenterIconRotation { get; private set; }
        public float CenterIconScale { get; private set; }
        public string Icon { get; private set; }

        public string BlockIcon
        {
            get { return Icon; }
            set { Icon = value ?? string.Empty; }
        }

        public PowerEntry(
            long entryId,
            FillableTexture fillableTexture,
            float ratio,
            string percentText,
            Color fillColor,
            bool drawCenterIcon = true,
            float centerIconRotation = 0f,
            float centerIconScale = 1f,
            string blockIcon = "",
            IMyTerminalBlock entity = null,
            Func<IList<ITooltipLine>> getDetails = null)
        {
            Update(
                entryId,
                fillableTexture,
                ratio,
                percentText,
                fillColor,
                drawCenterIcon,
                centerIconRotation,
                centerIconScale,
                blockIcon,
                entity,
                getDetails);
        }

        public void Update(
            long entryId,
            FillableTexture fillableTexture,
            float ratio,
            string percentText,
            Color fillColor,
            bool drawCenterIcon = true,
            float centerIconRotation = 0f,
            float centerIconScale = 1f,
            string blockIcon = "",
            IMyTerminalBlock entity = null,
            Func<IList<ITooltipLine>> getDetails = null)
        {
            EntryId = entryId;
            Entity = entity;
            FillableTexture = fillableTexture;
            Ratio = ratio;
            PercentText = percentText ?? string.Empty;
            FillColor = fillColor;
            DrawCenterIcon = drawCenterIcon;
            CenterIconRotation = centerIconRotation;
            CenterIconScale = centerIconScale;
            Icon = blockIcon ?? string.Empty;
            _getDetails = getDetails ?? _getDetails;
        }

        public IList<ITooltipLine> GetDetails()
        {
            var details = _getDetails?.Invoke();
            if (details != null)
                return details;

            return new List<ITooltipLine>
            {
                new StaticTooltipLine(PercentText)
            };
        }
    }
}
