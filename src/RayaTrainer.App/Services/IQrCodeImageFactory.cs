using System.Windows.Media;

namespace RayaTrainer.App.Services;

public interface IQrCodeImageFactory
{
    ImageSource Create(string content);
}
