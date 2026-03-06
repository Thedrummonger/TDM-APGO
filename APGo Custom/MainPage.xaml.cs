
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Packets;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Text.Json;

namespace APGo_Custom;

public partial class MainPage : ContentPage
{
    public bool _isTracking = false;
    public List<BaseLocation> _setupLocations = new List<BaseLocation>();
    public Dictionary<string, APLocation> _activeLocationMapping = new Dictionary<string, APLocation>();
    public ArchipelagoSession? _session = null;
    public string? _currentRoomHash = null;
    public bool _mapLoaded = false;
    public Timer? _refreshTimer = null;
    public bool _needsRefresh = false;
    public Queue<object> _chatMessageQueue = new();
    private Timer? _chatProcessTimer = null;
    private const int MaxMessagesPerSecond = 10;
    public bool IsConnecting = false;

    public HashSet<char> GoalItemsRecieved = [];
    public GoalSetting GoalSetting = GoalSetting.option_short_macguffin;

    public SettingsPage SettingsPage;

    public (double Lat, double Long)? LastKnownLocation = null;
    public BaseLocation? AnchorMarker = null;

    public bool HasActiveAP => _session != null && _session.Socket.Connected;
    public bool IsInReconnectState => _session != null && !_session.Socket.Connected;

    public MainPage()
    {
        InitializeComponent();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        var ConnectionCache = await DataFileHelpers.LoadLastConnectionCache();
        var USerSettings = await DataFileHelpers.LoadUserSettings();
        SettingsPage = new SettingsPage(this, ConnectionCache, USerSettings, MapWebView);
        await OpenStreetMapHelpers.LoadMapAsync(this, MapWebView);
        await DataFileHelpers.LoadSetupLocations(this, MapWebView);
        OpenStreetMapHelpers.StartLocationTracking(this, MapWebView);
        _refreshTimer = new Timer(DoRefresh, null, 1000, 1000);
        _chatProcessTimer = new Timer(ProcessChatQueue, null, 100, 100);
    }

    public void DoRefresh(object? state)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_session != null && !_session.Socket.Connected)
            {
                await APConnectionHelpers.DisconnectFromArchipelago(this, MapWebView, ConnectionButton);
                return;
            }
            if (!_needsRefresh || !HasActiveAP) return;

            _needsRefresh = false;
            Debug.WriteLine("Refreshing map markers");

            var checkedLocations = _session!.Locations.AllLocationsChecked;
            var CurrentKeyCount = _session.Items.AllItemsReceived.Where(x => x.ItemName == "Progressive Key").Count();
            var HintCache = _session.CreateHintCache();

            foreach (var mapping in _activeLocationMapping)
            {
                if (checkedLocations.Contains(mapping.Value.ArchipelagoLocationId))
                {
                    await MapWebView.EvaluateJavaScriptAsync($"removeMarker('{mapping.Key}');");
                    continue;
                }

                (string borderColor, string fillColor) = MarkerHelpers.GetMarkerColor(_session, mapping.Value, CurrentKeyCount, HintCache);
                await MapWebView.EvaluateJavaScriptAsync($"updateMarkerColor('{mapping.Value.Id}', '{borderColor}', '{fillColor}');");
            }

            foreach (var i in _session.Items.AllItemsReceived)
            {
                //Track the "letter" items for the goal
                if (i.ItemName.Length == 1 && APLocationHelpers.LongGoal.Contains(i.ItemName[0]))
                    GoalItemsRecieved.Add(i.ItemName[0]);
            }
            if (APLocationHelpers.HasGoal(this))
                _session.SetGoalAchieved();
        });
    }

    public void OnLocationButtonClicked(object sender, EventArgs e) => OpenStreetMapHelpers.FocusCurrentLocation(this, MapWebView);

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        //_isTracking = false;
    }

    public async void OnMapNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (e.Url.StartsWith("mapholdclick://"))
        {
            e.Cancel = true;
            if (_session == null) 
                await MarkerHelpers.AddMarker(this, e, MapWebView);
        }
        else if (e.Url.StartsWith("markerclick://"))
        {
            e.Cancel = true;
            if (_session == null)
                await MarkerHelpers.RemoveMarker(this, e, MapWebView);
            else
                await APLocationHelpers.DisplayLocationDetails(this, MapWebView, e);
        }
    }

    public async void OnConnectionButtonClicked(object sender, EventArgs e)
    {
        if (_session != null)
        {
            bool disconnect = await DisplayAlert("Disconnect",
                "Do you want to disconnect from Archipelago?",
                "Yes", "No");

            if (disconnect)
            {
               await APConnectionHelpers.DisconnectFromArchipelago(this, MapWebView, ConnectionButton);
            }
        }
        else
        {
            //await APConnectionHelpers.ShowConnectionDialog(this, MapWebView, ConnectionButton);
            await APConnectionHelpers.ConnectToArchipelago(this, MapWebView, SettingsPage.ConnectionDetails, ConnectionButton);
            _needsRefresh = true;
        }
    }
    
    public void OnItemReceived(Archipelago.MultiClient.Net.Helpers.ReceivedItemsHelper helper)
    {
        _needsRefresh = true;
    }

    public void OnLocationsChecked(System.Collections.ObjectModel.ReadOnlyCollection<long> newCheckedLocations)
    {
        _needsRefresh = true;
    }

    private void OnSendMessageClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ChatInput.Text) || _session == null)
            return;

        _session.Say(ChatInput.Text);
        ChatInput.Text = "";
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        if (SettingsPage is null) return;
        await Navigation.PushModalAsync(SettingsPage);
    }

    public void OnArchipelagoMessageReceived(LogMessage message)
    {
        if (message is HintItemSendLogMessage)
            _needsRefresh = true;
        Debug.WriteLine($"Chat Message Recieved: {message}");
        _chatMessageQueue.Enqueue(message);
    }

    private void ProcessChatQueue(object? state)
    {
        if (_chatMessageQueue.Count == 0) return;

        ProcessChatQueueOnMainThread();
    }
    private bool _isAtBottom = true;
    private void ProcessChatQueueOnMainThread()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            int messagesToProcess = Math.Min(_chatMessageQueue.Count, MaxMessagesPerSecond / 10);

            Debug.WriteLine($"Processing Message: {messagesToProcess}");

            for (int i = 0; i < messagesToProcess && _chatMessageQueue.Count > 0; i++)
            {
                var message = _chatMessageQueue.Dequeue();
                Debug.WriteLine($"Adding To chat: {message}");
                AddChatMessage(message);
            }

            while (ChatMessages.Children.Count > 100)
            {
                ChatMessages.Children.RemoveAt(0);
            }

            var lastChild = ChatMessages.Children.LastOrDefault();
            if (_isAtBottom && lastChild != null)
            {
                await Task.Delay(100);
                await ChatScrollView.ScrollToAsync(ChatMessages, ScrollToPosition.End, false);
            }
        });
    }

    private void OnChatScrolled(object sender, ScrolledEventArgs e)
    {
        var scrollView = (ScrollView)sender;
        var scrollHeight = scrollView.ContentSize.Height - scrollView.Height;

        // Consider "at bottom" if within 50 pixels of the bottom
        _isAtBottom = e.ScrollY >= scrollHeight - 50;
    }

    public void AddChatMessage(object message)
    {
        var label = new Label
        {
            Padding = new Thickness(3),
            LineBreakMode = LineBreakMode.WordWrap
        };
        if (message is LogMessage APLogMessage)
        {
            var formattedString = new FormattedString();
            foreach (var part in APLogMessage.Parts)
            {
                var span = new Span
                {
                    Text = part.Text,
                    TextColor = Color.FromRgb(part.Color.R, part.Color.G, part.Color.B)
                };
                formattedString.Spans.Add(span);
            }
            label.FormattedText = formattedString;
        }
        else
        {
            label.Text = message.ToString();
        }
        ChatMessages.Add(label);
    }

    private bool _isChatVisible = true;

    private void OnToggleChatClicked(object sender, EventArgs e)
    {
        _isChatVisible = !_isChatVisible;

        MainGrid.RowDefinitions.Clear();

        if (_isChatVisible)
        {
            MainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
            MainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }
        else
        {
            MainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            MainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0, GridUnitType.Absolute) });
        }

        ChatGrid.IsVisible = _isChatVisible;
    }
}