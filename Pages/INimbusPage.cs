using System.Windows.Controls;

namespace Nimbus.Pages
{
    /// <summary>Every tab implements this so the main window can pop its cards in when it opens.</summary>
    public interface INimbusPage
    {
        /// <summary>The panel whose children pop in one after another.</summary>
        Panel PopRoot { get; }

        /// <summary>Called each time the tab is opened.</summary>
        void OnShown();
    }
}
