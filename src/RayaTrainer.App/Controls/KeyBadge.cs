using System.Windows;
using System.Windows.Controls;

namespace RayaTrainer.App.Controls;

public sealed class KeyBadge : ContentControl
{
    static KeyBadge()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(KeyBadge),
            new FrameworkPropertyMetadata(typeof(KeyBadge)));
    }
}
