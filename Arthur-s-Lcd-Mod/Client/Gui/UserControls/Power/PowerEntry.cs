using LcdMod.Common.Mvvm;
using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRageMath;

namespace LcdMod.Client.Gui.UserControls.Power
{
    internal sealed class PowerEntry : ObservableObject
    {
        Func<IList<PowerDetailLine>> _getDetails;

        public long EntryId { get; private set; }
        public IMyTerminalBlock Entity { get; private set; }
        public string FillClass { get; private set; }
        public float Ratio { get; private set; }
        public string PercentText { get; private set; }
        public Color FillColor { get; private set; }
        public bool DrawCenterIcon { get; private set; }
        public float CenterIconRotation { get; private set; }
        public string Icon { get; private set; }

        public string BlockIcon
        {
            get { return Icon; }
            set { Icon = value ?? string.Empty; }
        }

        public PowerEntry(
            long entryId,
            string fillClass,
            float ratio,
            string percentText,
            Color fillColor,
            bool drawCenterIcon = true,
            float centerIconRotation = 0f,
            string blockIcon = "",
            IMyTerminalBlock entity = null,
            Func<IList<PowerDetailLine>> getDetails = null)
        {
            Update(
                entryId,
                fillClass,
                ratio,
                percentText,
                fillColor,
                drawCenterIcon,
                centerIconRotation,
                blockIcon,
                entity,
                getDetails);
        }

        public void Update(
            long entryId,
            string fillClass,
            float ratio,
            string percentText,
            Color fillColor,
            bool drawCenterIcon = true,
            float centerIconRotation = 0f,
            string blockIcon = "",
            IMyTerminalBlock entity = null,
            Func<IList<PowerDetailLine>> getDetails = null)
        {
            EntryId = entryId;
            Entity = entity;
            FillClass = fillClass;
            Ratio = ratio;
            PercentText = percentText ?? string.Empty;
            FillColor = fillColor;
            DrawCenterIcon = drawCenterIcon;
            CenterIconRotation = centerIconRotation;
            Icon = blockIcon ?? string.Empty;
            _getDetails = getDetails ?? _getDetails;
        }

        /// <summary>True when the entry has its own details (batteries, jump drives) worth a flyout.</summary>
        public bool HasDetails => _getDetails != null;

        public IList<PowerDetailLine> GetDetails()
        {
            var details = _getDetails?.Invoke();
            if (details != null)
                return details;

            return new List<PowerDetailLine> { new PowerDetailLine(PercentText) };
        }
    }
}
