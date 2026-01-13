namespace APGo_Custom;

public partial class SettingsPage : ContentPage
{
    private readonly MainPage MainPage;
    public SettingsPage(MainPage parent, ConnectionDetails? connectionCache)
    {
        InitializeComponent();

        ServerAddress = connectionCache?.Host ?? "archipelago.gg";
        Port = connectionCache?.Port?.ToString() ?? "38281";
        SlotName = connectionCache?.Slot ?? string.Empty;
        Password = connectionCache?.Password ?? string.Empty;

        MainPage = parent;
        ServerEntry.Text = ServerAddress;
        PortEntry.Text = Port;
        SlotEntry.Text = SlotName;
        PasswordEntry.Text = Password;
    }

    public string ServerAddress;
    public string Port;
    public string SlotName;
    public string Password;

    private void OnServerChanged(object sender, TextChangedEventArgs e)
    {
        ServerAddress = e.NewTextValue;
    }

    private void OnPortChanged(object sender, TextChangedEventArgs e)
    {
        Port = e.NewTextValue;
    }

    private void OnSlotChanged(object sender, TextChangedEventArgs e)
    {
        SlotName = e.NewTextValue;
    }

    private void OnPasswordChanged(object sender, TextChangedEventArgs e)
    {
        Password = e.NewTextValue;
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}