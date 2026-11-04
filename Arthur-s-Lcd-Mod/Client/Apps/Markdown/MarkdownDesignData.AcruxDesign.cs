using LcdMod.Client.Markdown;

namespace LcdMod.Client.Apps.Markdown
{
    /// <summary>
    /// Design-time data for <see cref="MarkdownView"/>, used by the IDE preview through
    /// <c>Design.DataContext="{x:Static md:MarkdownDesignData.Sample}"</c>: a document using the supported markup.
    /// A *.AcruxDesign.cs file: compiled for the preview, not packed by MDK.
    /// </summary>
    internal static class MarkdownDesignData
    {
        const string Text = @"# Mining Outpost Theta

Welcome aboard. Read the **safety rules** before undocking.

## Docking
1. Approach the connector at *under 5 m/s*.
2. Lock the connector, then switch thrusters off.
3. Report to the `Control Room`.

## Status
- Refinery: online
- Assemblers: **3/4** running
- Hydrogen: low, refill at pad B

> Unauthorized grids are shot on sight.

---
Questions? Ask the station master.";

        public static MarkdownViewModel Sample
        {
            get
            {
                var viewModel = new MarkdownViewModel(new DesignMarkdownHost { Document = new MarkdownParser().Parse(Text) });
                viewModel.Refresh();
                return viewModel;
            }
        }

        sealed class DesignMarkdownHost : IMarkdownHost
        {
            public MarkdownDocument Document { get; set; }
        }
    }
}
