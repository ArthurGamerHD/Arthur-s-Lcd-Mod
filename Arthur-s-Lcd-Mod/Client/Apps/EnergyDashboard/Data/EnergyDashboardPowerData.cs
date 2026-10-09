using System;
using System.Collections.Generic;
using LcdMod.Client.Modules.Power;
using VRageMath;

namespace LcdMod.Client.Apps.EnergyDashboard
{
    internal sealed class EnergyDashboardPowerRow
    {
        public PowerSubtypeSnapshot Entry;
        public EnergyDashboardPowerCategory Category;
        public Color Color;
        public string SpriteName;
        public double CurrentW;
        public double MaxW;
        public double RatioDenominatorW;
        public bool IsCharge;
        public bool Selected;
        public bool Hover;
        public long UpdateToken;
        public EnergyPowerRowModel Model;
    }

    internal enum EnergyDashboardPowerCategory
    {
        Consumer,
        Producer,
        Charge
    }

    internal static class EnergyDashboardPowerMetrics
    {
        public const int MAX_GRAPH_SERIES = 8;

        public static double GetCurrentProducerTotal(PowerSnapshot snapshot)
        {
            return snapshot.Producers.KnownCurrentOutputW;
        }

        public static double GetCurrentConsumerTotal(PowerSnapshot snapshot)
        {
            return snapshot.TotalRequiredInputW;
        }

        public static double GetMaxConsumerTotal(PowerSnapshot snapshot)
        {
            double total = 0.0;
            var entries = snapshot.ConsumerSubtypes;
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry != null && entry.MaxW > 0.0)
                        total += entry.MaxW;
                }
            }

            return Math.Max(snapshot.TotalRequiredInputW, total);
        }

        /// <summary>
        /// Demand against the supply available right now, as the vanilla power usage HUD measures it, so a grid asking for
        /// more than it can produce reads as fully loaded.
        /// </summary>
        public static float LoadRatio(PowerSnapshot snapshot)
        {
            if (snapshot.MaxAvailableW <= 0)
                return snapshot.TotalRequiredInputW > 0 ? 1f : 0f;
            return Ratio(snapshot.TotalRequiredInputW, snapshot.MaxAvailableW);
        }

        public static double GetChargeTotal(PowerSnapshot snapshot)
        {
            double total = 0.0;
            var entries = snapshot.ChargeSubtypes;
            if (entries == null)
                return total;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null)
                    total += entry.CurrentW;
            }

            return total;
        }

        public static double GetGraphMax(List<PowerSnapshot> snapshots, List<EnergyDashboardPowerRow> rows,
            bool producers, bool charge, EnergyDashboardPowerRow selectedRow = null)
        {
            if (charge)
                return GetMaxChargeSubtypeCapacity(rows);

            double max = 0;
            if (snapshots != null)
            {
                for (int i = 0; i < snapshots.Count; i++)
                {
                    double total = producers
                        ? GetCurrentProducerTotal(snapshots[i])
                        : GetCurrentConsumerTotal(snapshots[i]);
                    if (total > max)
                        max = total;
                }
            }

            int seriesCount = Math.Min(MAX_GRAPH_SERIES, rows?.Count ?? 0);
            for (int r = 0; r < seriesCount; r++)
            {
                if (rows == null) continue;

                var row = rows[r];
                if (row.CurrentW > max)
                    max = row.CurrentW;

                if (snapshots == null || row.Entry == null)
                    continue;

                for (int i = 0; i < snapshots.Count; i++)
                {
                    double value = GetSubtypeValue(snapshots[i], row.Entry.Key, producers, false);
                    if (value > max)
                        max = value;
                }
            }

            if (selectedRow != null)
                AddSubtypeMax(snapshots, selectedRow, producers, false, ref max);

            return max;
        }

        static void AddSubtypeMax(List<PowerSnapshot> snapshots, EnergyDashboardPowerRow row,
            bool producers, bool charge, ref double max)
        {
            if (row == null || row.Entry == null || string.IsNullOrEmpty(row.Entry.Key))
                return;

            if (row.CurrentW > max)
                max = row.CurrentW;

            if (snapshots == null)
                return;

            var key = row.Entry.Key;
            for (int i = 0; i < snapshots.Count; i++)
            {
                double value = GetSubtypeValue(snapshots[i], key, producers, charge);
                if (value > max)
                    max = value;
            }
        }

        static double GetMaxChargeSubtypeCapacity(List<EnergyDashboardPowerRow> rows)
        {
            double max = 0.0;
            if (rows == null)
                return max;

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].MaxW > max)
                    max = rows[i].MaxW;
            }

            return max;
        }

        public static double GetSubtypeValue(PowerSnapshot snapshot, string key, bool producers, bool charge)
        {
            var entries = charge ? snapshot.ChargeSubtypes :
                producers ? snapshot.ProducerSubtypes : snapshot.ConsumerSubtypes;
            if (entries == null || string.IsNullOrEmpty(key))
                return 0;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null && string.Equals(entry.Key, key, StringComparison.Ordinal))
                    return entry.CurrentW;
            }

            return 0;
        }

        public static float Ratio(double current, double max)
        {
            if (max <= 0)
                return 0f;
            return (float)Math.Max(0, Math.Min(1, current / max));
        }
    }
}
