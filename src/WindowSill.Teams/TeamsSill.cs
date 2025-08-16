using CommunityToolkit.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;

using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Diagnostics;

using WindowSill.API;
using WindowSill.Teams.Core;
using WindowSill.Teams.FirstTimeSetup;
using WindowSill.Teams.Settings;
using WindowSill.Teams.UI;

using Path = System.IO.Path;

namespace WindowSill.Teams;

[Export(typeof(ISill))]
[Name("Microsoft Teams")]
[Priority(Priority.Lowest)]
public sealed class TeamsSill : ISillActivatedByDefault, ISillFirstTimeSetup, ISillListView
{
    private readonly ISettingsProvider _settingsProvider;
    private readonly ILogger _logger;
    private readonly Lazy<SillListViewButtonItem> _likeButton;
    private readonly Lazy<SillListViewButtonItem> _loveButton;
    private readonly Lazy<SillListViewButtonItem> _applauseButton;
    private readonly Lazy<SillListViewButtonItem> _laughButton;
    private readonly Lazy<SillListViewButtonItem> _wowButton;
    private readonly Lazy<SillListViewButtonItem> _microphoneButton;
    private readonly Lazy<SillListViewButtonItem> _leaveButton;
    private readonly string _pluginContentDirectory;

    private TeamsClient? _teamsClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private CancellationToken _cancellationToken = CancellationToken.None;

    [ImportingConstructor]
    public TeamsSill(ISettingsProvider settingsProvider, IPluginInfo pluginInfo)
    {
        _settingsProvider = settingsProvider;
        _logger = this.Log();

        _pluginContentDirectory = pluginInfo.GetPluginContentDirectory();

        _likeButton
            = new(() =>
                new(
                    new BitmapImage(new Uri(Path.Combine(_pluginContentDirectory, "Assets", "like.gif"))),
                    "/WindowSill.Teams/Misc/Like".GetLocalizedString(),
                    OnLikeButtonClickAsync));

        _loveButton
            = new(() =>
                new(
                    new BitmapImage(new Uri(Path.Combine(_pluginContentDirectory, "Assets", "love.gif"))),
                    "/WindowSill.Teams/Misc/Love".GetLocalizedString(),
                    OnLoveButtonClickAsync));

        _applauseButton
            = new(() =>
                new(
                    new BitmapImage(new Uri(Path.Combine(_pluginContentDirectory, "Assets", "applause.gif"))),
                    "/WindowSill.Teams/Misc/Applause".GetLocalizedString(),
                    OnApplauseButtonClickAsync));

        _laughButton
            = new(() =>
                new(
                    new BitmapImage(new Uri(Path.Combine(_pluginContentDirectory, "Assets", "laugh.gif"))),
                    "/WindowSill.Teams/Misc/Laugh".GetLocalizedString(),
                    OnLaughButtonClickAsync));

        _wowButton
            = new(() =>
                new(
                    new BitmapImage(new Uri(Path.Combine(_pluginContentDirectory, "Assets", "wow.gif"))),
                    "/WindowSill.Teams/Misc/Wow".GetLocalizedString(),
                    OnWowButtonClickAsync));

        _microphoneButton
            = new(() =>
                new(
                    '\uEC71',
                    "/WindowSill.Teams/Misc/Mute".GetLocalizedString(),
                    OnMicrophoneButtonClickAsync));

        _leaveButton = new(() => LeaveCallSillListViewButtonItem.Create(pluginInfo, OnLeaveCallButtonClickAsync));
    }

    public string DisplayName => "/WindowSill.Teams/Misc/DisplayName".GetLocalizedString();

    public IconElement CreateIcon()
    {
        return new ImageIcon
        {
            Source = new SvgImageSource(new Uri(Path.Combine(_pluginContentDirectory, "Assets", "microsoft_teams_2019.svg")))
        };
    }

    public SillSettingsView[]? SettingsViews =>
        [
        new SillSettingsView(
            DisplayName,
            new(() => new SettingsView()))
        ];

    public ObservableCollection<SillListViewItem> ViewList { get; } = new();

    public SillView? PlaceholderView => null;

    public IFirstTimeSetupContributor[] GetFirstTimeSetupContributors()
    {
        if (Process.GetProcessesByName("ms-teams").Length == 0)
        {
            return [];
        }

        return [new TeamsFirstTimeSetupContributor()];
    }

    public async ValueTask OnActivatedAsync()
    {
        _cancellationTokenSource = new();
        _cancellationToken = _cancellationTokenSource.Token;

        _teamsClient
            = new TeamsClient(
                new TeamsClientOptions(
                    _settingsProvider.GetSetting(Settings.Settings.Token)));

        _teamsClient.Update += TeamsSocket_Update;
        _teamsClient.ServiceResponse += TeamsSocket_ServiceResponse;
        _teamsClient.NewToken += TeamsSocket_NewToken;

        await _teamsClient.ConnectPerpetuallyAsync(_cancellationToken);
    }

    public async ValueTask OnDeactivatedAsync()
    {
        await ThreadHelper.RunOnUIThreadAsync(() =>
        {
            if (_cancellationTokenSource is not null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }

            ViewList.Clear();

            if (_teamsClient is not null)
            {
                _teamsClient.Update -= TeamsSocket_Update;
                _teamsClient.ServiceResponse -= TeamsSocket_ServiceResponse;
                _teamsClient.NewToken -= TeamsSocket_NewToken;
                _teamsClient.Dispose();
            }
        });
    }

    private void TeamsSocket_NewToken(object? sender, string newToken)
    {
        _logger.LogInformation("Microsoft Teams provided a new token");
        _settingsProvider.SetSetting(Settings.Settings.Token, newToken);
    }

    private void TeamsSocket_ServiceResponse(object? sender, Core.Models.ServiceResponse e)
    {
        _logger.LogInformation("Microsoft Teams service responded: {0}", e.Response);
    }

    private void TeamsSocket_Update(object? sender, Core.Models.MeetingUpdate e)
    {
        _logger.LogInformation("Microsoft Teams provided an update.");

        ThreadHelper.RunOnUIThreadAsync(() =>
        {
            ViewList.Clear();

            if (!_cancellationToken.IsCancellationRequested && e.MeetingPermissions is not null)
            {
                if (e.MeetingPermissions.CanReact)
                {
                    ViewList.Add(_likeButton.Value);
                    ViewList.Add(_loveButton.Value);
                    ViewList.Add(_applauseButton.Value);
                    ViewList.Add(_laughButton.Value);
                    ViewList.Add(_wowButton.Value);
                }

                if (e.MeetingPermissions.CanToggleMute)
                {
                    if (e.MeetingState is not null && e.MeetingState.IsMuted)
                    {
                        _microphoneButton.Value.Content = '\uF781';
                        _microphoneButton.Value.PreviewFlyoutContent = "/WindowSill.Teams/Misc/Unmute".GetLocalizedString();
                    }
                    else
                    {
                        _microphoneButton.Value.Content = '\uEC71';
                        _microphoneButton.Value.PreviewFlyoutContent = "/WindowSill.Teams/Misc/Mute".GetLocalizedString();
                    }

                    ViewList.Add(_microphoneButton.Value);
                }

                if (e.MeetingPermissions.CanLeave)
                {
                    ViewList.Add(_leaveButton.Value);
                }
            }
        }).Forget();
    }

    private async Task OnLikeButtonClickAsync()
    {
        Guard.IsNotNull(_teamsClient);
        await _teamsClient.ThumbsUpAsync(_cancellationToken);
    }

    private async Task OnLoveButtonClickAsync()
    {
        Guard.IsNotNull(_teamsClient);
        await _teamsClient.LoveAsync(_cancellationToken);
    }

    private async Task OnApplauseButtonClickAsync()
    {
        Guard.IsNotNull(_teamsClient);
        await _teamsClient.ApplauseAsync(_cancellationToken);
    }

    private async Task OnLaughButtonClickAsync()
    {
        Guard.IsNotNull(_teamsClient);
        await _teamsClient.LaughAsync(_cancellationToken);
    }

    private async Task OnWowButtonClickAsync()
    {
        Guard.IsNotNull(_teamsClient);
        await _teamsClient.WowAsync(_cancellationToken);
    }

    private async Task OnMicrophoneButtonClickAsync()
    {
        Guard.IsNotNull(_teamsClient);
        await _teamsClient.ToggleMuteAsync(_cancellationToken);
    }

    private async Task OnLeaveCallButtonClickAsync()
    {
        Guard.IsNotNull(_teamsClient);
        await _teamsClient.LeaveCallAsync(_cancellationToken);
    }
}
