using System.Collections.ObjectModel;
using System.Windows.Media;
using RayaTrainer.App.Services;

namespace RayaTrainer.App.ViewModels;

public sealed class MobileRemoteAccessViewModel : ViewModelBase
{
    private readonly IMobileRemoteLinkProvider _linkProvider;
    private readonly IQrCodeImageFactory _qrCodeImageFactory;
    private readonly IMobileRemoteAvailability _availability;
    private readonly Action<string> _setStatusMessage;
    private string _remoteUrl = "尚未生成";
    private ImageSource? _qrCodeImage;
    private LanAddressEntry? _selectedAddress;

    public MobileRemoteAccessViewModel(
        IMobileRemoteLinkProvider linkProvider,
        IQrCodeImageFactory qrCodeImageFactory,
        IMobileRemoteAvailability availability,
        Action<string> setStatusMessage)
    {
        _linkProvider = linkProvider;
        _qrCodeImageFactory = qrCodeImageFactory;
        _availability = availability;
        _setStatusMessage = setStatusMessage;
        GenerateQrCodeCommand = new RelayCommand(GenerateQrCode, () => _availability.IsAvailable);

        var addresses = _linkProvider.GetAvailableAddresses();
        AvailableAddresses = new ObservableCollection<LanAddressEntry>(addresses);
        SelectedAddress = addresses.FirstOrDefault();
    }

    public RelayCommand GenerateQrCodeCommand { get; }

    public ObservableCollection<LanAddressEntry> AvailableAddresses { get; }

    public LanAddressEntry? SelectedAddress
    {
        get => _selectedAddress;
        set
        {
            if (Equals(_selectedAddress, value))
            {
                return;
            }

            _selectedAddress = value;
            OnPropertyChanged();
            if (value is not null)
            {
                GenerateQrCode();
            }
        }
    }

    public string RemoteUrl
    {
        get => _remoteUrl;
        private set
        {
            if (_remoteUrl == value)
            {
                return;
            }

            _remoteUrl = value;
            OnPropertyChanged();
        }
    }

    public ImageSource? QrCodeImage
    {
        get => _qrCodeImage;
        private set
        {
            if (ReferenceEquals(_qrCodeImage, value))
            {
                return;
            }

            _qrCodeImage = value;
            OnPropertyChanged();
        }
    }

    public string GenerateQrCodeHelpText => "生成手机遥控页面的局域网访问链接和二维码；手机扫码后仍需在电脑端允许设备配对。";

    private void GenerateQrCode()
    {
        if (!_availability.IsAvailable)
        {
            _setStatusMessage($"手机遥控服务不可用：{_availability.UnavailableReason}");
            return;
        }

        try
        {
            var ip = SelectedAddress?.IpAddress;
            var remoteUrl = ip is not null
                ? _linkProvider.CreateRemoteUrl(ip)
                : _linkProvider.CreateRemoteUrl();
            RemoteUrl = remoteUrl;
            QrCodeImage = _qrCodeImageFactory.Create(remoteUrl);
            _setStatusMessage($"手机遥控二维码已生成：{remoteUrl}");
        }
        catch (Exception ex)
        {
            _setStatusMessage($"生成手机遥控二维码失败：{ex.Message}");
        }
    }
}
