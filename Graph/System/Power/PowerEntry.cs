using System;
using System.Collections.Generic;
using Graph.Apps.Utility;
using Sandbox.ModAPI;
using VRageMath;

namespace Graph.System.Power
{
    internal sealed class PowerEntry
    {
        readonly Func<IList<ITooltipLine>> _getDetails;

        public long EntryId { get; }
        public IMyTerminalBlock Entity { get; }
        public FillableTexture FillableTexture { get; }
        public float Ratio { get; }
        public string PercentText { get; }
        public Color FillColor { get; }
        public bool DrawCenterIcon { get; }
        public float CenterIconRotation { get; }
        public float CenterIconScale { get; }
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
            EntryId = entryId;
            Entity = entity;
            FillableTexture = fillableTexture;
            Ratio = ratio;
            PercentText = percentText;
            FillColor = fillColor;
            DrawCenterIcon = drawCenterIcon;
            CenterIconRotation = centerIconRotation;
            CenterIconScale = centerIconScale;
            Icon = blockIcon ?? string.Empty;
            _getDetails = getDetails;
        }

        public IList<ITooltipLine> GetDetails()
        {
            var details = _getDetails != null ? _getDetails() : null;
            if (details != null)
                return details;

            return new List<ITooltipLine>
            {
                new StaticTooltipLine(PercentText)
            };
        }
    }
}
